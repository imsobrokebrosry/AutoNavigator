using System;
using System.Numerics;
using ExileCore2.PoEMemory.Components;
using GameOffsets2.Native;

namespace AutoNavigator;

public static class AutoNavigatorExtensions
{
    /// <summary>
    /// Converts render position to grid coordinates
    /// </summary>
    public static Vector3 GridPos(this Render render)
    {
        return render.Pos / AutoNavigator.GridToWorldMultiplier;
    }

    /// <summary>
    /// Truncates Vector2 to Vector2i
    /// </summary>
    public static Vector2i Truncate(this Vector2 v)
    {
        return new Vector2i((int)v.X, (int)v.Y);
    }

    /// <summary>
    /// Converts Vector2i to Vector2
    /// </summary>
    public static Vector2 ToVector2(this Vector2i v)
    {
        return new Vector2(v.X, v.Y);
    }

    /// <summary>
    /// Calculates distance between two Vector2i points
    /// </summary>
    public static float DistanceTo(this Vector2i from, Vector2i to)
    {
        return Vector2.Distance(from.ToVector2(), to.ToVector2());
    }

    /// <summary>
    /// Calculates angle between two points in degrees
    /// </summary>
    public static float AngleTo(this Vector2 from, Vector2 to)
    {
        var direction = to - from;
        return (float)(Math.Atan2(direction.Y, direction.X) * 180.0 / Math.PI);
    }

    /// <summary>
    /// Normalizes angle to 0-360 range
    /// </summary>
    public static float NormalizeAngle(this float angle)
    {
        while (angle < 0) angle += 360;
        while (angle >= 360) angle -= 360;
        return angle;
    }

    /// <summary>
    /// Checks if a point is within a certain radius of another point
    /// </summary>
    public static bool IsWithinRadius(this Vector2 point, Vector2 center, float radius)
    {
        return Vector2.Distance(point, center) <= radius;
    }

    /// <summary>
    /// Gets the midpoint between two vectors
    /// </summary>
    public static Vector2 Midpoint(this Vector2 from, Vector2 to)
    {
        return (from + to) * 0.5f;
    }

    /// <summary>
    /// Clamps a vector to a maximum length
    /// </summary>
    public static Vector2 ClampLength(this Vector2 vector, float maxLength)
    {
        if (vector.Length() <= maxLength)
            return vector;
        
        return Vector2.Normalize(vector) * maxLength;
    }
}