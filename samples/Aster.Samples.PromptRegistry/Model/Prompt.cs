namespace Aster.Samples.PromptRegistry.Model;

/// <summary>
/// The single aspect of a <c>Prompt</c> resource. Facets are derived from these properties:
/// <c>Name</c> and <c>Template</c> (string, required), <c>Variables</c> (string[]) and
/// <c>Model</c> (object, because <see cref="ModelSettings"/> is a nested record).
/// </summary>
public sealed record Prompt
{
    /// <summary>A display name.</summary>
    public required string Name { get; init; }

    /// <summary>The template text, with <c>{{variable}}</c> placeholders.</summary>
    public required string Template { get; init; }

    /// <summary>The variables the template expects.</summary>
    public string[] Variables { get; init; } = [];

    /// <summary>The model the prompt is tuned for.</summary>
    public required ModelSettings Model { get; init; }
}

/// <summary>Model settings, stored as one <c>object</c> facet.</summary>
public sealed record ModelSettings
{
    /// <summary>The model provider, such as <c>openai</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>The model name.</summary>
    public required string Model { get; init; }

    /// <summary>The sampling temperature.</summary>
    public decimal Temperature { get; init; }

    /// <summary>The response token limit.</summary>
    public int MaxTokens { get; init; }
}
