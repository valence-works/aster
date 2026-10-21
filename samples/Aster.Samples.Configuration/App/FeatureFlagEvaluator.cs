using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Aster.Samples.Configuration.App;

/// <summary>
/// Answers "is this flag on for this tenant and subject?" from the current options, so a promotion
/// or rollback takes effect on the next call.
/// </summary>
public sealed class FeatureFlagEvaluator(IOptionsMonitor<FeatureFlagsOptions> flags)
{
    /// <summary>
    /// Returns whether <paramref name="key"/> is on. An unknown or disabled flag is off; a flag
    /// limited to tenants is off for other tenants; a partial rollout is on for a stable share of
    /// subjects, chosen by hashing the flag key with <paramref name="subjectId"/>.
    /// </summary>
    public bool IsEnabled(string key, string? tenantId = null, string? subjectId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!flags.CurrentValue.TryGetValue(key, out var flag) || !flag.Enabled)
            return false;

        if (flag.AllowedTenants.Length > 0 && (tenantId is null || !flag.AllowedTenants.Contains(tenantId, StringComparer.Ordinal)))
            return false;

        return flag.RolloutPercentage switch
        {
            >= 100 => true,
            <= 0 => false,
            var percentage => subjectId is not null && Bucket(key, subjectId) < percentage,
        };
    }

    /// <summary>A stable bucket in [0, 100) for a flag and subject.</summary>
    public static int Bucket(string key, string subjectId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{key}:{subjectId}"));
        return (int)(BitConverter.ToUInt32(hash, 0) % 100);
    }
}
