using System.Text.Json.Nodes;
using Elsa.Api.Client.Resources.ActivityDescriptors.Models;
using Elsa.Api.Client.Resources.WorkflowDefinitions.Models;
using Elsa.Studio.Contracts;
using Elsa.Studio.Models;

namespace Aster.Elsa.Studio.Forms;

/// <summary>
/// Builds the <see cref="DisplayInputEditorContext"/> an Elsa Studio <see cref="IUIHintHandler"/> renders from.
/// Elsa's editors were made for activity inputs, so the context describes each facet as an unwrapped input
/// (<see cref="InputDescriptor.IsWrapped"/> <see langword="false"/>): the editor shows no expression syntax picker
/// and writes plain values, which <see cref="AsterFormState"/> converts back to the facet type. The workflow and
/// activity parts of the context are placeholders some editors read (the code editor reads the workflow definition
/// ID, field extensions the activity type name).
/// </summary>
internal static class AsterEditorContexts
{
    /// <summary>The placeholder activity type the contexts name.</summary>
    public const string ActivityTypeName = "Aster.Elsa.Studio.Resource";

    /// <summary>Creates the context for one field.</summary>
    public static DisplayInputEditorContext Create(
        AsterFormModel model,
        AsterFormField field,
        AsterFieldEditor editor,
        IUIHintHandler handler,
        object? value,
        bool isReadOnly,
        Func<object?, Task> onValueChanged)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(handler);

        var input = new InputDescriptor
        {
            Name = field.Key,
            TypeName = field.AsterType ?? "object",
            DisplayName = field.IsRequired ? $"{field.Label} *" : field.Label,
            Description = field.Description,
            UIHint = editor.UiHint,
            IsWrapped = false,
            IsReadOnly = isReadOnly,
            IsSensitive = AsterUiHints.Normalize(field.UiHint) == AsterUiHints.Secret,
            UISpecifications = editor.UISpecifications,
            IsBrowsable = true,
        };

        var activityDescriptor = new ActivityDescriptor
        {
            TypeName = ActivityTypeName,
            Namespace = "Aster",
            Name = "Resource",
            Category = "Aster",
            DisplayName = model.DefinitionId,
            Inputs = [input],
        };

        var context = new DisplayInputEditorContext
        {
            WorkflowDefinition = new WorkflowDefinition { DefinitionId = $"aster:{model.DefinitionId}", Name = model.DefinitionId },
            Activity = new JsonObject { ["id"] = field.Key, ["type"] = ActivityTypeName },
            ActivityDescriptor = activityDescriptor,
            InputDescriptor = input,
            UIHintHandler = handler,
            Value = value,
            IsReadOnly = isReadOnly,
        };

        // Keep Value current, since several editors re-read it when they render again.
        context.OnValueChanged = async changed =>
        {
            context.Value = changed;
            await onValueChanged(changed);
        };

        return context;
    }
}
