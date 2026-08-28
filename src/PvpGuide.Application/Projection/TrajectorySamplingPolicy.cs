using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Application.Projection;

public static class TrajectorySamplingPolicy
{
    public const string Version = "lock-on-motion/v1";
    public const int MaximumUniformRate = 30;
    public const int MaximumTickRate = 5;

    public static TrajectorySamplingSettings CreateSettings() =>
        new(Version, MaximumUniformRate);

    public static void ValidatePlan(
        TrajectorySamplePlan plan,
        ProjectionSourceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!string.Equals(plan.PolicyVersion, Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Projection source returned sampling policy '{plan.PolicyVersion}', expected '{Version}'.");
        }

        var expectedUniformRate = Math.Min(metadata.FramesPerSecond, MaximumUniformRate);
        if (plan.UniformRate != expectedUniformRate)
        {
            throw new InvalidOperationException(
                $"Projection source returned uniform rate {plan.UniformRate}Hz, expected {expectedUniformRate}Hz.");
        }
    }
}
