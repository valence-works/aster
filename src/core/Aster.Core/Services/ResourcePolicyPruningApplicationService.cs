using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Services;

/// <summary>
/// Default host-controlled policy pruning application service.
/// </summary>
public sealed class ResourcePolicyPruningApplicationService : IResourcePolicyPruningApplicationService
{
    private readonly IResourceDefinitionStore definitionStore;
    private readonly IResourceVersionReader versionReader;
    private readonly IResourceLifecycleMarkerStore markerStore;
    private readonly IResourceVersionPruningStore pruningStore;
    private readonly IResourcePolicyValidator policyValidator;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourcePolicyPruningApplicationService"/>.
    /// </summary>
    public ResourcePolicyPruningApplicationService(
        IResourceDefinitionStore definitionStore,
        IResourceVersionReader versionReader,
        IResourceLifecycleMarkerStore markerStore,
        IResourceVersionPruningStore pruningStore,
        IResourcePolicyValidator policyValidator)
    {
        ArgumentNullException.ThrowIfNull(definitionStore);
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(markerStore);
        ArgumentNullException.ThrowIfNull(pruningStore);
        ArgumentNullException.ThrowIfNull(policyValidator);
        this.definitionStore = definitionStore;
        this.versionReader = versionReader;
        this.markerStore = markerStore;
        this.pruningStore = pruningStore;
        this.policyValidator = policyValidator;
    }

    /// <inheritdoc />
    public async ValueTask<ResourcePolicyPruningApplicationResult> ApplyAsync(
        ResourcePolicyPruningApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = TenantScopeResolver.Resolve(request.TenantScope);
        var candidates = ResolveCandidates(request);
        if (candidates.Count == 0)
        {
            return new ResourcePolicyPruningApplicationResult
            {
                TenantScope = tenant,
                AppliedAt = request.AppliedAt,
                Candidates = [],
            };
        }

        var shapeFailures = candidates
            .Select((candidate, index) => ValidateShape(index, candidate))
            .ToArray();
        var resourceIds = candidates
            .Where((candidate, index) => shapeFailures[index] is null && !string.IsNullOrWhiteSpace(candidate.ResourceId))
            .Select(static candidate => candidate!.ResourceId!)
            .ToHashSet(StringComparer.Ordinal);
        var allVersions = await ReadVersionsByResourceIdAsync(
            tenant,
            resourceIds,
            ResourceVersionScope.AllVersions,
            cancellationToken);
        var draftVersions = await ReadVersionsByResourceIdAsync(
            tenant,
            resourceIds,
            ResourceVersionScope.Draft,
            cancellationToken);
        var markers = resourceIds.Count == 0
            ? new Dictionary<string, ResourceLifecycleMarker>(StringComparer.Ordinal)
            : new Dictionary<string, ResourceLifecycleMarker>(
                await markerStore.GetMarkersAsync(resourceIds, tenant, cancellationToken),
                StringComparer.Ordinal);
        var definitions = new Dictionary<string, ResourceDefinition?>(StringComparer.Ordinal);
        var results = new ResourcePolicyPruningApplicationCandidateResult?[candidates.Count];
        var processed = new Dictionary<PruningCandidateKey, ResourcePolicyPruningApplicationCandidateResult>();

        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];

            if (shapeFailures[index] is not null)
            {
                results[index] = shapeFailures[index];
                continue;
            }

            var key = new PruningCandidateKey(candidate!.ResourceId!, candidate.ResourceVersion!.Value);
            if (processed.TryGetValue(key, out var previous))
            {
                results[index] = DuplicateResult(index, candidate, previous);
                continue;
            }

