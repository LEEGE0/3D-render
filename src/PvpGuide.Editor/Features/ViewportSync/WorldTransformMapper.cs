using PvpGuide.Domain;

namespace PvpGuide.Editor.Features.ViewportSync;

public readonly record struct WorldPosition
{
    public WorldPosition(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "World position components must be finite.");
        }

        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }

    public double Y { get; }

    public double Z { get; }
}

public static class WorldTransformMapper
{
    public static WorldPosition ToWorldPosition(Position3 position) =>
        new(position.X, position.Y, position.Z);

    public static double ToRotationYRadians(double yawDegrees) =>
        -(yawDegrees * (Math.PI / 180));
}
