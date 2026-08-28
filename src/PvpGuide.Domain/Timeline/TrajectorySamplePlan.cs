using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace PvpGuide.Domain.Timeline;

public sealed class TrajectorySamplePlan
{
    private const string FingerprintDomain = "pvp-guide/trajectory-sample-plan/v1";
    private readonly ReadOnlyCollection<double> _orderedTimes;

    public TrajectorySamplePlan(
        string policyVersion,
        int uniformRate,
        IEnumerable<double> orderedTimes)
        : this(policyVersion, uniformRate, orderedTimes, null)
    {
    }

    public TrajectorySamplePlan(
        string policyVersion,
        int uniformRate,
        IEnumerable<double> orderedTimes,
        string? fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        if (uniformRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(uniformRate), "Uniform sample rate must be positive.");
        }

        ArgumentNullException.ThrowIfNull(orderedTimes);
        var copiedTimes = orderedTimes.ToArray();
        for (var index = 0; index < copiedTimes.Length; index++)
        {
            var timeSeconds = copiedTimes[index];
            if (!double.IsFinite(timeSeconds) || timeSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(orderedTimes),
                    "Sample times must be finite and non-negative.");
            }

            if (index > 0 && copiedTimes[index - 1] >= timeSeconds)
            {
                throw new ArgumentException(
                    "Sample times must be strictly increasing without duplicates.",
                    nameof(orderedTimes));
            }
        }

        var computedFingerprint = ComputeFingerprint(policyVersion, uniformRate, copiedTimes);
        if (fingerprint is not null &&
            !string.Equals(fingerprint, computedFingerprint, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Sampling policy fingerprint does not match the plan payload.",
                nameof(fingerprint));
        }

        PolicyVersion = policyVersion;
        UniformRate = uniformRate;
        _orderedTimes = Array.AsReadOnly(copiedTimes);
        Fingerprint = computedFingerprint;
    }

    public string PolicyVersion { get; }

    public int UniformRate { get; }

    public IReadOnlyList<double> OrderedTimes => _orderedTimes;

    public string Fingerprint { get; }

    internal bool HasValidFingerprint() =>
        string.Equals(
            Fingerprint,
            ComputeFingerprint(PolicyVersion, UniformRate, _orderedTimes),
            StringComparison.Ordinal);

    private static string ComputeFingerprint(
        string policyVersion,
        int uniformRate,
        IReadOnlyList<double> orderedTimes)
    {
        var versionBytes = Encoding.UTF8.GetBytes(policyVersion);
        var payload = new byte[
            Encoding.UTF8.GetByteCount(FingerprintDomain) + 1 +
            sizeof(int) + versionBytes.Length +
            sizeof(int) + sizeof(int) +
            (sizeof(long) * orderedTimes.Count)];
        var offset = 0;

        offset += Encoding.UTF8.GetBytes(FingerprintDomain, payload.AsSpan(offset));
        payload[offset++] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), versionBytes.Length);
        offset += sizeof(int);
        versionBytes.CopyTo(payload.AsSpan(offset));
        offset += versionBytes.Length;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), uniformRate);
        offset += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), orderedTimes.Count);
        offset += sizeof(int);
        foreach (var timeSeconds in orderedTimes)
        {
            BinaryPrimitives.WriteInt64LittleEndian(
                payload.AsSpan(offset),
                BitConverter.DoubleToInt64Bits(timeSeconds));
            offset += sizeof(long);
        }

        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }
}
