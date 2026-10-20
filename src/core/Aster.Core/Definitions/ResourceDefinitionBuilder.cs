using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Definitions;

/// <summary>
/// Fluent builder for constructing a <see cref="ResourceDefinition"/>.
/// </summary>
/// <remarks>
/// Aspect key scheme:
/// <list type="bullet">
///   <item>Unnamed: <c>AspectDefinitionId</c> (derived from <c>typeof(T).Name</c>)</item>
///   <item>Named: <c>"{AspectDefinitionId}:{Name}"</c> composite</item>
/// </list>
/// Duplicate keys are detected at <see cref="Build"/> time and throw <see cref="DuplicateAspectAttachmentException"/>.
/// </remarks>
public sealed class ResourceDefinitionBuilder
{
    private string? definitionId;
    private bool isSingleton;
    private readonly List<(string Key, AspectDefinition Definition)> aspectEntries = [];
    private readonly Dictionary<string, SharedAttachment> sharedAttachments = new(StringComparer.Ordinal);
    private readonly List<ResourcePolicyDeclaration> policyDeclarations = [];

    /// <summary>
    /// Sets the logical persistent definition identifier.
    /// </summary>
    /// <param name="id">The definition ID (e.g., "Product").</param>
    /// <returns>This builder for chaining.</returns>
    public ResourceDefinitionBuilder WithDefinitionId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        definitionId = id;
        return this;
    }

    /// <summary>
    /// Specifies whether only one instance can exist for this definition.
    /// </summary>
    /// <param name="singleton"><see langword="true"/> to enforce singleton behaviour.</param>
    /// <returns>This builder for chaining.</returns>
    public ResourceDefinitionBuilder WithSingleton(bool singleton = true)
    {
        isSingleton = singleton;
        return this;
    }

    /// <summary>
    /// Attaches an unnamed aspect derived from the type <typeparamref name="T"/>.
    /// The <c>AspectDefinitionId</c> is set to <c>typeof(T).Name</c>, and one facet is declared per
    /// public property whose CLR type maps to a supported facet type (ADR 0002 §8).
    /// </summary>
    /// <typeparam name="T">The POCO type representing the aspect.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public ResourceDefinitionBuilder WithAspect<T>() =>
        AddAspect(typeof(T).Name, name: null, TypedAspectFacets.Derive(typeof(T)));

    /// <summary>
    /// Attaches a named aspect derived from the type <typeparamref name="T"/>.
    /// The attachment key is <c>"{typeof(T).Name}:{name}"</c>; facets are derived as for <see cref="WithAspect{T}()"/>.
    /// </summary>
    /// <typeparam name="T">The POCO type representing the aspect.</typeparam>
    /// <param name="name">The discriminator name for this attachment.</param>
    /// <returns>This builder for chaining.</returns>
    public ResourceDefinitionBuilder WithNamedAspect<T>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return AddAspect(typeof(T).Name, name, TypedAspectFacets.Derive(typeof(T)));
    }

    /// <summary>
    /// Attaches an unnamed aspect without a CLR type. Declare its facets with
    /// <see cref="WithFacet(string, string, bool)"/>.
    /// </summary>
    /// <param name="aspectDefinitionId">The aspect definition ID (e.g., "Seo").</param>
    /// <returns>This builder for chaining.</returns>
    public ResourceDefinitionBuilder WithAspect(string aspectDefinitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aspectDefinitionId);
        return AddAspect(aspectDefinitionId, name: null, []);
    }

    /// <summary>
    /// Attaches a named aspect without a CLR type. The attachment key is <c>"{aspectDefinitionId}:{name}"</c>.
    /// </summary>
    /// <param name="aspectDefinitionId">The aspect definition ID (e.g., "Price").</param>
    /// <param name="name">The discriminator name for this attachment.</param>
    /// <returns>This builder for chaining.</returns>
    public ResourceDefinitionBuilder WithNamedAspect(string aspectDefinitionId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aspectDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return AddAspect(aspectDefinitionId, name, []);
    }

    /// <summary>
    /// Attaches a shared aspect definition from <see cref="IAspectDefinitionStore"/> (ADR 0003). The pinned
    /// version is resolved and embedded by <see cref="BuildAsync"/>; facets cannot be added or decorated here.
    /// </summary>
    /// <param name="aspectDefinitionId">The shared aspect definition ID (e.g., "Seo").</param>
    /// <param name="version">The version to pin; <see langword="null"/> pins the latest version at build time.</param>
    /// <param name="name">The discriminator name for a named attachment (key <c>"{aspectDefinitionId}:{name}"</c>).</param>
    /// <returns>This builder for chaining.</returns>
    public ResourceDefinitionBuilder WithSharedAspect(string aspectDefinitionId, int? version = null, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aspectDefinitionId);
        if (name is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (version is <= 0)
            throw new ArgumentOutOfRangeException(nameof(version), version, "Pinned versions start at 1.");

        var key = name is null ? aspectDefinitionId : $"{aspectDefinitionId}:{name}";

        // Placeholder: keeps attachment order and duplicate detection; replaced by the stored version in BuildAsync.
        aspectEntries.Add((key, new AspectDefinition
        {
            AspectDefinitionId = aspectDefinitionId,
            Id = string.Empty,
            Version = version ?? 0,
            IsShared = true,
        }));
        sharedAttachments[key] = new SharedAttachment(aspectDefinitionId, version, name);
        return this;
    }

    /// <summary>
    /// Attaches the shared aspect definition whose ID is <c>typeof(T).Name</c>, as registered from
    /// <see cref="AspectDefinitionBuilder.FromType{T}"/>. See <see cref="WithSharedAspect(string, int?, string?)"/>.
    /// </summary>
    /// <typeparam name="T">The POCO type the shared aspect was registered from.</typeparam>
    /// <param name="version">The version to pin; <see langword="null"/> pins the latest version at build time.</param>
    /// <param name="name">The discriminator name for a named attachment.</param>
    /// <returns>This builder for chaining.</returns>
    public ResourceDefinitionBuilder WithSharedAspect<T>(int? version = null, string? name = null) =>
        WithSharedAspect(typeof(T).Name, version, name);

    /// <summary>
    /// Attaches an unnamed aspect derived from the type <typeparamref name="T"/>.
    /// Equivalent to <see cref="WithAspect{T}()"/>.
    /// </summary>
    /// <typeparam name="T">The POCO type representing the aspect.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public ResourceDefinitionBuilder WithTypedAspect<T>() => WithAspect<T>();

    private ResourceDefinitionBuilder AddAspect(string aspectDefinitionId, string? name, IReadOnlyList<FacetDefinition> facets)
    {
        var key = name is null ? aspectDefinitionId : $"{aspectDefinitionId}:{name}";
        aspectEntries.Add((key, new AspectDefinition
        {
            AspectDefinitionId = aspectDefinitionId,
            Id = Guid.NewGuid().ToString(),
            Version = 1,
            RequiresName = name is not null,
            FacetDefinitions = facets,
        }));
        return this;
    }

    /// <summary>
    /// Attaches an explicit policy declaration to the definition.
    /// </summary>
    /// <param name="policy">The policy declaration to attach.</param>
    /// <returns>This builder for chaining.</returns>
    public ResourceDefinitionBuilder WithPolicy(ResourcePolicyDeclaration policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policyDeclarations.Add(policy);
        return this;
    }

    /// <summary>
    /// Declares a facet on the most recently registered aspect.
    /// </summary>
    /// <param name="facetDefinitionId">The facet ID (e.g., "Title").</param>
    /// <param name="type">The facet type descriptor (e.g., "string", "int", "datetime"); see ADR 0002.</param>
    /// <param name="isRequired">Whether a present aspect must carry a non-null value for this facet.</param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no aspect has been registered yet, or the aspect already declares a facet with this ID.
    /// </exception>
    public ResourceDefinitionBuilder WithFacet(string facetDefinitionId, string type, bool isRequired = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facetDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var (key, lastAspect) = LastAspect(nameof(WithFacet));

        if (lastAspect.FacetDefinitions.Any(facet => facet.FacetDefinitionId == facetDefinitionId))
            throw new InvalidOperationException($"Aspect '{key}' already declares facet '{facetDefinitionId}'. Use WithFacetMetadata(\"{facetDefinitionId}\", ...) to decorate it.");

        var facet = new FacetDefinition
        {
            FacetDefinitionId = facetDefinitionId,
            Id = Guid.NewGuid().ToString(),
            Version = 1,
            Type = type,
            IsRequired = isRequired,
        };
        aspectEntries[^1] = (key, lastAspect with { FacetDefinitions = [.. lastAspect.FacetDefinitions, facet] });
        return this;
    }

    /// <summary>
    /// Adds a facet to the most recently registered aspect, using <c>typeof(T).Name</c> as both the
    /// <c>FacetDefinitionId</c> and (lowercased) the type. Replaces a facet with the same ID.
    /// </summary>
    /// <typeparam name="T">The CLR type representing the facet value (e.g., <c>decimal</c>, <c>string</c>).</typeparam>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no aspect has been registered yet.</exception>
    [Obsolete("Names the facet after its CLR type. Use WithFacet(facetDefinitionId, type) or derive facets with WithAspect<T>().")]
    public ResourceDefinitionBuilder WithTypedFacet<T>()
    {
        var (key, lastAspect) = LastAspect(nameof(WithTypedFacet));
        var facetDefinitionId = typeof(T).Name;
        var facet = new FacetDefinition
        {
            FacetDefinitionId = facetDefinitionId,
            Id = Guid.NewGuid().ToString(),
            Version = 1,
            Type = facetDefinitionId.ToLowerInvariant(),
            IsRequired = false,
        };

        // Appended last (replacing a derived facet with the same ID) so WithFacetMetadata(metadata) targets it.
        var updatedFacets = lastAspect.FacetDefinitions.Where(existing => existing.FacetDefinitionId != facetDefinitionId).Append(facet).ToList();
        aspectEntries[^1] = (key, lastAspect with { FacetDefinitions = updatedFacets });
        return this;
    }

    /// <summary>
    /// Attaches optional display and validation metadata to the facet with the given ID on the most
    /// recently registered aspect, including facets derived from a typed aspect.
    /// </summary>
    /// <param name="facetDefinitionId">The facet ID.</param>
    /// <param name="metadata">The metadata to attach.</param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no aspect is registered or the facet is not declared on it.</exception>
    public ResourceDefinitionBuilder WithFacetMetadata(string facetDefinitionId, FacetMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facetDefinitionId);
        ArgumentNullException.ThrowIfNull(metadata);
        var (key, lastAspect) = LastAspect(nameof(WithFacetMetadata));

        var index = lastAspect.FacetDefinitions.ToList().FindIndex(facet => facet.FacetDefinitionId == facetDefinitionId);
        if (index < 0)
            throw new InvalidOperationException($"Aspect '{key}' does not declare facet '{facetDefinitionId}'.");

        var facets = new List<FacetDefinition>(lastAspect.FacetDefinitions);
        facets[index] = facets[index] with { Metadata = metadata };
        aspectEntries[^1] = (key, lastAspect with { FacetDefinitions = facets });
        return this;
    }

    private (string Key, AspectDefinition Aspect) LastAspect(string caller)
    {
        if (aspectEntries.Count == 0)
            throw new InvalidOperationException($"Register an aspect (WithAspect) before calling {caller}.");

        var last = aspectEntries[^1];
        if (sharedAttachments.ContainsKey(last.Key))
        {
            throw new InvalidOperationException(
                $"Aspect '{last.Key}' is a shared aspect; its facets come from the aspect definition store. Register a new aspect version to change them.");
        }

        return last;
    }

    /// <summary>
    /// Attaches optional display and validation metadata to the most recently added facet
    /// of the most recently registered aspect.
    /// </summary>
    /// <param name="metadata">The metadata to attach.</param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no facet has been added yet.</exception>
    public ResourceDefinitionBuilder WithFacetMetadata(FacetMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (aspectEntries.Count > 0)
            LastAspect(nameof(WithFacetMetadata));

        if (aspectEntries.Count == 0 || aspectEntries[^1].Definition.FacetDefinitions.Count == 0)
            throw new InvalidOperationException("Add a facet (WithFacet or a typed aspect) before calling WithFacetMetadata.");

        var (key, lastAspect) = aspectEntries[^1];
        var facets = new List<FacetDefinition>(lastAspect.FacetDefinitions);
        facets[^1] = facets[^1] with { Metadata = metadata };
        aspectEntries[^1] = (key, lastAspect with { FacetDefinitions = facets });
        return this;
    }

    /// <summary>
    /// Builds the <see cref="ResourceDefinition"/> from the configured state.
    /// </summary>
    /// <returns>The constructed definition. <c>Version</c> is set to 0 and auto-incremented by the store on registration.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <c>DefinitionId</c> has not been set, or a shared aspect is attached (use <see cref="BuildAsync"/>).
    /// </exception>
    /// <exception cref="DuplicateAspectAttachmentException">
    /// Thrown if the same aspect key is attached more than once.
    /// </exception>
    public ResourceDefinition Build()
    {
        if (sharedAttachments.Count > 0)
            throw new InvalidOperationException("Shared aspects are attached; resolve them with BuildAsync(aspectDefinitionStore).");

        return BuildCore(aspectEntries);
    }

    /// <summary>
    /// Builds the <see cref="ResourceDefinition"/>, resolving every shared aspect attachment from
    /// <paramref name="aspectDefinitionStore"/> in the tenant and embedding the stored version unchanged,
    /// marked <see cref="AspectDefinition.IsShared"/> (ADR 0003 §3).
    /// </summary>
    /// <param name="aspectDefinitionStore">The store to resolve shared aspects from.</param>
    /// <param name="tenantScope">The tenant to resolve in; the default tenant when <see langword="null"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The constructed definition, as from <see cref="Build"/>.</returns>
    /// <exception cref="SharedAspectResolutionException">
    /// An attached aspect or pinned version does not exist (<see cref="SharedAspectDiagnosticCodes.AspectDefinitionNotFound"/>),
    /// or a stored aspect requires a name and the attachment is unnamed (<see cref="SharedAspectDiagnosticCodes.AspectNameRequired"/>).
    /// </exception>
    public async ValueTask<ResourceDefinition> BuildAsync(
        IAspectDefinitionStore aspectDefinitionStore,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aspectDefinitionStore);

        var resolved = new List<(string Key, AspectDefinition Definition)>(aspectEntries.Count);
        var diagnostics = new List<SharedAspectDiagnostic>();
        foreach (var (key, definition) in aspectEntries)
        {
            if (!sharedAttachments.TryGetValue(key, out var attachment))
            {
                resolved.Add((key, definition));
                continue;
            }

            var stored = attachment.Version is { } version
                ? await aspectDefinitionStore.GetVersionAsync(attachment.AspectDefinitionId, version, tenantScope, cancellationToken)
                : await aspectDefinitionStore.GetLatestAsync(attachment.AspectDefinitionId, tenantScope, cancellationToken);

            if (stored is null)
            {
                diagnostics.Add(new SharedAspectDiagnostic(
                    SharedAspectDiagnosticCodes.AspectDefinitionNotFound,
                    attachment.Version is { } missing
                        ? $"Aspect definition '{attachment.AspectDefinitionId}' version {missing} does not exist."
                        : $"Aspect definition '{attachment.AspectDefinitionId}' does not exist.",
                    key,
                    attachment.AspectDefinitionId,
                    attachment.Version));
                continue;
            }

            if (stored.RequiresName && attachment.Name is null)
            {
                diagnostics.Add(new SharedAspectDiagnostic(
                    SharedAspectDiagnosticCodes.AspectNameRequired,
                    $"Aspect definition '{attachment.AspectDefinitionId}' requires a named attachment.",
                    key,
                    attachment.AspectDefinitionId,
                    attachment.Version));
                continue;
            }

            resolved.Add((key, stored with { IsShared = true }));
        }

        if (diagnostics.Count > 0)
            throw new SharedAspectResolutionException(diagnostics);

        return BuildCore(resolved);
    }

    private ResourceDefinition BuildCore(IReadOnlyList<(string Key, AspectDefinition Definition)> entries)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            throw new InvalidOperationException("DefinitionId must be set before calling Build().");

        // Validate uniqueness — detect first duplicate key
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, _) in entries)
        {
            if (!seen.Add(key))
                throw new DuplicateAspectAttachmentException(key);
        }

        var aspectDict = entries.ToDictionary(e => e.Key, e => e.Definition);

        return new ResourceDefinition
        {
            DefinitionId = definitionId,
            Id = Guid.NewGuid().ToString(),
            Version = 0, // auto-incremented by the store
            AspectDefinitions = aspectDict,
            PolicyDeclarations = [.. policyDeclarations],
            IsSingleton = isSingleton,
        };
    }

    private sealed record SharedAttachment(string AspectDefinitionId, int? Version, string? Name);
}
