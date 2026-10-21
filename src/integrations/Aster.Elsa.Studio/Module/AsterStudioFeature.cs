using Elsa.Studio;
using Elsa.Studio.Abstractions;
using Elsa.Studio.Contracts;
using Elsa.Studio.Models;

namespace Aster.Elsa.Studio.Module;

/// <summary>
/// The module's Elsa Studio feature. Elsa Studio's shell adds the assembly of every registered
/// <see cref="IFeature"/> to its router, which is how the module's pages become routable.
/// </summary>
internal sealed class AsterStudioFeature : FeatureBase;

/// <summary>Adds the module's menu entry: Resources, under Elsa Studio's General group.</summary>
internal sealed class AsterStudioMenu : IMenuProvider
{
    /// <summary>The route of the definition list, relative to the Studio's base path.</summary>
    public const string DefinitionsHref = "aster/definitions";

    // Material "inventory_2" (outlined), the SVG path format Elsa Studio's menu renders.
    private const string Icon = "<path d=\"M0 0h24v24H0z\" fill=\"none\"/><path d=\"M20 2H4c-1 0-2 .9-2 2v3.01c0 .72.43 1.34 1 1.69V20c0 1.1 1.1 2 2 2h14c.9 0 2-.9 2-2V8.7c.57-.35 1-.97 1-1.69V4c0-1.1-1-2-2-2zm-1 18H5V9h14v11zm1-13H4V4h16v3z\"/><path d=\"M9 12h6v2H9z\"/>";

    /// <inheritdoc />
    public ValueTask<IEnumerable<MenuItem>> GetMenuItemsAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IEnumerable<MenuItem>>(
        [
            new MenuItem
            {
                Icon = Icon,
                Href = DefinitionsHref,
                Text = "Resources",
                GroupName = MenuItemGroups.General.Name,
                Order = 50,
            },
        ]);
}
