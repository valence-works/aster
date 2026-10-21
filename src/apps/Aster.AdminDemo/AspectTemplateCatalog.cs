namespace Aster.AdminDemo;

internal static class AspectTemplateCatalog
{
    public static IReadOnlyList<AspectTemplateDto> All { get; } =
    [
        new(
            "content",
            "Content",
            "Editorial title, summary, and body fields.",
            [
                new("title", "string", true, new FacetMetadataDto { Label = "Title", UiHint = "text", MaxLength = 200, DisplayOrder = 1 }),
                new("summary", "string", false, new FacetMetadataDto { UiHint = "multiline", MaxLength = 500, DisplayOrder = 2 }),
                new("body", "string", false, new FacetMetadataDto { UiHint = "richtext", DisplayOrder = 3 }),
            ]),
        new(
            "seo",
            "SEO",
            "Search metadata and URL slug fields.",
            [
                new("slug", "string", true, new FacetMetadataDto { UiHint = "slug", Pattern = "^[a-z0-9]+(-[a-z0-9]+)*$", DisplayOrder = 1 }),
                new("metaTitle", "string", false, new FacetMetadataDto { MaxLength = 60, DisplayOrder = 2 }),
                new("metaDescription", "string", false, new FacetMetadataDto { UiHint = "multiline", MaxLength = 160, DisplayOrder = 3 }),
            ]),
        new(
            "publishable",
            "Publishable",
            "Publication window fields.",
            [
                new("publishFrom", "datetime", false, new FacetMetadataDto { UiHint = "date", DisplayOrder = 1 }),
                new("publishUntil", "datetime", false, new FacetMetadataDto { UiHint = "date", DisplayOrder = 2 }),
            ]),
        new(
            "taggable",
            "Taggable",
            "Simple tagging metadata.",
            [
                new("tags", "string[]", false, new FacetMetadataDto { UiHint = "tags", Description = "Up to 10 distinct tags", MaxLength = 40, MaxItems = 10, UniqueItems = true, DisplayOrder = 1 }),
            ]),
        new(
            "navigation",
            "Navigation",
            "Menu ordering and visibility fields.",
            [
                new("menuOrder", "number", false, new FacetMetadataDto { MinValue = 0, MaxValue = 100, DefaultValue = 0m, DisplayOrder = 1 }),
                new("showInMenu", "boolean", false, new FacetMetadataDto { UiHint = "toggle", DefaultValue = false, DisplayOrder = 2 }),
            ]),
    ];

    public static AspectTemplateDto? Find(string key) =>
        All.FirstOrDefault(template => string.Equals(template.Key, key, StringComparison.Ordinal));
}
