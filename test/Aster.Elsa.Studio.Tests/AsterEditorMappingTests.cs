using System.Text.Json.Nodes;
using Aster.Elsa.Studio.Forms;
using global::Elsa.Api.Client.Resources.ActivityDescriptors.Models;
using global::Elsa.Studio.UIHints;
using global::Elsa.Studio.UIHints.Extensions;

namespace Aster.Elsa.Studio.Tests;

/// <summary>
/// #117: <c>x-aster-uiHint</c> values (#107) map to Elsa Studio's UI-hint input editors, with a fallback per facet
/// type, and the editor options are in the shape Elsa Studio's own editors read.
/// </summary>
public sealed class AsterEditorMappingTests
{
    private static AsterFormField Field(
        AsterValueKind kind,
        string? hint = null,
        bool isArray = false,
        IReadOnlyList<string>? options = null,
        IReadOnlyList<string>? targets = null) =>
        new("A", "F", "F", null, kind, isArray, IsRequired: false, AsterType: null, hint,
            (options ?? []).Select(static option => (JsonNode)JsonValue.Create(option)!).ToList(), targets ?? [], null);

    public static TheoryData<string, bool, string, bool, string> StandardHints => new()
    {
        { "String", false, "text", false, InputUIHints.SingleLine },
        { "String", false, "slug", false, InputUIHints.SingleLine },
        { "String", false, "email", false, InputUIHints.SingleLine },
        { "String", false, "url", false, InputUIHints.SingleLine },
        { "String", false, "secret", false, InputUIHints.SingleLine },
        { "String", false, "multiline", false, InputUIHints.MultiLine },
        { "String", false, "markdown", false, InputUIHints.MultiLine },
        { "String", false, "html", false, InputUIHints.MultiLine },
        { "String", false, "code", false, InputUIHints.CodeEditor },
        { "String", false, "json", false, InputUIHints.JsonEditor },
        { "Object", false, "json", false, InputUIHints.JsonEditor },
        { "Integer", false, "number", false, InputUIHints.SingleLine },
        { "Number", false, "number", false, InputUIHints.SingleLine },
        { "String", false, "select", true, InputUIHints.DropDown },
        { "String", true, "select", true, InputUIHints.CheckList },
        { "Integer", false, "radio", true, InputUIHints.RadioList },
        { "Boolean", false, "checkbox", false, InputUIHints.Checkbox },
        { "String", true, "checkbox", true, InputUIHints.CheckList },
        { "Boolean", false, "switch", false, InputUIHints.Checkbox },
        { "Boolean", false, "toggle", false, InputUIHints.Checkbox },
        { "DateTime", false, "date", false, InputUIHints.DateTimePicker },
        { "DateTime", false, "datetime", false, InputUIHints.DateTimePicker },
        { "String", true, "tags", false, InputUIHints.MultiText },
        { "Reference", false, "reference-picker", false, AsterEditorMapping.ReferencePickerHint },
        { "Reference", true, "reference-picker", false, AsterEditorMapping.ReferencePickerHint },
    };

    [Theory]
    [MemberData(nameof(StandardHints))]
    public void StandardHint_MapsToElsaStudioEditor(string kind, bool isArray, string hint, bool hasOptions, string expected)
    {
        var editor = AsterEditorMapping.Resolve(Field(Enum.Parse<AsterValueKind>(kind), hint, isArray, hasOptions ? ["a", "b"] : null));

        Assert.Equal(expected, editor.UiHint);
        Assert.False(editor.IsFallback);
    }

