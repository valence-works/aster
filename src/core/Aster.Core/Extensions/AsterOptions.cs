using Aster.Core.Models.References;

namespace Aster.Core.Extensions;

/// <summary>
/// Host options for the core SDK, configured through
/// <see cref="AsterCoreServiceCollectionExtensions.AddAsterCore(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{AsterOptions})"/>.
/// </summary>
public sealed class AsterOptions
{
    /// <summary>
    /// Gets or sets how saves treat reference values whose targets fail validation.
    /// Defaults to <see cref="ReferenceValidationMode.Strict"/>.
    /// </summary>
    public ReferenceValidationMode ReferenceValidation { get; set; } = ReferenceValidationMode.Strict;
}
