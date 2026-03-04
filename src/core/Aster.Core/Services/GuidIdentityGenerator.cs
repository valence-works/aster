using Aster.Core.Abstractions;

namespace Aster.Core.Services;

/// <summary>
/// Default <see cref="IIdentityGenerator"/> implementation that generates a new <see cref="Guid"/> string.
/// </summary>
public sealed class GuidIdentityGenerator : IIdentityGenerator
{
    /// <inheritdoc />
    public string NewId() => Guid.NewGuid().ToString();
}
