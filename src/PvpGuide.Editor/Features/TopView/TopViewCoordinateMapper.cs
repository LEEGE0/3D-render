using PvpGuide.Domain;

namespace PvpGuide.Editor.Features.TopView;

public readonly record struct ScreenPoint
{
    public ScreenPoint(double x, double y)
    {
        if (!double.IsFinite(x))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Screen coordinates must be finite.");
        }

        if (!double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(y), "Screen coordinates must be finite.");
        }

        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }
}

public enum TopViewHitKind
{
    None,
    ActorBody,
    RotationHandle,
}

public sealed class TopViewCoordinateMapper
{
    public const double ActorHitRadiusPixels = 16;
    public const double RotationHandleHitRadiusPixels = 10;

    private readonly double _panelWidth;
    private readonly double _panelHeight;
    private readonly double _centerX;
    private readonly double _centerZ;
    private readonly double _pixelsPerUnit;

    public TopViewCoordinateMapper(
        double panelWidth,
        double panelHeight,
        double centerX,
        double centerZ,
        double pixelsPerUnit)
    {
        if (!double.IsFinite(panelWidth) || panelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(panelWidth), "Panel width must be finite and positive.");
        }

        if (!double.IsFinite(panelHeight) || panelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(panelHeight), "Panel height must be finite and positive.");
        }

        if (!double.IsFinite(centerX))
        {
            throw new ArgumentOutOfRangeException(nameof(centerX), "Center X must be finite.");
        }

        if (!double.IsFinite(centerZ))
        {
            throw new ArgumentOutOfRangeException(nameof(centerZ), "Center Z must be finite.");
        }

        if (!double.IsFinite(pixelsPerUnit) || pixelsPerUnit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerUnit), "Pixels per unit must be finite and positive.");
        }

        _panelWidth = panelWidth;
        _panelHeight = panelHeight;
        _centerX = centerX;
        _centerZ = centerZ;
        _pixelsPerUnit = pixelsPerUnit;
    }

    public ScreenPoint WorldToScreen(Position3 position) => new(
        (_panelWidth / 2) + ((position.X - _centerX) * _pixelsPerUnit),
        (_panelHeight / 2) + ((position.Z - _centerZ) * _pixelsPerUnit));

    public Position3 ScreenToWorld(ScreenPoint point, double preservedY) => new(
        _centerX + ((point.X - (_panelWidth / 2)) / _pixelsPerUnit),
        preservedY,
        _centerZ + ((point.Y - (_panelHeight / 2)) / _pixelsPerUnit));

    public double PointerYawDegrees(ScreenPoint pointer, ScreenPoint actorCenter)
    {
        var degrees = Math.Atan2(pointer.Y - actorCenter.Y, pointer.X - actorCenter.X) * (180 / Math.PI);
        return NormalizeYawDegrees(degrees);
    }

    public ScreenPoint RotationHandlePosition(ScreenPoint actorCenter, double yawDegrees, double distancePixels = 28)
    {
        if (!double.IsFinite(yawDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(yawDegrees), "Yaw must be finite.");
        }

        if (!double.IsFinite(distancePixels) || distancePixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(distancePixels), "Handle distance must be finite and positive.");
        }

        var radians = NormalizeYawDegrees(yawDegrees) * (Math.PI / 180);
        return new ScreenPoint(
            actorCenter.X + (distancePixels * Math.Cos(radians)),
            actorCenter.Y + (distancePixels * Math.Sin(radians)));
    }

    public bool IsWithinPanel(ScreenPoint point) =>
        point.X >= 0 && point.X <= _panelWidth && point.Y >= 0 && point.Y <= _panelHeight;

    public bool IsActorBodyHit(ScreenPoint pointer, ScreenPoint actorCenter) =>
        IsWithinPanel(pointer) && IsWithinRadius(pointer, actorCenter, ActorHitRadiusPixels);

    public bool IsRotationHandleHit(ScreenPoint pointer, ScreenPoint handleCenter) =>
        IsWithinPanel(pointer) && IsWithinRadius(pointer, handleCenter, RotationHandleHitRadiusPixels);

    public TopViewHitKind HitTest(ScreenPoint pointer, ScreenPoint actorCenter, ScreenPoint rotationHandleCenter)
    {
        if (!IsWithinPanel(pointer))
        {
            return TopViewHitKind.None;
        }

        if (IsRotationHandleHit(pointer, rotationHandleCenter))
        {
            return TopViewHitKind.RotationHandle;
        }

        return IsActorBodyHit(pointer, actorCenter)
            ? TopViewHitKind.ActorBody
            : TopViewHitKind.None;
    }

    private static bool IsWithinRadius(ScreenPoint point, ScreenPoint center, double radius)
    {
        var deltaX = point.X - center.X;
        var deltaY = point.Y - center.Y;
        return (deltaX * deltaX) + (deltaY * deltaY) <= radius * radius;
    }

    private static double NormalizeYawDegrees(double yawDegrees)
    {
        var normalized = yawDegrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
