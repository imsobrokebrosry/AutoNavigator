using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GameOffsets2.Native;

namespace AutoNavigator
{
    public class PathfindingEngine
    {
        private readonly AutoNavigator _navigator;
        private readonly Dictionary<string, List<Vector2i>> _pathCache = new();

        public PathfindingEngine(AutoNavigator navigator)
        {
            _navigator = navigator;
        }

        public async Task<List<Vector2i>> GeneratePathAsync(Vector2 start, Vector2 end, CancellationToken cancellationToken)
        {
            return await Task.Run(() => GeneratePath(start, end), cancellationToken);
        }

        public List<Vector2i> GeneratePath(Vector2 start, Vector2 end)
        {
            // Simple pathfinding implementation
            // In a real implementation, this would use A* or similar algorithm
            var path = new List<Vector2i>();
            
            var current = new Vector2i((int)start.X, (int)start.Y);
            var target = new Vector2i((int)end.X, (int)end.Y);
            
            // Simple line interpolation (replace with proper A* algorithm)
            var distance = Vector2.Distance(start, end);
            var steps = (int)(distance / 5); // 5 units per step
            
            for (int i = 0; i <= steps; i++)
            {
                var t = (float)i / steps;
                var interpolated = Vector2.Lerp(start, end, t);
                path.Add(new Vector2i((int)interpolated.X, (int)interpolated.Y));
            }
            
            return path;
        }

        public List<Vector2i> CropPath(List<Vector2i> fullPath, Vector2 playerPos, int radius)
        {
            if (fullPath.Count == 0) return new List<Vector2i>();
            
            var croppedPath = new List<Vector2i>();
            var playerGrid = new Vector2i((int)playerPos.X, (int)playerPos.Y);
            
            // Find points within radius that are reachable
            for (int i = fullPath.Count - 1; i >= 0; i--)
            {
                var point = fullPath[i];
                var distance = Vector2.Distance(playerPos, new Vector2(point.X, point.Y));
                
                if (distance <= radius)
                {
                    // Add this point and a few more ahead
                    var startIndex = Math.Max(0, i);
                    var endIndex = Math.Min(fullPath.Count, i + _navigator.Settings.ExtraPointsCount.Value);
                    
                    for (int j = startIndex; j < endIndex; j++)
                    {
                        croppedPath.Add(fullPath[j]);
                    }
                    break;
                }
            }
            
            return croppedPath.Count > 0 ? croppedPath : new List<Vector2i> { fullPath.FirstOrDefault() };
        }

        public List<Vector2i> OptimizePathTSP(List<Vector2i> originalPath)
        {
            if (originalPath.Count <= 3) return originalPath;
            
            // Simple TSP optimization - nearest neighbor
            var optimized = new List<Vector2i>();
            var remaining = new List<Vector2i>(originalPath);
            var current = remaining.First();
            
            optimized.Add(current);
            remaining.Remove(current);
            
            while (remaining.Count > 0)
            {
                var nearest = remaining.OrderBy(p => 
                    Vector2.Distance(new Vector2(current.X, current.Y), new Vector2(p.X, p.Y))).First();
                
                optimized.Add(nearest);
                remaining.Remove(nearest);
                current = nearest;
            }
            
            return optimized;
        }

        public void ClearCache()
        {
            _pathCache.Clear();
        }
    }
}