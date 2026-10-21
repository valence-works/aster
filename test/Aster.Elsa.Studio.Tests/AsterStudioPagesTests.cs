using System.Text.Json.Nodes;
using Aster.Elsa.Studio.Client;
using Aster.Elsa.Studio.Components;
using Aster.Elsa.Studio.Module;
using Aster.Elsa.Studio.Pages;
using Bunit;
using Bunit.Rendering;
using global::Elsa.Studio.Contracts;
using global::Elsa.Studio.Services;
using global::Elsa.Studio.UIHints;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Aster.Elsa.Studio.Tests;

/// <summary>
/// #117 acceptance, rendered with bUnit: a resource type defined at runtime is editable in the module's pages with no
/// code changes, and server diagnostics show on the fields they concern.
/// </summary>
public sealed class AsterStudioPagesTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private AsterTestServer server = default!;
    private BunitContext context = default!;

    public async Task InitializeAsync()
    {
        server = await AsterTestServer.StartAsync();
        context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddMudServices();
        context.Services.AddSingleton(server.CreateClient());

        // Elsa Studio's own UI-hint service, over the module's reference picker and a stand-in for Elsa's editors.
        context.Services.AddScoped<IUIHintHandler, AsterReferencePickerHandler>();
        context.Services.AddScoped<IUIHintHandler, TestUIHintHandler>();
        context.Services.AddScoped<IUIHintService, DefaultUIHintService>();
    }

    public async Task DisposeAsync()
    {
        await context.DisposeAsync();
        await server.DisposeAsync();
    }

    private static string Field(string facetId) => $"[data-aster-field='Details.{facetId}']";

    private static string Input(string facetId) => $"input[data-test-field='Details.{facetId}']";

    // Finds an element and raises an event on it on the renderer's dispatcher. MudBlazor inputs finish some updates
    // asynchronously, and the re-render they cause can replace a handler the DOM snapshot still names; bUnit then
    // refuses the event without running anything, so it is raised again on the fresh render.
    private static async Task RaiseAsync<TComponent>(IRenderedComponent<TComponent> cut, Action raise)
        where TComponent : IComponent
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await cut.InvokeAsync(raise);
                return;
            }
            catch (UnknownEventHandlerIdException) when (attempt < 10)
            {
                await Task.Delay(20);
            }
        }
    }

    [Fact]
    public async Task DefinitionList_ShowsDefinitionsRegisteredAtRuntime()
    {
        var empty = context.Render<DefinitionList>();
        empty.WaitForElement(".aster-empty", Timeout);

        await server.RegisterAsync(AsterTestServer.Product());

        var list = context.Render<DefinitionList>();
        var row = list.WaitForElement("tr[data-aster-definition='Product']", Timeout);
        Assert.Contains("Details", row.TextContent);
        Assert.Equal("aster/definitions/Product/new", row.QuerySelector("a.aster-new")!.GetAttribute("href"));
    }

    [Fact]
    public async Task RuntimeDefinition_IsEditableWithElsaStudioEditors()
    {
        await server.RegisterAsync(AsterTestServer.Supplier());
        await server.RegisterAsync(AsterTestServer.Product());
        await server.CreateAsync("Supplier", "acme", new JsonObject { ["Info"] = new JsonObject { ["Name"] = "Acme" } });
        context.Render<MudPopoverProvider>();

        var editor = context.Render<ResourceEditor>(parameters => parameters.Add(page => page.DefinitionId, "Product"));
        editor.WaitForElement(Input("Title"), Timeout);

        // Each facet renders with the Elsa Studio editor its UI hint maps to, or its type's fallback.
        Assert.Equal(InputUIHints.SingleLine, editor.Find(Input("Title")).GetAttribute("data-test-hint"));
        Assert.Equal("Title *", editor.Find(Input("Title")).GetAttribute("data-test-label"));
        Assert.Equal(InputUIHints.SingleLine, editor.Find(Input("Price")).GetAttribute("data-test-hint"));
        Assert.Equal(InputUIHints.Checkbox, editor.Find(Input("Published")).GetAttribute("data-test-hint"));
        Assert.Equal(InputUIHints.MultiText, editor.Find(Input("Tags")).GetAttribute("data-test-hint"));
        Assert.Equal(InputUIHints.DropDown, editor.Find(Input("Category")).GetAttribute("data-test-hint"));
        Assert.Equal(InputUIHints.DateTimePicker, editor.Find(Input("ReleaseDate")).GetAttribute("data-test-hint"));
        Assert.Equal(InputUIHints.JsonEditor, editor.Find(Input("Spec")).GetAttribute("data-test-hint"));
        Assert.Equal(InputUIHints.SingleLine, editor.Find(Input("Notes")).GetAttribute("data-test-hint"));

        // The reference facet renders the module's own picker, through the same IUIHintService.
        Assert.NotNull(editor.Find($"{Field("Supplier")} [data-aster-reference-picker='Details.Supplier']"));

        // Typing into MudTextField finishes asynchronously; set the resource ID through the field's binding instead.
        var resourceId = editor.FindComponents<MudTextField<string>>().Single(static field => field.Instance.Class!.Contains("aster-resource-id"));
        await editor.InvokeAsync(() => resourceId.Instance.ValueChanged.InvokeAsync("hammer"));
        await RaiseAsync(editor, () => editor.Find(Input("Title")).Change("Hammer"));
        await RaiseAsync(editor, () => editor.Find(Input("Price")).Change("12.5"));
        await RaiseAsync(editor, () => editor.Find(Input("Published")).Change("true"));
        await RaiseAsync(editor, () => editor.Find(Input("Tags")).Change("steel, heavy"));
        await RaiseAsync(editor, () => editor.Find(Input("Category")).Change("tools"));
        await RaiseAsync(editor, () => editor.Find(Input("ReleaseDate")).Change("2026-01-02T00:00:00"));
        await RaiseAsync(editor, () => editor.Find(Input("Spec")).Change("{ \"weight\": 2 }"));
        await editor.InvokeAsync(() => editor.FindComponent<MudAutocomplete<string>>().Instance.ValueChanged.InvokeAsync("acme"));
        await RaiseAsync(editor, () => editor.Find("button.aster-save").Click());

        var navigation = context.Services.GetRequiredService<NavigationManager>();
        editor.WaitForAssertion(() => Assert.EndsWith("aster/resources/hammer", navigation.Uri), Timeout);

        var saved = await server.CreateClient().GetResourceAsync("hammer");
        var details = saved!.Aspects["Details"]!;
        Assert.Equal("Hammer", details["Title"]!.GetValue<string>());
        Assert.Equal(12.5m, details["Price"]!.GetValue<decimal>());
        Assert.True(details["Published"]!.GetValue<bool>());
        Assert.Equal(2, details["Tags"]!.AsArray().Count);
        Assert.Equal("2026-01-02T00:00:00Z", details["ReleaseDate"]!.GetValue<string>());
        Assert.Equal(2, details["Spec"]!["weight"]!.GetValue<int>());
        Assert.Equal("acme", details["Supplier"]!.GetValue<string>());
    }

    [Fact]
    public async Task ServerDiagnostics_ShowOnTheirFields()
    {
        await server.RegisterAsync(AsterTestServer.Product());
        context.Render<MudPopoverProvider>();

        var editor = context.Render<ResourceEditor>(parameters => parameters.Add(page => page.DefinitionId, "Product"));
        editor.WaitForElement(Input("Title"), Timeout);

        // A value that doesn't fit the facet type is caught before the request.
        await RaiseAsync(editor, () => editor.Find(Input("Price")).Change("cheap"));
        await RaiseAsync(editor, () => editor.Find("button.aster-save").Click());
        editor.WaitForAssertion(() => Assert.Contains("Fix the highlighted fields", editor.Find(".aster-error").TextContent), Timeout);
        Assert.Equal("Enter a number.", editor.Find($"{Field("Price")} .aster-field-error").TextContent);

        // Values only the server rejects: a missing required title, a category outside AllowedValues, a tag too long.
        await RaiseAsync(editor, () => editor.Find(Input("Price")).Change("5"));
        await RaiseAsync(editor, () => editor.Find(Input("Category")).Change("food"));
        await RaiseAsync(editor, () => editor.Find(Input("Tags")).Change("ok, far-too-long-for-a-tag"));
        await RaiseAsync(editor, () => editor.Find("button.aster-save").Click());

        editor.WaitForAssertion(() => Assert.NotEmpty(editor.FindAll($"{Field("Title")} .aster-field-error")), Timeout);
        Assert.Single(editor.FindAll($"{Field("Category")} .aster-field-error"));
        Assert.StartsWith("Item 2:", editor.Find($"{Field("Tags")} .aster-field-error").TextContent);
        Assert.Empty(editor.FindAll($"{Field("Price")} .aster-field-error"));
        Assert.Empty(editor.FindAll(".aster-error"));
        Assert.Null(await server.CreateClient().GetResourceAsync("food"));
    }

    [Fact]
    public async Task ExistingResource_EditsVersionsAndActivates()
    {
        await server.RegisterAsync(AsterTestServer.Product());
        await server.CreateAsync("Product", "saw", new JsonObject { ["Details"] = new JsonObject { ["Title"] = "Saw", ["Stock"] = 4 } });
        context.Render<MudPopoverProvider>();

        var editor = context.Render<ResourceEditor>(parameters => parameters.Add(page => page.ResourceId, "saw"));
        editor.WaitForElement(Input("Title"), Timeout);
        Assert.Equal("Saw", editor.Find(Input("Title")).GetAttribute("value"));
        Assert.Equal("4", editor.Find(Input("Stock")).GetAttribute("value"));

        // Save version 2 on top of version 1.
        await RaiseAsync(editor, () => editor.Find(Input("Title")).Change("Hand saw"));
        await RaiseAsync(editor, () => editor.Find("button.aster-save").Click());
        editor.WaitForAssertion(() => Assert.Contains("Saved version 2", editor.Find(".aster-message").TextContent), Timeout);
        editor.WaitForElement("tr[data-aster-version='2']", Timeout);

        // Activate version 1 in Published: a rollback.
        await RaiseAsync(editor, () => editor.Find("tr[data-aster-version='1'] button.aster-activate").Click());
        editor.WaitForAssertion(() => Assert.NotNull(editor.Find("tr[data-aster-version='1'] [data-aster-channel='Published']")), Timeout);
        var versions = await server.CreateClient().GetVersionsAsync("saw");
        Assert.Equal(["Published"], versions.Single(static version => version.Version == 1).ActiveChannels);

        // View version 1, read-only.
        await RaiseAsync(editor, () => editor.Find("tr[data-aster-version='1'] button.aster-view").Click());
        editor.WaitForAssertion(() => Assert.Equal("Saw", editor.Find(Input("Title")).GetAttribute("value")), Timeout);
        Assert.True(editor.Find(Input("Title")).HasAttribute("disabled"));
        Assert.Empty(editor.FindAll("button.aster-save"));

        await RaiseAsync(editor, () => editor.Find("button.aster-show-latest").Click());
        editor.WaitForAssertion(() => Assert.Equal("Hand saw", editor.Find(Input("Title")).GetAttribute("value")), Timeout);
    }

    [Fact]
    public async Task SlowVersionLoad_AfterNavigating_DoesNotReplaceTheNewResource()
    {
        await server.RegisterAsync(AsterTestServer.Product());
        await server.CreateAsync("Product", "saw", new JsonObject { ["Details"] = new JsonObject { ["Title"] = "Saw" } });
        await server.CreateAsync("Product", "hammer", new JsonObject { ["Details"] = new JsonObject { ["Title"] = "Hammer" } });
        context.Render<MudPopoverProvider>();

        var editor = context.Render<ResourceEditor>(parameters => parameters.Add(page => page.ResourceId, "saw"));
        editor.WaitForElement("tr[data-aster-version='1'] button.aster-view", Timeout);

        // Viewing saw's version 1 is slow; the user moves on to hammer before it answers.
        var release = new TaskCompletionSource();
        server.Recorder.Gate = ("/api/aster/resources/saw?version=1", release.Task);
        await RaiseAsync(editor, () => editor.Find("tr[data-aster-version='1'] button.aster-view").Click());
        editor.Render(parameters => parameters.Add(page => page.ResourceId, "hammer"));
        editor.WaitForAssertion(() => Assert.Equal("Hammer", editor.Find(Input("Title")).GetAttribute("value")), Timeout);

        release.SetResult();
        await Task.Delay(200);
        editor.WaitForAssertion(() => Assert.Equal("Hammer", editor.Find(Input("Title")).GetAttribute("value")), Timeout);
        Assert.NotEmpty(editor.FindAll("button.aster-save"));
    }

    [Fact]
    public async Task StaleEdit_ShowsTheConcurrencyConflict()
    {
        await server.RegisterAsync(AsterTestServer.Supplier());
        await server.CreateAsync("Supplier", "acme", new JsonObject { ["Info"] = new JsonObject { ["Name"] = "Acme" } });

        var editor = context.Render<ResourceEditor>(parameters => parameters.Add(page => page.ResourceId, "acme"));
        editor.WaitForElement("input[data-test-field='Info.Name']", Timeout);

        // Someone else saves version 2 meanwhile.
        await server.CreateClient().UpdateResourceAsync("acme", 1, new JsonObject { ["Info"] = new JsonObject { ["Name"] = "Acme Corp" } });

        await RaiseAsync(editor, () => editor.Find("input[data-test-field='Info.Name']").Change("Acme Inc"));
        await RaiseAsync(editor, () => editor.Find("button.aster-save").Click());
        editor.WaitForElement(".aster-conflict", Timeout);
    }

    [Fact]
    public async Task ReferencePicker_SearchesTheTargetDefinition()
    {
        await server.RegisterAsync(AsterTestServer.Supplier());
        await server.RegisterAsync(AsterTestServer.Product());
        await server.CreateAsync("Supplier", "acme", new JsonObject { ["Info"] = new JsonObject { ["Name"] = "Acme" } });
        await server.CreateAsync("Supplier", "globex", new JsonObject { ["Info"] = new JsonObject { ["Name"] = "Globex" } });
        context.Render<MudPopoverProvider>();

        var editor = context.Render<ResourceEditor>(parameters => parameters.Add(page => page.DefinitionId, "Product"));
        editor.WaitForState(() => editor.FindComponents<ReferencePicker>().Count == 1, Timeout);
        server.Recorder.Clear();

        var autocomplete = editor.FindComponent<MudAutocomplete<string>>().Instance;
        var search = autocomplete.SearchFunc ?? throw new InvalidOperationException("The picker sets no search function.");
        var found = await editor.InvokeAsync(async () => await search("glo", CancellationToken.None)!);

        Assert.Equal(["globex"], found ?? []);
        var query = Assert.Single(server.Recorder.Requests, static request => request.PathAndQuery == "/api/aster/resources/query");
        Assert.Equal("Supplier", query.Body!["definitionId"]!.GetValue<string>());
    }

    [Fact]
    public async Task ResourceList_ValidatesTheQueryAndListsResources()
    {
        await server.RegisterAsync(AsterTestServer.Supplier());
        foreach (var id in new[] { "acme", "globex" })
            await server.CreateAsync("Supplier", id, new JsonObject { ["Info"] = new JsonObject { ["Name"] = id } });
        context.Render<MudPopoverProvider>();

        var list = context.Render<ResourceList>(parameters => parameters.Add(page => page.DefinitionId, "Supplier"));
        list.WaitForElement("tr[data-aster-resource='globex']", Timeout);
        Assert.Equal(2, list.FindAll("tr[data-aster-resource]").Count);
        Assert.Contains(server.Recorder.Requests, static request => request.PathAndQuery == "/api/aster/resources/query/validate");

        await RaiseAsync(list, () => list.Find(".aster-search input").Change("glo"));
        await RaiseAsync(list, () => list.Find("button.aster-search-button").Click());
        list.WaitForAssertion(() => Assert.Single(list.FindAll("tr[data-aster-resource]")), Timeout);
        Assert.Equal("aster/resources/globex", list.Find("tr[data-aster-resource='globex'] a").GetAttribute("href"));
    }
}
