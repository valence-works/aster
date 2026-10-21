namespace Aster.Samples.Configuration.Model;

/// <summary>
/// The deployment environments. Each one is an Aster activation channel: the version active in
/// <c>prod</c> is the configuration production runs with.
/// </summary>
public static class Environments
{
    /// <summary>The development channel, where new versions are first activated.</summary>
    public const string Dev = "dev";

    /// <summary>The staging channel.</summary>
    public const string Staging = "staging";

    /// <summary>The production channel.</summary>
    public const string Prod = "prod";

    /// <summary>The promotion order.</summary>
    public static IReadOnlyList<string> PromotionOrder { get; } = [Dev, Staging, Prod];

    /// <summary>Returns the environment a version in <paramref name="channel"/> is promoted to next.</summary>
    /// <exception cref="ArgumentException"><paramref name="channel"/> is not a known environment.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="channel"/> is the last environment.</exception>
    public static string Next(string channel)
    {
        var index = PromotionOrder.ToList().IndexOf(channel);
        if (index < 0)
            throw new ArgumentException($"'{channel}' is not an environment.", nameof(channel));
        if (index == PromotionOrder.Count - 1)
            throw new InvalidOperationException($"'{channel}' is the last environment; there is nothing to promote to.");

        return PromotionOrder[index + 1];
    }
}
