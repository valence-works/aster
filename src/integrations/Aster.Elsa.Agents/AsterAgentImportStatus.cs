namespace Aster.Elsa.Agents;

/// <summary>What the import did with one source agent.</summary>
public enum AsterAgentImportStatus
{
    /// <summary>The agent was saved to Aster and activated in the store's channel.</summary>
    Imported,

    /// <summary>An agent with the same ID already exists in Aster, in any lifecycle state, so it was left as it is.</summary>
    Skipped,

    /// <summary>The agent could not be imported; see <see cref="AsterAgentImportItem.Error"/>.</summary>
    Failed,
}
