namespace PvpGuide.Domain.Timeline;

public sealed class TrajectorySamplingSettings
{
    public TrajectorySamplingSettings(string policyVersion, int maximumUniformRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        if (maximumUniformRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumUniformRate),
                "Maximum uniform sample rate must be positive.");
        }

        PolicyVersion = policyVersion;
        MaximumUniformRate = maximumUniformRate;
    }

    public string PolicyVersion { get; }

    public int MaximumUniformRate { get; }
}
