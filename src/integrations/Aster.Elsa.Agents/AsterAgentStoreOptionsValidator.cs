using Microsoft.Extensions.Options;

namespace Aster.Elsa.Agents;

/// <summary>Validates <see cref="AsterAgentStoreOptions"/>: the channel must be a usable Aster channel name.</summary>
internal sealed class AsterAgentStoreOptionsValidator : IValidateOptions<AsterAgentStoreOptions>
{
    public ValidateOptionsResult Validate(string? name, AsterAgentStoreOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Channel))
            return ValidateOptionsResult.Fail("AsterAgentStoreOptions.Channel is required.");

        if (!string.Equals(options.Channel, options.Channel.Trim(), StringComparison.Ordinal))
            return ValidateOptionsResult.Fail("AsterAgentStoreOptions.Channel must not start or end with whitespace.");

        return ValidateOptionsResult.Success;
    }
}
