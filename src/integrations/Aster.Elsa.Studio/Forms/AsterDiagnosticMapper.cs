using Aster.Elsa.Studio.Client;

namespace Aster.Elsa.Studio.Forms;

/// <summary>A place on a form that can show errors, identified by the request-body path its value is sent at.</summary>
/// <param name="Key">The form's key for the place, such as a field key (<c>Content.Title</c>) or <c>resourceId</c>.</param>
/// <param name="RequestPath">The request-body path, such as <c>aspects.Content.Title</c> or <c>filter</c>.</param>
internal sealed record AsterErrorTarget(string Key, string RequestPath);

/// <summary>Server diagnostics placed on a form: per target key, plus those that concern no target.</summary>
/// <param name="ByTarget">The messages for each target key.</param>
/// <param name="Unplaced">The messages that concern the request as a whole, or a place the form doesn't show.</param>
internal sealed record AsterFormErrors(IReadOnlyDictionary<string, IReadOnlyList<string>> ByTarget, IReadOnlyList<string> Unplaced)
{
    /// <summary>No errors.</summary>
    public static AsterFormErrors None { get; } = new(new Dictionary<string, IReadOnlyList<string>>(), []);

    /// <summary>Gets the messages for a target key.</summary>
    public IReadOnlyList<string> For(string key) => ByTarget.TryGetValue(key, out var messages) ? messages : [];

    /// <summary>Gets whether there are any errors.</summary>
    public bool Any => ByTarget.Count > 0 || Unplaced.Count > 0;
}

/// <summary>
/// Maps server diagnostics onto form fields by request-body path. A diagnostic's path is its <c>path</c> when the
/// server sets one (query diagnostics, #194, such as <c>filter.and[1].op</c>), or else
/// <c>aspects.{aspectKey}.{facetId}</c> built from its aspect key and facet ID (save diagnostics). It lands on the
/// target with the longest request path that is the path itself or a prefix of it ending at a member or index
/// boundary, so <c>aspects.Content.Tags</c> catches element diagnostics and <c>aspects.Content</c> catches
/// aspect-level ones. Diagnostics without a matching target stay unplaced, for a form-level alert.
/// </summary>
internal static class AsterDiagnosticMapper
{
    /// <summary>The targets of a resource form: every field, every aspect section, and the resource ID.</summary>
    public static IReadOnlyList<AsterErrorTarget> ResourceTargets(AsterFormModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var targets = new List<AsterErrorTarget> { new("resourceId", "resourceId") };
        foreach (var section in model.Sections)
        {
            targets.Add(new AsterErrorTarget(SectionKey(section.AspectKey), section.RequestPath));
            targets.AddRange(section.Fields.Select(static field => new AsterErrorTarget(field.Key, field.RequestPath)));
        }

        return targets;
    }

    /// <summary>The target key of an aspect section.</summary>
    public static string SectionKey(string aspectKey) => $"aspect:{aspectKey}";

    /// <summary>The request-body path a diagnostic concerns, or <see langword="null"/> when it names none.</summary>
    public static string? PathOf(AsterDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        if (!string.IsNullOrEmpty(diagnostic.Path))
            return diagnostic.Path;
        if (string.IsNullOrEmpty(diagnostic.AspectKey))
            return null;

        return string.IsNullOrEmpty(diagnostic.FacetId)
            ? $"aspects.{diagnostic.AspectKey}"
            : $"aspects.{diagnostic.AspectKey}.{diagnostic.FacetId}";
    }

    /// <summary>Places <paramref name="diagnostics"/> on <paramref name="targets"/>.</summary>
    public static AsterFormErrors Map(IEnumerable<AsterDiagnostic> diagnostics, IEnumerable<AsterErrorTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(targets);

        var candidates = targets.OrderByDescending(static target => target.RequestPath.Length).ToList();
        var byTarget = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var unplaced = new List<string>();

        foreach (var diagnostic in diagnostics)
        {
            var message = diagnostic.ElementIndex is { } index ? $"Item {index + 1}: {diagnostic.Message}" : diagnostic.Message;
            var path = PathOf(diagnostic);
            var target = path is null ? null : candidates.FirstOrDefault(candidate => Covers(candidate.RequestPath, path));
            if (target is null)
            {
                unplaced.Add($"{message} ({diagnostic.Code})");
                continue;
            }

            if (!byTarget.TryGetValue(target.Key, out var messages))
                byTarget[target.Key] = messages = [];
            messages.Add(message);
        }

        return new AsterFormErrors(
            byTarget.ToDictionary(static pair => pair.Key, static pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal),
            unplaced);
    }

    private static bool Covers(string targetPath, string path) =>
        path.StartsWith(targetPath, StringComparison.Ordinal)
        && (path.Length == targetPath.Length || path[targetPath.Length] is '.' or '[');
}
