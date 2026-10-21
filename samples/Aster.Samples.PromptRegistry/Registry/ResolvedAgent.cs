using System.Text.RegularExpressions;
using Aster.Samples.PromptRegistry.Model;

namespace Aster.Samples.PromptRegistry.Registry;

/// <summary>An agent as it runs in one channel, with the versions it resolved to.</summary>
public sealed partial record ResolvedAgent(
    string AgentId,
    int AgentVersion,
    Agent Agent,
    string PromptId,
    int PromptVersion,
    Prompt Prompt,
    IReadOnlyList<ResolvedTool> Tools)
{
    /// <summary>
    /// Fills the prompt template. Every declared variable needs a value; placeholders that are not
    /// declared variables are left as they are.
    /// </summary>
    /// <exception cref="ArgumentException">A declared variable has no value.</exception>
    public string RenderPrompt(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var missing = Prompt.Variables.Where(variable => !values.ContainsKey(variable)).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"Prompt '{PromptId}' v{PromptVersion} needs values for: {string.Join(", ", missing)}.", nameof(values));

        return Placeholder().Replace(Prompt.Template, match =>
            Prompt.Variables.Contains(match.Groups[1].Value) ? values[match.Groups[1].Value] : match.Value);
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}")]
    private static partial Regex Placeholder();
}

/// <summary>A tool at the version active in the agent's channel.</summary>
public sealed record ResolvedTool(string ToolId, int Version, Tool Tool);
