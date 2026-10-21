using System.Globalization;
using System.Text.Json.Nodes;
using Aster.Elsa.Studio.Forms;
using global::Elsa.Api.Client.Resources.Scripting.Models;
using global::Elsa.Api.Client.Shared.Models;

namespace Aster.Elsa.Studio.Tests;

/// <summary>Values travel between Aster payloads and Elsa Studio's editors in the facet's type.</summary>
public sealed class AsterFieldValuesTests
{
    private static AsterFormField Field(AsterValueKind kind, string? hint = null, bool isArray = false) =>
        new("A", "F", "F", null, kind, isArray, false, null, hint, [], [], null);

    private static JsonNode? Convert(AsterFormField field, object? input)
    {
        Assert.True(AsterFieldValues.TryFromEditorValue(field, AsterEditorMapping.Resolve(field), input, out var value, out var error), error);
        return value;
    }

    private static string Error(AsterFormField field, object? input)
    {
        Assert.False(AsterFieldValues.TryFromEditorValue(field, AsterEditorMapping.Resolve(field), input, out _, out var error));
        return error!;
    }

    [Fact]
    public void Text_EmptyIsAbsent()
    {
        Assert.Equal("x", Convert(Field(AsterValueKind.String), "x")!.GetValue<string>());
        Assert.Null(Convert(Field(AsterValueKind.String), ""));
        Assert.Null(Convert(Field(AsterValueKind.Integer), "  "));
    }

    [Fact]
    public void Numbers_ParseOrReportInputErrors()
    {
        Assert.Equal(42, Convert(Field(AsterValueKind.Integer, "number"), "42")!.GetValue<long>());
        Assert.Equal(1.25m, Convert(Field(AsterValueKind.Number, "number"), "1.25")!.GetValue<decimal>());
        Assert.Equal("Enter a whole number.", Error(Field(AsterValueKind.Integer), "4.5"));
        Assert.Equal("Enter a number.", Error(Field(AsterValueKind.Number), "lots"));
        Assert.Equal("Item 2: Enter a whole number.", Error(Field(AsterValueKind.Integer, isArray: true), new List<string> { "1", "two" }));
    }

    [Fact]
    public void Numbers_ReadADecimalCommaInTheUsersCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal(1.5m, Convert(Field(AsterValueKind.Number, "number"), "1,5")!.GetValue<decimal>());
            Assert.Equal(1.5m, Convert(Field(AsterValueKind.Number, "number"), "1.5")!.GetValue<decimal>());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Checkbox_WritesBooleans()
    {
        var field = Field(AsterValueKind.Boolean, "switch");
        Assert.True(Convert(field, true)!.GetValue<bool>());
        Assert.False(Convert(field, false)!.GetValue<bool>());
        Assert.Equal(true, AsterFieldValues.ToEditorValue(field, AsterEditorMapping.Resolve(field), JsonValue.Create(true)));
    }

    [Fact]
    public void DateTime_ReadsPickerTextInTheUsersCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // en-GB writes day/month: this is 4 March, which the invariant culture would read as 3 April.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
            var field = Field(AsterValueKind.DateTime, "datetime");
            Assert.Equal("2026-03-04T05:06:07Z", Convert(field, "04/03/2026 05:06:07")!.GetValue<string>());
            Assert.Equal("2026-03-04T05:06:07Z", Convert(field, "2026-03-04T05:06:07")!.GetValue<string>());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void DateTime_RoundTripsThroughThePickerAsUtc()
    {
        var field = Field(AsterValueKind.DateTime, "datetime");
        var editor = AsterEditorMapping.Resolve(field);

        var shown = AsterFieldValues.ToEditorValue(field, editor, JsonValue.Create("2026-03-04T05:06:07Z"));
        Assert.Equal("2026-03-04T05:06:07", shown);

        // The picker writes DateTime.ToString() in the current culture.
        var picked = DateTime.Parse((string)shown!, CultureInfo.InvariantCulture).ToString(CultureInfo.CurrentCulture);
        Assert.Equal("2026-03-04T05:06:07Z", Convert(field, picked)!.GetValue<string>());

        // A date hint stores the date at midnight UTC.
        Assert.Equal("2026-03-04T00:00:00Z", Convert(Field(AsterValueKind.DateTime, "date"), "2026-03-04T05:06:07")!.GetValue<string>());
        Assert.Equal("Enter a date and time.", Error(field, "soon"));
    }

