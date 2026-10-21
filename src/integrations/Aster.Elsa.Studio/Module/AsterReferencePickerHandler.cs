using Aster.Elsa.Studio.Components;
using Aster.Elsa.Studio.Forms;
using Elsa.Studio.Contracts;
using Elsa.Studio.Models;
using Microsoft.AspNetCore.Components;

namespace Aster.Elsa.Studio.Module;

/// <summary>
/// Elsa Studio UI-hint handler for <see cref="AsterEditorMapping.ReferencePickerHint"/>: renders
/// <see cref="ReferencePicker"/>, which searches the facet's target definitions through <c>POST /resources/query</c>.
/// Registered with Elsa Studio's <c>AddUIHintHandler</c>, so <c>IUIHintService</c> resolves it like Elsa's own editors.
/// </summary>
internal sealed class AsterReferencePickerHandler : IUIHintHandler
{
    /// <inheritdoc />
    public string UISyntax => "Literal";

    /// <inheritdoc />
    public bool GetSupportsUIHint(string uiHint) => uiHint == AsterEditorMapping.ReferencePickerHint;

    /// <inheritdoc />
    public RenderFragment DisplayInputEditor(DisplayInputEditorContext context) => builder =>
    {
        builder.OpenComponent<ReferencePicker>(0);
        builder.AddComponentParameter(1, nameof(ReferencePicker.EditorContext), context);
        builder.CloseComponent();
    };
}
