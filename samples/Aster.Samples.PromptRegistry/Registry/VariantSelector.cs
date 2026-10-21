using System.Security.Cryptography;
using System.Text;

namespace Aster.Samples.PromptRegistry.Registry;

/// <summary>
/// Assigns a subject (a user or conversation) to one arm of an A/B test. The assignment depends
/// only on the resource ID, the subject and the number of arms, so a subject sees the same version
/// on every call and in every process.
/// </summary>
public static class VariantSelector
{
    /// <summary>Returns the index, in [0, <paramref name="armCount"/>), of the arm <paramref name="subjectId"/> belongs to.</summary>
    public static int Select(string resourceId, string subjectId, int armCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(armCount, 1);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{resourceId}:{subjectId}"));
        return (int)(BitConverter.ToUInt32(hash, 0) % (uint)armCount);
    }
}