    public static TheoryData<string, bool, string?, string> Fallbacks => new()
    {
        // No hint: by type.
        { "String", false, null, InputUIHints.SingleLine },
        { "Guid", false, null, InputUIHints.SingleLine },
        { "Integer", false, null, InputUIHints.SingleLine },
        { "Number", false, null, InputUIHints.SingleLine },
        { "Boolean", false, null, InputUIHints.Checkbox },
        { "DateTime", false, null, InputUIHints.DateTimePicker },
        { "Object", false, null, InputUIHints.JsonEditor },
        { "Any", false, null, InputUIHints.JsonEditor },
        { "Reference", false, null, AsterEditorMapping.ReferencePickerHint },
        { "Reference", true, null, AsterEditorMapping.ReferencePickerHint },
        { "String", true, null, InputUIHints.MultiText },
        { "Integer", true, null, InputUIHints.MultiText },
        { "Object", true, null, InputUIHints.JsonEditor },

        // A hint the module doesn't know, or a vendor hint.
        { "String", false, "acme:color-picker", InputUIHints.SingleLine },
        { "Boolean", false, "acme:toggle", InputUIHints.Checkbox },

        // A hint that doesn't apply to the facet type.
        { "Boolean", false, "text", InputUIHints.Checkbox },
        { "String", false, "number", InputUIHints.SingleLine },
        { "String", false, "date", InputUIHints.SingleLine },
        { "Integer", false, "tags", InputUIHints.SingleLine },
        { "String", false, "reference-picker", InputUIHints.SingleLine },

        // select and radio without AllowedValues have nothing to offer.
        { "String", false, "select", InputUIHints.SingleLine },
        { "String", false, "radio", InputUIHints.SingleLine },
    };

    [Theory]
    [MemberData(nameof(Fallbacks))]
    public void MissingOrInapplicableHint_FallsBackByType(string kind, bool isArray, string? hint, string expected)
    {
        var editor = AsterEditorMapping.Resolve(Field(Enum.Parse<AsterValueKind>(kind), hint, isArray));

        Assert.Equal(expected, editor.UiHint);
        Assert.True(editor.IsFallback);
    }

    [Fact]
    public void AllowedValuesWithoutHint_RenderAsSelect()
    {
        Assert.Equal(InputUIHints.DropDown, AsterEditorMapping.Resolve(Field(AsterValueKind.String, options: ["a"])).UiHint);
        Assert.Equal(InputUIHints.CheckList, AsterEditorMapping.Resolve(Field(AsterValueKind.String, isArray: true, options: ["a"])).UiHint);
    }

    [Fact]
    public void Options_AreReadableByElsaStudioEditors()
    {
        // Elsa Studio's editors read their options with these extension methods (Elsa.Studio.UIHints 3.9).
        var dropDown = Descriptor(AsterEditorMapping.Resolve(Field(AsterValueKind.String, "select", options: ["tools", "toys"])));
        Assert.Equal(["tools", "toys"], dropDown.GetSelectList().Items.Select(static item => item.Value));

        var checkList = Descriptor(AsterEditorMapping.Resolve(Field(AsterValueKind.String, "checkbox", isArray: true, options: ["a", "b"])));
        Assert.Equal(["a", "b"], checkList.GetCheckList().Items.Select(static item => item.Value));

        var radioList = Descriptor(AsterEditorMapping.Resolve(Field(AsterValueKind.Integer, "radio", options: ["1", "2"])));
        Assert.Equal(["1", "2"], radioList.GetRadioList().Items.Select(static item => item.Value));

        var json = Descriptor(AsterEditorMapping.Resolve(Field(AsterValueKind.Object, "json")));
        Assert.Equal("json", json.GetCodeEditorOptions().Language);

        static InputDescriptor Descriptor(AsterFieldEditor editor) => new() { Name = "F", UIHint = editor.UiHint, UISpecifications = editor.UISpecifications };
    }

    [Fact]
    public void ReferencePicker_CarriesTargetsAndMultiplicity()
    {
        var editor = AsterEditorMapping.Resolve(Field(AsterValueKind.Reference, isArray: true, targets: ["Supplier", "Vendor"]));

        var options = (System.Text.Json.JsonElement)editor.UISpecifications![AsterEditorMapping.ReferenceSpecificationKey];
        Assert.True(options.GetProperty("multiple").GetBoolean());
        Assert.Equal(["Supplier", "Vendor"], options.GetProperty("targetDefinitionIds").EnumerateArray().Select(static id => id.GetString()));
    }
}
