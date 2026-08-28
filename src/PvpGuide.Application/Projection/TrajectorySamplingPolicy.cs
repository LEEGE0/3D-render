using PvpGuide.Domain.Timeline;

namespace PvpGuide.Application.Projection;

public static class TrajectorySamplingPolicy
{
    public const string Version = "lock-on-motion/v1";
    public const int MaximumUniformRate = 30;
    public const int MaximumTickRate = 5;

    public static TrajectorySamplingSettings CreateSettings() =>
        new(Version, MaximumUniformRate);

    public static void ValidatePlan(TrajectorySamplePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!string.Equals(plan.PolicyVersion, Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Projection source returned sampling policy '{plan.PolicyVersion}', expected '{Version}'.");
        }

        if (plan.UniformRate > MaximumUniformRate)
        {
            throw new InvalidOperationException(
                $"Projection source returned {plan.UniformRate}Hz samples, above the {MaximumUniformRate}Hz limit.");
        }
    }
}
