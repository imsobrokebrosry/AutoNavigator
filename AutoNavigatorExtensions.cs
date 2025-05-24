using System.Numerics;
using ExileCore2.PoEMemory.Components;
using GameOffsets2.Native; 

namespace AutoNavigator;

public static class AutoNavigatorExtensions
{
    public static Vector3 GridPos(this Render render)
    {
        return render.Pos / AutoNavigator.GridToWorldMultiplier;
    }

    public static Vector2i Truncate(this Vector2 v)
    {
        return new Vector2i((int)v.X, (int)v.Y);
    }
}