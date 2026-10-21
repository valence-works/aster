namespace Aster.Elsa.Agents;

/// <summary>The outcome of <see cref="IAsterAgentImporter.ImportAsync"/>.</summary>
public sealed record AsterAgentImportResult
{
    /// <summary>Gets one item per source agent, in ID order.</summary>
    public required IReadOnlyList<AsterAgentImportItem> Items { get; init; }

    /// <summary>Gets the number of agents imported by this run.</summary>
    public int ImportedCount => Items.Count(static item => item.Status == AsterAgentImportStatus.Imported);

    /// <summary>Gets the number of agents skipped because they already exist in Aster.</summary>
    public int SkippedCount => Items.Count(static item => item.Status == AsterAgentImportStatus.Skipped);

    /// <summary>Gets the number of agents that failed to import.</summary>
    public int FailedCount => Items.Count(static item => item.Status == AsterAgentImportStatus.Failed);

    /// <summary>Gets whether no agent failed.</summary>
    public bool Succeeded => FailedCount == 0;
}
