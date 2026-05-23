namespace Aster.Core.Models.Lifecycle;

/// <summary>
/// Lifecycle points where host hooks can observe or gate core operations.
/// </summary>
public enum LifecyclePoint
{
    /// <summary>
    /// Before a resource version is saved.
    /// </summary>
    BeforeSave,

    /// <summary>
    /// After a resource version has been saved.
    /// </summary>
    AfterSave,

    /// <summary>
    /// Before a resource version is activated in a channel.
    /// </summary>
    BeforeActivate,

    /// <summary>
    /// After a resource version has been activated in a channel.
    /// </summary>
    AfterActivate,

    /// <summary>
    /// Before a resource version is deactivated from a channel.
    /// </summary>
    BeforeDeactivate,

    /// <summary>
    /// After a resource version has been deactivated from a channel.
    /// </summary>
    AfterDeactivate,

    /// <summary>
    /// Before a portable snapshot is exported.
    /// </summary>
    BeforeExport,

    /// <summary>
    /// After a portable snapshot has been exported.
    /// </summary>
    AfterExport,

    /// <summary>
    /// Before an import preview is planned.
    /// </summary>
    BeforePreviewImport,

    /// <summary>
    /// After an import preview has been planned.
    /// </summary>
    AfterPreviewImport,

    /// <summary>
    /// Before a portable snapshot import is applied.
    /// </summary>
    BeforeImport,

    /// <summary>
    /// After a portable snapshot import has completed.
    /// </summary>
    AfterImport,
}

/// <summary>
/// Resource save operation that triggered a lifecycle hook.
/// </summary>
public enum ResourceSaveKind
{
    /// <summary>
    /// A new resource is being created.
    /// </summary>
    Create,

    /// <summary>
    /// An existing resource is being updated with a new version.
    /// </summary>
    Update,

    /// <summary>
    /// An existing resource is being upgraded to a newer definition schema version.
    /// </summary>
    SchemaUpgrade,
}

/// <summary>
/// Hook outcome status.
/// </summary>
public enum LifecycleHookOutcomeStatus
{
    /// <summary>
    /// Continue with the next hook or underlying operation.
    /// </summary>
    Continue,

    /// <summary>
    /// Reject the operation before mutation.
    /// </summary>
    Rejected,

    /// <summary>
    /// Report a hook failure.
    /// </summary>
    Failed,
}
