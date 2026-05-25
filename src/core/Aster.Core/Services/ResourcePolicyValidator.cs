using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Policies;

namespace Aster.Core.Services;

/// <summary>
/// Default validation for definition-attached policy declarations.
/// </summary>
public sealed class ResourcePolicyValidator : IResourcePolicyValidator
{
    /// <inheritdoc />
    public ResourcePolicyValidationResult Validate(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var diagnostics = new List<ResourcePolicyDiagnostic>();
        var declarations = definition.PolicyDeclarations ?? [];
        var policyIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < declarations.Count; index++)
        {
            var declaration = declarations[index];
            var path = $"policyDeclarations/{index}";

            if (declaration is null)
            {
                diagnostics.Add(Diagnostic(
                    ResourcePolicyDiagnosticCodes.PolicyInvalid,
                    "Policy declaration cannot be null.",
                    path));
                continue;
            }

            ValidateDeclarationShape(declaration, path, policyIds, diagnostics);
            ValidateDeclarationCompatibility(declaration, path, diagnostics);
            ValidateCriteria(declaration, path, diagnostics);
        }

        return diagnostics.Count == 0
            ? ResourcePolicyValidationResult.Success
            : new ResourcePolicyValidationResult { Diagnostics = diagnostics };
    }

    private static void ValidateDeclarationShape(
        ResourcePolicyDeclaration declaration,
        string path,
        ISet<string> policyIds,
        ICollection<ResourcePolicyDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(declaration.PolicyId))
        {
            diagnostics.Add(Diagnostic(
                ResourcePolicyDiagnosticCodes.PolicyInvalid,
                "Policy ID is required.",
                $"{path}/policyId"));
        }
        else if (!policyIds.Add(declaration.PolicyId))
        {
            diagnostics.Add(Diagnostic(
                ResourcePolicyDiagnosticCodes.PolicyConflict,
                $"Policy ID '{declaration.PolicyId}' is declared more than once.",
                $"{path}/policyId",
                declaration.PolicyId));
        }

        if (!Enum.IsDefined(declaration.Kind))
        {
            diagnostics.Add(Diagnostic(
                ResourcePolicyDiagnosticCodes.PolicyKindUnsupported,
                $"Policy kind '{declaration.Kind}' is not supported.",
                $"{path}/kind",
                declaration.PolicyId));
        }

        if (!Enum.IsDefined(declaration.Target))
        {
            diagnostics.Add(Diagnostic(
                ResourcePolicyDiagnosticCodes.PolicyTargetInvalid,
                $"Policy target '{declaration.Target}' is not supported.",
                $"{path}/target",
                declaration.PolicyId));
        }

        if (!Enum.IsDefined(declaration.Outcome))
        {
            diagnostics.Add(Diagnostic(
                ResourcePolicyDiagnosticCodes.PolicyOutcomeUnsupported,
                $"Policy outcome '{declaration.Outcome}' is not supported.",
                $"{path}/outcome",
                declaration.PolicyId));
        }
    }

    private static void ValidateDeclarationCompatibility(
        ResourcePolicyDeclaration declaration,
        string path,
        ICollection<ResourcePolicyDiagnostic> diagnostics)
    {
        var valid = declaration.Kind switch
        {
            ResourcePolicyKind.Retention =>
                declaration.Target == ResourcePolicyTarget.Resource
                && declaration.Outcome == ResourcePolicyOutcome.Retain,
            ResourcePolicyKind.Archival =>
                declaration.Target == ResourcePolicyTarget.Resource
                && declaration.Outcome == ResourcePolicyOutcome.Archive,
            ResourcePolicyKind.SoftDelete =>
                declaration.Target == ResourcePolicyTarget.Resource
                && declaration.Outcome == ResourcePolicyOutcome.SoftDelete,
            ResourcePolicyKind.VersionPruning =>
                declaration.Target == ResourcePolicyTarget.ResourceVersion
                && declaration.Outcome == ResourcePolicyOutcome.PrunePreview,
            _ => true,
        };

        if (!valid)
        {
            diagnostics.Add(Diagnostic(
                ResourcePolicyDiagnosticCodes.PolicyInvalid,
                $"Policy '{declaration.PolicyId}' has incompatible kind, target, or outcome.",
                path,
                declaration.PolicyId));
        }
    }

    private static void ValidateCriteria(
        ResourcePolicyDeclaration declaration,
        string path,
        ICollection<ResourcePolicyDiagnostic> diagnostics)
    {
        var criteria = declaration.Criteria;
        if (criteria is null)
        {
            diagnostics.Add(Diagnostic(
                ResourcePolicyDiagnosticCodes.PolicyInvalid,
                "Policy criteria are required.",
                $"{path}/criteria",
                declaration.PolicyId));
            return;
        }

        if (criteria.MinimumAge is { } minimumAge && minimumAge <= TimeSpan.Zero)
        {
            diagnostics.Add(Diagnostic(
                ResourcePolicyDiagnosticCodes.PolicyInvalid,
                "Minimum age must be greater than zero.",
                $"{path}/criteria/minimumAge",
                declaration.PolicyId));
        }

        if (criteria.MaximumRetainedVersions is { } retainedVersions && retainedVersions <= 0)
        {
            diagnostics.Add(Diagnostic(
                ResourcePolicyDiagnosticCodes.PolicyInvalid,
                "Maximum retained versions must be greater than zero.",
                $"{path}/criteria/maximumRetainedVersions",
                declaration.PolicyId));
        }

        if (declaration.Kind == ResourcePolicyKind.VersionPruning && criteria.MaximumRetainedVersions is null)
        {
            diagnostics.Add(Diagnostic(
                ResourcePolicyDiagnosticCodes.PolicyInvalid,
                "Version pruning policies require a maximum retained version count.",
                $"{path}/criteria/maximumRetainedVersions",
                declaration.PolicyId));
        }

        if (criteria.ActivationState == ResourcePolicyActivationState.Active
            && string.IsNullOrWhiteSpace(criteria.ActivationChannel))
        {
            diagnostics.Add(Diagnostic(
                ResourcePolicyDiagnosticCodes.PolicyInvalid,
                "Active policy criteria require an activation channel.",
                $"{path}/criteria/activationChannel",
                declaration.PolicyId));
        }

        if (criteria.ActivationState is { } activationState && !Enum.IsDefined(activationState))
        {
            diagnostics.Add(Diagnostic(
                ResourcePolicyDiagnosticCodes.PolicyInvalid,
                $"Activation state '{activationState}' is not supported.",
                $"{path}/criteria/activationState",
                declaration.PolicyId));
        }

        if (criteria.LifecycleState is { } lifecycleState && !Enum.IsDefined(lifecycleState))
        {
            diagnostics.Add(Diagnostic(
                ResourcePolicyDiagnosticCodes.PolicyInvalid,
                $"Lifecycle state '{lifecycleState}' is not supported.",
                $"{path}/criteria/lifecycleState",
                declaration.PolicyId));
        }

        if (!string.IsNullOrWhiteSpace(criteria.UnsupportedFacetPredicate))
        {
            diagnostics.Add(Diagnostic(
                ResourcePolicyDiagnosticCodes.PolicyCriteriaUnsupported,
                "Arbitrary resource facet predicates are not supported by policy declarations in this slice.",
                $"{path}/criteria/unsupportedFacetPredicate",
                declaration.PolicyId));
        }
    }

    internal static ResourcePolicyDiagnostic Diagnostic(
        string code,
        string message,
        string? path = null,
        string? policyId = null,
        string? resourceId = null,
        int? resourceVersion = null) =>
        new()
        {
            Code = code,
            Message = message,
            Path = path,
            PolicyId = policyId,
            ResourceId = resourceId,
            ResourceVersion = resourceVersion,
        };
}
