using System.Collections.ObjectModel;

namespace PvpGuide.Domain.Timeline;

public sealed class ActorMovementTrajectory
{
    private readonly ReadOnlyCollection<MovementTrajectorySample> _samples;

    public ActorMovementTrajectory(
        string actorId,
        IEnumerable<MovementTrajectorySample> samples,
        long segmentSteps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentNullException.ThrowIfNull(samples);
        if (segmentSteps < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentSteps), "Segment step count cannot be negative.");
        }

        var copiedSamples = samples.ToArray();
        if (copiedSamples.Any(sample => sample is null))
        {
            throw new ArgumentException("Trajectory samples cannot contain null values.", nameof(samples));
        }

        for (var index = 1; index < copiedSamples.Length; index++)
        {
            if (copiedSamples[index - 1].TimeSeconds >= copiedSamples[index].TimeSeconds)
            {
                throw new ArgumentException(
                    "Trajectory sample times must be strictly increasing without duplicates.",
                    nameof(samples));
            }
        }

        ActorId = actorId;
        _samples = Array.AsReadOnly(copiedSamples);
        SegmentSteps = segmentSteps;
    }

    public string ActorId { get; }

    public IReadOnlyList<MovementTrajectorySample> Samples => _samples;

    public long SegmentSteps { get; }
}
