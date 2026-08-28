using System.Collections.ObjectModel;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Editor.Features.Trajectory;

public static class TrajectoryTickSelectionPolicy
{
    public const int MaximumTickRate = 5;

    public static IReadOnlyList<int> SelectOrderedSampleIndices(
        ActorMovementTrajectory trajectory,
        int? uniformRate)
    {
        ArgumentNullException.ThrowIfNull(trajectory);
        if (uniformRate is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uniformRate),
                "Uniform rate must be positive when present.");
        }

        if (trajectory.Samples.Count == 0)
        {
            return Array.AsReadOnly(Array.Empty<int>());
        }

        if (uniformRate is null)
        {
            return Array.AsReadOnly(Enumerable.Range(0, trajectory.Samples.Count).ToArray());
        }

        if (uniformRate.Value <= MaximumTickRate)
        {
            return Array.AsReadOnly(Enumerable.Range(0, trajectory.Samples.Count).ToArray());
        }

        var uniformCandidateIndices = FindExactUniformCandidateIndices(
            trajectory.Samples,
            uniformRate.Value);
        var selectedIndices = new SortedSet<int>();
        if (uniformCandidateIndices.Count > 0)
        {
            var lastGridIndex = (long)Math.Floor(
                trajectory.Samples[^1].TimeSeconds * MaximumTickRate);
            for (long gridIndex = 0; gridIndex <= lastGridIndex; gridIndex++)
            {
                selectedIndices.Add(SelectNearestSampleIndex(
                    trajectory.Samples,
                    uniformCandidateIndices,
                    gridIndex / (double)MaximumTickRate));
            }
        }

        for (var index = 0; index < trajectory.Samples.Count; index++)
        {
            if (trajectory.Samples[index].AnchorKind != TrajectoryAnchorKind.None)
            {
                selectedIndices.Add(index);
            }
        }

        return Array.AsReadOnly(selectedIndices.ToArray());
    }

    public static int SelectNearestSampleIndex(
        IReadOnlyList<MovementTrajectorySample> samples,
        IReadOnlyList<int> candidateIndices,
        double requestedTimeSeconds)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(candidateIndices);
        if (!double.IsFinite(requestedTimeSeconds) || requestedTimeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedTimeSeconds),
                "Requested time must be finite and non-negative.");
        }

        if (candidateIndices.Count == 0)
        {
            throw new ArgumentException("At least one candidate index is required.", nameof(candidateIndices));
        }

        ValidateCandidateIndices(candidateIndices, samples.Count);
        var bestIndex = candidateIndices[0];
        var bestDistance = Math.Abs(samples[bestIndex].TimeSeconds - requestedTimeSeconds);
        for (var candidatePosition = 1; candidatePosition < candidateIndices.Count; candidatePosition++)
        {
            var candidateIndex = candidateIndices[candidatePosition];
            var distance = Math.Abs(samples[candidateIndex].TimeSeconds - requestedTimeSeconds);
            if (distance < bestDistance)
            {
                bestIndex = candidateIndex;
                bestDistance = distance;
            }
        }

        return bestIndex;
    }

    private static ReadOnlyCollection<int> FindExactUniformCandidateIndices(
        IReadOnlyList<MovementTrajectorySample> samples,
        int uniformRate)
    {
        var indices = new List<int>();
        for (var index = 0; index < samples.Count; index++)
        {
            var sampleTime = samples[index].TimeSeconds;
            var gridIndex = Math.Round(sampleTime * uniformRate);
            if (gridIndex >= 0 && sampleTime == gridIndex / uniformRate)
            {
                indices.Add(index);
            }
        }

        return indices.AsReadOnly();
    }

    private static void ValidateCandidateIndices(
        IReadOnlyList<int> candidateIndices,
        int sampleCount)
    {
        for (var position = 0; position < candidateIndices.Count; position++)
        {
            var sampleIndex = candidateIndices[position];
            if (sampleIndex < 0 || sampleIndex >= sampleCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(candidateIndices),
                    "Candidate indices must refer to existing samples.");
            }

            if (position > 0 && candidateIndices[position - 1] >= sampleIndex)
            {
                throw new ArgumentException(
                    "Candidate indices must be strictly increasing without duplicates.",
                    nameof(candidateIndices));
            }
        }
    }
}
