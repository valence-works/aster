using System.Globalization;
using global::Elsa.Api.Client.Resources.Scripting.Models;
using global::Elsa.Api.Client.Shared.Models;
using global::Elsa.Studio.Contracts;
using global::Elsa.Studio.Models;
using global::Elsa.Studio.UIHints;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Aster.Elsa.Studio.Tests;

/// <summary>
/// Stands in for Elsa Studio's UI-hint handlers, whose editors need the Studio's JavaScript (Monaco, MudBlazor
/// extensions). It supports every hint, so a test sees which hint the form asked for, and writes values the way the
/// real editor for that hint does.
/// </summary>
internal sealed class TestUIHintHandler : IUIHintHandler
{
    public string UISyntax => "Literal";

    public bool GetSupportsUIHint(string uiHint) => true;

    public RenderFragment DisplayInputEditor(DisplayInputEditorContext context) => builder =>
    {
        builder.OpenComponent<TestEditor>(0);
        builder.AddComponentParameter(1, nameof(TestEditor.EditorContext), context);
        builder.CloseComponent();
    };
}

/// <summary>An <c>input</c> carrying the field name and hint; a change event writes the editor's value.</summary>
internal sealed class TestEditor : ComponentBase
{
    [Parameter] public DisplayInputEditorContext EditorContext { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "input");
        builder.AddAttribute(1, "data-test-field", EditorContext.InputDescriptor.Name);
        builder.AddAttribute(2, "data-test-hint", EditorContext.InputDescriptor.UIHint);
        builder.AddAttribute(3, "data-test-label", EditorContext.InputDescriptor.DisplayName);
        builder.AddAttribute(4, "value", Display(EditorContext.Value));
        builder.AddAttribute(5, "disabled", EditorContext.IsReadOnly);
        builder.AddAttribute(6, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, OnChangeAsync));
        builder.CloseElement();
    }

    private Task OnChangeAsync(ChangeEventArgs args)
    {
        var text = args.Value?.ToString() ?? "";
        object? value = EditorContext.InputDescriptor.UIHint switch
        {
            InputUIHints.Checkbox => bool.Parse(text),
            InputUIHints.MultiText or InputUIHints.CheckList => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            InputUIHints.JsonEditor or InputUIHints.CodeEditor => new WrappedInput { Expression = new Expression("Literal", text) },
            InputUIHints.DateTimePicker => DateTime.Parse(text, CultureInfo.InvariantCulture).ToString(CultureInfo.CurrentCulture),
            _ => text,
        };
        return EditorContext.OnValueChanged(value);
    }

    private static string Display(object? value) => value switch
    {
        null => "",
        IEnumerable<string> items => string.Join(",", items),
        _ => value.ToString() ?? "",
    };
}