            var result = await ApplyCandidateAsync(
                index,
                candidate,
                tenant,
                request.AppliedAt,
                allVersions,
                draftVersions,
                markers,
                definitions,
                cancellationToken);
            results[index] = result;
            processed[key] = result;
        }

        return new ResourcePolicyPruningApplicationResult
        {
            TenantScope = tenant,
            AppliedAt = request.AppliedAt,
            Candidates = results.Select(static result => result!).ToList(),
        };
    }

    private async ValueTask<ResourcePolicyPruningApplicationCandidateResult> ApplyCandidateAsync(
        int index,
        ResourcePolicyPruningApplicationCandidate candidate,
        TenantScope tenant,
        DateTimeOffset? appliedAt,
        IDictionary<string, List<Resource>> allVersions,
        IReadOnlyDictionary<string, List<Resource>> draftVersions,
        IReadOnlyDictionary<string, ResourceLifecycleMarker> markers,
        IDictionary<string, ResourceDefinition?> definitions,
        CancellationToken cancellationToken)
    {
        if (!allVersions.TryGetValue(candidate.ResourceId!, out var versions) || versions.Count == 0)
            return TargetNotFound(index, candidate, tenant);

        var ordered = versions.OrderByDescending(static version => version.Version).ToList();
        var latest = ordered[0];
        var target = ordered.FirstOrDefault(version => version.Version == candidate.ResourceVersion!.Value);
        if (target is null)
        {
            var policyFailure = await ValidatePolicyIdentityAsync(index, candidate, latest, tenant, definitions, cancellationToken);
            return policyFailure ?? CandidateResult(index, candidate, ResourcePolicyPruningApplicationCandidateStatus.AlreadyPruned);
        }

        if (target.Version == latest.Version)
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyPruningVersionProtectedLatest,
                $"Resource '{candidate.ResourceId}' version {target.Version} is the latest version and cannot be pruned.",
                "resourceVersion");
        }

        if (!IsDraftVersion(candidate.ResourceId!, target.Version, draftVersions))
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyPruningVersionProtectedActive,
                $"Resource '{candidate.ResourceId}' version {target.Version} is active and cannot be pruned.",
                "resourceVersion");
        }

        var preflightFailure = await ValidatePolicyAndSafetyAsync(
            index,
            candidate,
            target,
            versions,
            tenant,
            appliedAt,
            markers,
            definitions,
            cancellationToken);
        if (preflightFailure is not null)
            return preflightFailure;

        try
        {
            var removed = await pruningStore.PruneVersionAsync(candidate.ResourceId!, target.Version, tenant, cancellationToken);
            if (!removed)
                return CandidateResult(index, candidate, ResourcePolicyPruningApplicationCandidateStatus.AlreadyPruned);

            versions.RemoveAll(version => version.Version == target.Version);
            return CandidateResult(index, candidate, ResourcePolicyPruningApplicationCandidateStatus.Pruned);
        }
        catch (NotSupportedException)
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyPruningProviderUnsupported,
                "The active provider does not support destructive version pruning.",
                "provider");
        }
        catch (InvalidOperationException ex)
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyPruningWriteFailed,
                ex.Message,
                "provider");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyPruningWriteFailed,
                ex.Message,
                "provider");
        }
    }

    private async ValueTask<ResourcePolicyPruningApplicationCandidateResult?> ValidatePolicyAndSafetyAsync(
        int index,
        ResourcePolicyPruningApplicationCandidate candidate,
        Resource target,
        IReadOnlyCollection<Resource> versions,
        TenantScope tenant,
        DateTimeOffset? appliedAt,
        IReadOnlyDictionary<string, ResourceLifecycleMarker> markers,
        IDictionary<string, ResourceDefinition?> definitions,
        CancellationToken cancellationToken)
    {
        var (definition, policy) = await ReadPolicyAsync(target.DefinitionId, candidate.PolicyId!, tenant, definitions, cancellationToken);
        var identityFailure = ValidatePolicyIdentity(index, candidate, definition, policy);
        if (identityFailure is not null)
            return identityFailure;

        if (versions.Count - 1 < policy!.Criteria.MaximumRetainedVersions.GetValueOrDefault())
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyPruningUnsafe,
                $"Pruning resource '{candidate.ResourceId}' version {candidate.ResourceVersion} would violate the retained-version safety floor.",
                "resourceVersion");
        }

        if (!MatchesPolicyCriteria(policy, target, appliedAt, markers, out var path))
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyPruningPolicyMismatch,
                $"Resource '{candidate.ResourceId}' version {candidate.ResourceVersion} no longer matches policy '{candidate.PolicyId}'.",
                path);
        }

        return null;
    }

    private async ValueTask<ResourcePolicyPruningApplicationCandidateResult?> ValidatePolicyIdentityAsync(
        int index,
        ResourcePolicyPruningApplicationCandidate candidate,
        Resource latest,
        TenantScope tenant,
        IDictionary<string, ResourceDefinition?> definitions,
        CancellationToken cancellationToken)
    {
        var (definition, policy) = await ReadPolicyAsync(latest.DefinitionId, candidate.PolicyId!, tenant, definitions, cancellationToken);
        return ValidatePolicyIdentity(index, candidate, definition, policy);
    }

    private async ValueTask<(ResourceDefinition? Definition, ResourcePolicyDeclaration? Policy)> ReadPolicyAsync(
        string definitionId,
        string policyId,
        TenantScope tenant,
        IDictionary<string, ResourceDefinition?> definitions,
        CancellationToken cancellationToken)
    {
        if (!definitions.TryGetValue(definitionId, out var definition))
        {
            definition = await definitionStore.GetDefinitionAsync(definitionId, tenant, cancellationToken);
            definitions[definitionId] = definition;
        }

        var policy = definition?.PolicyDeclarations.FirstOrDefault(policy =>
            string.Equals(policy.PolicyId, policyId, StringComparison.Ordinal));
        return (definition, policy);
    }

    private ResourcePolicyPruningApplicationCandidateResult? ValidatePolicyIdentity(
        int index,
        ResourcePolicyPruningApplicationCandidate candidate,
        ResourceDefinition? definition,
        ResourcePolicyDeclaration? policy)
    {
        if (policy is null)
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyPruningPolicyMissing,
                $"Policy '{candidate.PolicyId}' is not declared on the current definition for resource '{candidate.ResourceId}'.",
                "policyId");
        }

        if (!policyValidator.Validate(definition!).IsValid
            || policy.Kind != candidate.PolicyKind
            || policy.Target != ResourcePolicyTarget.ResourceVersion
            || policy.Outcome != candidate.Outcome
            || policy.Criteria.MaximumRetainedVersions is null)
        {
            var path = policy.Kind != candidate.PolicyKind
                ? "policyKind"
                : policy.Outcome != candidate.Outcome
                    ? "outcome"
                    : "criteria";
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyPruningPolicyMismatch,
                $"Policy '{candidate.PolicyId}' no longer matches the submitted pruning candidate.",
                path);
        }

        return null;
    }

    private static bool MatchesPolicyCriteria(
        ResourcePolicyDeclaration policy,
        Resource target,
        DateTimeOffset? appliedAt,
        IReadOnlyDictionary<string, ResourceLifecycleMarker> markers,
        out string path)
    {
        if (!string.IsNullOrWhiteSpace(policy.Criteria.UnsupportedFacetPredicate))
        {
            path = "criteria";
            return false;
        }

        if (policy.Criteria.MinimumAge is { } minimumAge)
        {
            if (appliedAt is null)
            {
                path = "appliedAt";
                return false;
            }

            var created = new DateTimeOffset(DateTime.SpecifyKind(target.Created, DateTimeKind.Utc));
            if (appliedAt.Value - created < minimumAge)
            {
                path = "criteria/minimumAge";
                return false;
            }
        }

        if (policy.Criteria.ActivationState == ResourcePolicyActivationState.Active)
        {
            path = "criteria/activationState";
            return false;
        }

        if (policy.Criteria.LifecycleState is { } expectedLifecycleState)
        {
            var actual = markers.TryGetValue(target.ResourceId, out var marker)
                ? marker.State
                : ResourceLifecycleMarkerState.None;
            if (actual != expectedLifecycleState)
            {
                path = "criteria/lifecycleState";
                return false;
            }
        }

        path = "";
        return true;
    }

    private async ValueTask<Dictionary<string, List<Resource>>> ReadVersionsByResourceIdAsync(
        TenantScope tenant,
        IReadOnlyCollection<string> resourceIds,
        ResourceVersionScope scope,
        CancellationToken cancellationToken)
    {
        if (resourceIds.Count == 0)
            return new Dictionary<string, List<Resource>>(StringComparer.Ordinal);

        var resources = await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = tenant,
            Scope = scope,
            ResourceIds = resourceIds,
        }, cancellationToken);
        return resources.GroupBy(static resource => resource.ResourceId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToList(), StringComparer.Ordinal);
    }

    private static bool IsDraftVersion(
        string resourceId,
        int resourceVersion,
        IReadOnlyDictionary<string, List<Resource>> draftVersions) =>
        draftVersions.TryGetValue(resourceId, out var versions)
        && versions.Any(version => version.Version == resourceVersion);

    private static IReadOnlyList<ResourcePolicyPruningApplicationCandidate> ResolveCandidates(
        ResourcePolicyPruningApplicationRequest request) =>
        request.Candidates ?? [];

    private static ResourcePolicyPruningApplicationCandidateResult? ValidateShape(
        int index,
        ResourcePolicyPruningApplicationCandidate? candidate)
    {
        if (candidate is null)
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyPruningCandidateInvalid,
                "Policy pruning candidates must not be null.",
                "candidate");
        }

        if (string.IsNullOrWhiteSpace(candidate.PolicyId))
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyPruningCandidateInvalid,
                "Policy pruning candidates require a policy identifier.",
                "policyId");
        }

        if (candidate.PolicyKind != ResourcePolicyKind.VersionPruning)
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyPruningCandidateInvalid,
                "Policy pruning candidates require version-pruning policy kind.",
                "policyKind");
        }

        if (candidate.Outcome != ResourcePolicyOutcome.PrunePreview)
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyPruningCandidateInvalid,
                "Policy pruning candidates require prune-preview outcome.",
                "outcome");
        }

        if (string.IsNullOrWhiteSpace(candidate.ResourceId))
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyPruningCandidateInvalid,
                "Policy pruning candidates require a resource identifier.",
                "resourceId");
        }

        if (candidate.ResourceVersion is null or <= 0)
        {
            return Failure(
                index,
                candidate,
                ResourcePolicyDiagnosticCodes.PolicyPruningCandidateInvalid,
                "Policy pruning candidates require a positive resource version.",
                "resourceVersion");
        }

        return null;
    }

    private static ResourcePolicyPruningApplicationCandidateResult TargetNotFound(
        int index,
        ResourcePolicyPruningApplicationCandidate candidate,
        TenantScope tenant) =>
        Failure(
            index,
            candidate,
            ResourcePolicyDiagnosticCodes.PolicyPruningTargetNotFound,
            $"Resource '{candidate.ResourceId}' was not found in tenant '{tenant.TenantId}'.",
            "resourceId");

    private static ResourcePolicyPruningApplicationCandidateResult DuplicateResult(
        int index,
        ResourcePolicyPruningApplicationCandidate candidate,
        ResourcePolicyPruningApplicationCandidateResult previous) =>
        previous.Status == ResourcePolicyPruningApplicationCandidateStatus.AlreadyPruned
            ? CandidateResult(index, candidate, ResourcePolicyPruningApplicationCandidateStatus.AlreadyPruned)
            : CandidateResult(index, candidate, ResourcePolicyPruningApplicationCandidateStatus.Skipped);

    private static ResourcePolicyPruningApplicationCandidateResult Failure(
        int index,
        ResourcePolicyPruningApplicationCandidate? candidate,
        string code,
        string message,
        string? path = null) =>
        new()
        {
            Index = index,
            Status = ResourcePolicyPruningApplicationCandidateStatus.Failed,
            PolicyId = candidate?.PolicyId,
            ResourceId = candidate?.ResourceId,
            ResourceVersion = candidate?.ResourceVersion,
            Diagnostics =
            [
                ResourcePolicyValidator.Diagnostic(
                    code,
                    message,
                    path,
                    candidate?.PolicyId,
                    candidate?.ResourceId,
                    candidate?.ResourceVersion),
            ],
        };

    private static ResourcePolicyPruningApplicationCandidateResult CandidateResult(
        int index,
        ResourcePolicyPruningApplicationCandidate candidate,
        ResourcePolicyPruningApplicationCandidateStatus status) =>
        new()
        {
            Index = index,
            Status = status,
            PolicyId = candidate.PolicyId,
            ResourceId = candidate.ResourceId,
            ResourceVersion = candidate.ResourceVersion,
        };

    private readonly record struct PruningCandidateKey(string ResourceId, int ResourceVersion);

}
