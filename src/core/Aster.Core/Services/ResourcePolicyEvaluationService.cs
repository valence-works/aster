using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Services;

/// <summary>
/// Default non-mutating policy preview service.
/// </summary>
public sealed class ResourcePolicyEvaluationService : IResourcePolicyEvaluationService
{
    private readonly IResourceDefinitionStore definitionStore;
    private readonly IResourceVersionReader versionReader;
    private readonly IResourceLifecycleMarkerStore markerStore;
    private readonly IResourcePolicyValidator validator;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourcePolicyEvaluationService"/>.
    /// </summary>
    public ResourcePolicyEvaluationService(
        IResourceDefinitionStore definitionStore,
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerStore markerStore,
        IResourcePolicyValidator validator)
    {
        ArgumentNullException.ThrowIfNull(definitionStore);
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(markerStore);
        ArgumentNullException.ThrowIfNull(validator);
        this.definitionStore = definitionStore;
        this.versionReader = versionReader;
        this.markerStore = markerStore;
        this.validator = validator;
    }

    /// <inheritdoc />
    public async ValueTask<ResourcePolicyEvaluationPreview> PreviewAsync(
        ResourcePolicyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = TenantScopeResolver.Resolve(request.TenantScope);
        var diagnostics = new List<ResourcePolicyDiagnostic>();
        var candidates = new List<ResourcePolicyCandidateOutcome>();
        var definitions = (await definitionStore.ListDefinitionsAsync(tenant, cancellationToken)).ToList();

        if (request.DefinitionIds.Count > 0)
        {
            definitions = definitions
                .Where(definition => request.DefinitionIds.Contains(definition.DefinitionId))
                .ToList();
        }

        var resources = (await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = tenant,
            Scope = ResourceVersionScope.AllVersions,
        }, cancellationToken)).ToList();
        var markers = await markerStore.GetMarkersAsync(
            resources.Select(static resource => resource.ResourceId).Distinct(StringComparer.Ordinal),
            tenant,
            cancellationToken);
        var activeVersionKeysByChannel = new Dictionary<string, IReadOnlySet<ResourceVersionKey>>(StringComparer.Ordinal);
        IReadOnlySet<ResourceVersionKey>? draftVersionKeys = null;

        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validation = validator.Validate(definition);
            diagnostics.AddRange(validation.Diagnostics);

            if (!validation.IsValid)
                continue;

            foreach (var policy in SelectPolicies(definition, request))
            {
                if (policy.Criteria.MinimumAge is not null && request.EvaluationTimestamp is null)
                {
                    diagnostics.Add(ResourcePolicyValidator.Diagnostic(
                        ResourcePolicyDiagnosticCodes.PolicyEvaluationTimestampRequired,
                        $"Policy '{policy.PolicyId}' uses age-based criteria and requires an evaluation timestamp.",
                        "evaluationTimestamp",
                    policy.PolicyId));
                    continue;
                }

                var activationMatches = await GetActivationMatchesAsync(
                    policy,
                    tenant,
                    activeVersionKeysByChannel,
                    draftVersionKeys,
                    cancellationToken);
                if (policy.Criteria.ActivationState == ResourcePolicyActivationState.Draft)
                    draftVersionKeys = activationMatches;

                var definitionResources = resources
                    .Where(resource => string.Equals(resource.DefinitionId, definition.DefinitionId, StringComparison.Ordinal))
                    .ToList();

                if (policy.Kind == ResourcePolicyKind.VersionPruning)
                    AddPruningCandidates(policy, definitionResources, activationMatches, candidates, diagnostics);
                else
                    AddResourceCandidates(policy, definitionResources, request.EvaluationTimestamp, markers, activationMatches, candidates);
            }
        }

        return new ResourcePolicyEvaluationPreview
        {
            TenantScope = tenant,
            EvaluationTimestamp = request.EvaluationTimestamp,
            Candidates = candidates,
            Diagnostics = diagnostics,
        };
    }

    private static IEnumerable<ResourcePolicyDeclaration> SelectPolicies(
        ResourceDefinition definition,
        ResourcePolicyEvaluationRequest request) =>
        request.PolicyIds.Count == 0
            ? definition.PolicyDeclarations
            : definition.PolicyDeclarations.Where(policy => request.PolicyIds.Contains(policy.PolicyId));

    private static void AddResourceCandidates(
        ResourcePolicyDeclaration policy,
        IReadOnlyList<Resource> resources,
        DateTimeOffset? evaluationTimestamp,
        IReadOnlyDictionary<string, ResourceLifecycleMarker> markers,
        IReadOnlySet<ResourceVersionKey>? activationMatches,
        ICollection<ResourcePolicyCandidateOutcome> candidates)
    {
        foreach (var resource in LatestVersions(resources))
        {
            if (!MatchesAge(policy, resource, evaluationTimestamp))
                continue;

            if (!MatchesActivation(policy, resource, activationMatches))
                continue;

            if (!MatchesLifecycle(policy, resource.ResourceId, markers))
                continue;

            candidates.Add(new ResourcePolicyCandidateOutcome
            {
                PolicyId = policy.PolicyId,
                PolicyKind = policy.Kind,
                Outcome = policy.Outcome,
                ResourceId = resource.ResourceId,
                ResourceVersion = resource.Version,
                Reason = $"Resource '{resource.ResourceId}' matched policy '{policy.PolicyId}'.",
            });
        }
    }

    private static void AddPruningCandidates(
        ResourcePolicyDeclaration policy,
        IReadOnlyList<Resource> resources,
        IReadOnlySet<ResourceVersionKey>? activationMatches,
        ICollection<ResourcePolicyCandidateOutcome> candidates,
        ICollection<ResourcePolicyDiagnostic> diagnostics)
    {
        var retainedVersions = policy.Criteria.MaximumRetainedVersions.GetValueOrDefault();
        foreach (var group in resources.GroupBy(static resource => resource.ResourceId, StringComparer.Ordinal))
        {
            var ordered = group
                .Where(resource => MatchesActivation(policy, resource, activationMatches))
                .OrderByDescending(static resource => resource.Version)
                .ToList();
            var pruneCandidates = ordered.Skip(retainedVersions).ToList();
            if (pruneCandidates.Count == ordered.Count && ordered.Count > 0)
            {
                diagnostics.Add(ResourcePolicyValidator.Diagnostic(
                    ResourcePolicyDiagnosticCodes.PolicyPruningUnsafe,
                    $"Policy '{policy.PolicyId}' would prune every version of resource '{group.Key}'.",
                    policyId: policy.PolicyId,
                    resourceId: group.Key));
                continue;
            }

            foreach (var resource in pruneCandidates)
            {
                candidates.Add(new ResourcePolicyCandidateOutcome
                {
                    PolicyId = policy.PolicyId,
                    PolicyKind = policy.Kind,
                    Outcome = ResourcePolicyOutcome.PrunePreview,
                    ResourceId = resource.ResourceId,
                    ResourceVersion = resource.Version,
                    Reason = $"Resource '{resource.ResourceId}' version {resource.Version} is outside the retained version count.",
                });
            }
        }
    }

    private static IEnumerable<Resource> LatestVersions(IEnumerable<Resource> resources) =>
        resources
            .GroupBy(static resource => resource.ResourceId, StringComparer.Ordinal)
            .Select(static group => group.OrderByDescending(static resource => resource.Version).First());

    private static bool MatchesAge(
        ResourcePolicyDeclaration policy,
        Resource resource,
        DateTimeOffset? evaluationTimestamp)
    {
        if (policy.Criteria.MinimumAge is not { } minimumAge)
            return true;

        var created = new DateTimeOffset(DateTime.SpecifyKind(resource.Created, DateTimeKind.Utc));
        return evaluationTimestamp!.Value - created >= minimumAge;
    }

    private static bool MatchesLifecycle(
        ResourcePolicyDeclaration policy,
        string resourceId,
        IReadOnlyDictionary<string, ResourceLifecycleMarker> markers)
    {
        if (policy.Criteria.LifecycleState is not { } expected)
            return true;

        var actual = markers.TryGetValue(resourceId, out var marker)
            ? marker.State
            : ResourceLifecycleMarkerState.None;

        return actual == expected;
    }

    private async ValueTask<IReadOnlySet<ResourceVersionKey>?> GetActivationMatchesAsync(
        ResourcePolicyDeclaration policy,
        TenantScope tenant,
        IDictionary<string, IReadOnlySet<ResourceVersionKey>> activeVersionKeysByChannel,
        IReadOnlySet<ResourceVersionKey>? draftVersionKeys,
        CancellationToken cancellationToken)
    {
        return policy.Criteria.ActivationState switch
        {
            null => null,
            ResourcePolicyActivationState.Active => await GetActiveVersionKeysAsync(
                tenant,
                policy.Criteria.ActivationChannel!,
                activeVersionKeysByChannel,
                cancellationToken),
            ResourcePolicyActivationState.Draft => draftVersionKeys ?? await ReadVersionKeysAsync(
                tenant,
                ResourceVersionScope.Draft,
                activationChannel: null,
                cancellationToken),
            _ => null,
        };
    }

    private async ValueTask<IReadOnlySet<ResourceVersionKey>> GetActiveVersionKeysAsync(
        TenantScope tenant,
        string channel,
        IDictionary<string, IReadOnlySet<ResourceVersionKey>> activeVersionKeysByChannel,
        CancellationToken cancellationToken)
    {
        if (activeVersionKeysByChannel.TryGetValue(channel, out var cached))
            return cached;

        var keys = await ReadVersionKeysAsync(tenant, ResourceVersionScope.Active, channel, cancellationToken);
        activeVersionKeysByChannel[channel] = keys;
        return keys;
    }

    private async ValueTask<IReadOnlySet<ResourceVersionKey>> ReadVersionKeysAsync(
        TenantScope tenant,
        ResourceVersionScope scope,
        string? activationChannel,
        CancellationToken cancellationToken)
    {
        var versions = await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = tenant,
            Scope = scope,
            ActivationChannel = activationChannel,
        }, cancellationToken);

        return versions
            .Select(static resource => new ResourceVersionKey(resource.ResourceId, resource.Version))
            .ToHashSet();
    }

    private static bool MatchesActivation(
        ResourcePolicyDeclaration policy,
        Resource resource,
        IReadOnlySet<ResourceVersionKey>? activationMatches) =>
        policy.Criteria.ActivationState is null
        || (activationMatches?.Contains(new ResourceVersionKey(resource.ResourceId, resource.Version)) ?? false);

    private readonly record struct ResourceVersionKey(string ResourceId, int Version);
}