    [Fact]
    public void Lists_BecomeArraysOfTheElementType()
    {
        var tags = Convert(Field(AsterValueKind.String, "tags", isArray: true), new List<string> { "a", "b" })!.AsArray();
        Assert.Equal(["a", "b"], tags.Select(static tag => tag!.GetValue<string>()));

        var numbers = Convert(Field(AsterValueKind.Integer, isArray: true), new List<string> { "1", "2" })!.AsArray();
        Assert.Equal([1L, 2L], numbers.Select(static number => number!.GetValue<long>()));

        Assert.Null(Convert(Field(AsterValueKind.String, "tags", isArray: true), new List<string>()));

        // A check list's JSON text is read too.
        Assert.Equal(2, Convert(Field(AsterValueKind.String, isArray: true), "[\"x\",\"y\"]")!.AsArray().Count);
    }

    [Fact]
    public void JsonEditor_ReadsWrappedLiteralExpressions()
    {
        var field = Field(AsterValueKind.Object, "json");
        var written = new WrappedInput { Expression = new Expression("Literal", "{\"a\":1}") };

        Assert.Equal(1, Convert(field, written)!["a"]!.GetValue<int>());
        Assert.StartsWith("Enter valid JSON", Error(field, "{ not json"));
        Assert.Contains("\"a\": 1", (string)AsterFieldValues.ToEditorValue(field, AsterEditorMapping.Resolve(field), JsonNode.Parse("{\"a\":1}"))!);
    }

    [Fact]
    public void Guid_IsValidated()
    {
        Assert.Equal("3f2504e0-4f89-11d3-9a0c-0305e82c3301", Convert(Field(AsterValueKind.Guid), "3F2504E0-4F89-11D3-9A0C-0305E82C3301")!.GetValue<string>());
        Assert.StartsWith("Enter a GUID", Error(Field(AsterValueKind.Guid), "nope"));
    }

    [Fact]
    public void References_AreIdsOrListsOfIds()
    {
        var single = Field(AsterValueKind.Reference);
        Assert.Equal("acme", Convert(single, " acme ")!.GetValue<string>());
        Assert.Null(Convert(single, ""));
        Assert.Equal("acme", AsterFieldValues.ToEditorValue(single, AsterEditorMapping.Resolve(single), JsonValue.Create("acme")));

        var many = Field(AsterValueKind.Reference, isArray: true);
        Assert.Equal(["a", "b"], Convert(many, new List<string> { "a", "b" })!.AsArray().Select(static id => id!.GetValue<string>()));
        Assert.Equal(new List<string> { "a" }, AsterFieldValues.ToEditorValue(many, AsterEditorMapping.Resolve(many), new JsonArray("a")));
    }

    [Fact]
    public void InputError_KeepsThePreviousValueUntilFixed()
    {
        var model = new AsterFormModel("D", 1, [new AsterFormSection("A", "A", false, [Field(AsterValueKind.Integer)])]);
        var field = model.FindField("A", "F")!;
        var editor = AsterEditorMapping.Resolve(field);
        var state = AsterFormState.Create(model, JsonNode.Parse("""{ "A": { "F": 7 } }""")!.AsObject());

        Assert.False(state.SetEditorValue(field, editor, "seven"));
        Assert.Equal("Enter a whole number.", state.InputErrors["A.F"]);
        Assert.Equal(7, state.GetValue(field)!.GetValue<int>());

        Assert.True(state.SetEditorValue(field, editor, "8"));
        Assert.Empty(state.InputErrors);
        Assert.Equal(8, state.BuildAspects()["A"]!["F"]!.GetValue<long>());
    }
}
