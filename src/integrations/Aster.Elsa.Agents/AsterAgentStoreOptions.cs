namespace Aster.Elsa.Agents;

/// <summary>
/// Options of the Aster agent store, configured through
/// <see cref="AsterAgentsServiceCollectionExtensions.AddAsterElsaAgents"/> or
/// <see cref="AsterAgentPersistenceFeatureExtensions.UseAsterAgentStore"/>.
/// </summary>
public sealed class AsterAgentStoreOptions
{
    /// <summary>The channel the store reads from by default: <c>production</c>.</summary>
    public const string DefaultChannel = "production";

    /// <summary>
    /// Gets or sets the Aster activation channel the store reads. Elsa sees, for each agent, the version active in this
    /// channel; an agent with no version active there is invisible to Elsa. Defaults to <see cref="DefaultChannel"/>.
    /// </summary>
    public string Channel { get; set; } = DefaultChannel;

    /// <summary>
    /// Gets or sets whether every add or update through the store also activates the saved version in
    /// <see cref="Channel"/>, replacing the version active there. Defaults to <see langword="true"/>, which matches
    /// Elsa's own stores: a save is visible at once. With <see langword="false"/>, a save is a draft until the host
    /// activates it through Aster (promotion), so Elsa keeps running the previously active version.
    /// </summary>
    public bool AutoActivate { get; set; } = true;
}
