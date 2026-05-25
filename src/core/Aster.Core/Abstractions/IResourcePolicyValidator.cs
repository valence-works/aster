using Aster.Core.Models.Definitions;
using Aster.Core.Models.Policies;

namespace Aster.Core.Abstractions;

/// <summary>
/// Validates resource policy declarations without mutating resources or definitions.
/// </summary>
public interface IResourcePolicyValidator
{
    /// <summary>
    /// Validates policy declarations attached to a resource definition.
    /// </summary>
    /// <param name="definition">The definition whose policies should be validated.</param>
    /// <returns>Policy validation result.</returns>
    ResourcePolicyValidationResult Validate(ResourceDefinition definition);
}
