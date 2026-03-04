using System.Collections.Concurrent;
using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Microsoft.Extensions.Logging;

namespace Aster.Core.InMemory;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IResourceDefinitionStore"/>.
/// Stores ordered version snapshots per <c>DefinitionId</c>.
/// </summary>
public sealed partial class InMemoryResourceDefinitionStore : IResourceDefinitionStore
{
    private readonly ConcurrentDictionary<string, List<ResourceDefinition>> definitions = new(StringComparer.Ordinal);
    private readonly ILogger<InMemoryResourceDefinitionStore> logger;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryResourceDefinitionStore"/>.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public InMemoryResourceDefinitionStore(ILogger<InMemoryResourceDefinitionStore> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger;
    }

    /// <inheritdoc />
    public ValueTask<ResourceDefinition?> GetDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);

        if (definitions.TryGetValue(definitionId, out var versions))
        {
            lock (versions)
            {
                return ValueTask.FromResult<ResourceDefinition?>(versions.Count > 0 ? versions[^1] : null);
            }
        }

        return ValueTask.FromResult<ResourceDefinition?>(null);
    }

    /// <inheritdoc />
    public ValueTask<ResourceDefinition?> GetDefinitionVersionAsync(string definitionId, int version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);

        if (definitions.TryGetValue(definitionId, out var versions))
        {
            lock (versions)
            {
                var match = versions.FirstOrDefault(d => d.Version == version);
                return ValueTask.FromResult<ResourceDefinition?>(match);
            }
        }

        return ValueTask.FromResult<ResourceDefinition?>(null);
    }

    /// <inheritdoc />
    public ValueTask RegisterDefinitionAsync(ResourceDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var versions = definitions.GetOrAdd(definition.DefinitionId, _ => []);

        lock (versions)
        {
            int nextVersion = versions.Count + 1;
            var versionedDefinition = definition with { Version = nextVersion };
            versions.Add(versionedDefinition);
            LogDefinitionRegistered(definition.DefinitionId, nextVersion);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        var latest = new List<ResourceDefinition>();

        foreach (var versions in definitions.Values)
        {
            lock (versions)
            {
                if (versions.Count > 0)
                    latest.Add(versions[^1]);
            }
        }

        return ValueTask.FromResult<IEnumerable<ResourceDefinition>>(latest);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Structured log methods (source generated)
    // ──────────────────────────────────────────────────────────────────────────

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information,
        Message = "Registered definition '{DefinitionId}' version {Version}.")]
    private partial void LogDefinitionRegistered(string definitionId, int version);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
        Message = "Definition '{DefinitionId}' was not found.")]
    private partial void LogDefinitionNotFound(string definitionId);
}
