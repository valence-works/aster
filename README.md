# Aster

Aster is a .NET SDK for defining Resource Types and Resources.

### Example: Defining a Page resource

The following snippet demonstrates how to define a `Page` resource type with a
`Title` aspect and then create a new resource item:

```csharp
using Aster.Core.Resources;

using var store = new ResourceStore("Data Source=aster.db");
var manager = new ResourceManager(store);

// Define the aspect and type
var titleAspect = new AspectDefinition
{
    Name = "Title",
    Facets = { new FacetDefinition { Name = "Text", Type = "string" } }
};

var pageType = new ResourceTypeDefinition
{
    Name = "Page",
    Aspects = { titleAspect }
};

await manager.SaveTypeAsync(pageType);

// Create a new Page item
var item = new ResourceItem { Type = "Page" };
item.Aspects["Title"] = new { Text = "Hello world" };
await manager.CreateItemAsync(item);
```

