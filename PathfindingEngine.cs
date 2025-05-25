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
            // Enhanced pathfinding implementation
            var path = new List<Vector2i>();
            
            var current = new Vector2i((int)start.X, (int)start.Y);
            var target = new Vector2i((int)end.X, (int)end.Y);
            
            // Calculate optimal step size based on distance
            var distance = Vector2.Distance(start, end);
            var stepSize = Math.Max(5, Math.Min(15, (int)(distance / 10))); // Adaptive step size
            var steps = (int)(distance / stepSize);
            
            for (int i = 0; i <= steps; i++)
            {
                var t = (float)i / steps;
                var interpolated = Vector2.Lerp(start, end, t);
                path.Add(new Vector2i((int)interpolated.X, (int)interpolated.Y));
            }
            
            // Ensure we end exactly at target
            if (path.Count == 0 || Vector2.Distance(new Vector2(path.Last().X, path.Last().Y), end) > 5)
            {
                path.Add(target);
            }
            
            return path;
        }

        public List<Vector2i> CropPath(List<Vector2i> fullPath, Vector2 playerPos, int radius)
        {
            if (fullPath.Count == 0) return new List<Vector2i>();
            
            var croppedPath = new List<Vector2i>();
            var playerGrid = new Vector2i((int)playerPos.X, (int)playerPos.Y);
            
            // Find the closest point on the path to the player
            var closestIndex = 0;
            var closestDistance = float.MaxValue;
            
            for (int i = 0; i < fullPath.Count; i++)
            {
                var distance = Vector2.Distance(playerPos, new Vector2(fullPath[i].X, fullPath[i].Y));
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }
            
            // Add points from closest index forward, within radius
            for (int i = closestIndex; i < fullPath.Count; i++)
            {
                var point = fullPath[i];
                var distance = Vector2.Distance(playerPos, new Vector2(point.X, point.Y));
                
                if (distance <= radius)
                {
                    croppedPath.Add(point);
                    
                    // Limit number of points for performance
                    if (croppedPath.Count >= _navigator.Settings.ExtraPointsCount.Value)
                        break;
                }
                else if (croppedPath.Count > 0)
                {
                    // Add one more point outside radius for better pathfinding
                    croppedPath.Add(point);
                    break;
                }
            }
            
            return croppedPath.Count > 0 ? croppedPath : new List<Vector2i> { fullPath.FirstOrDefault() };
        }

        // Enhanced TSP optimization - Python bot style
        public List<Vector2i> OptimizePathTSP(List<Vector2i> originalPath)
        {
            if (originalPath.Count <= 3) return originalPath;
            
            // Use nearest neighbor algorithm with 2-opt improvement
            var optimized = NearestNeighborTSP(originalPath);
            
            // Apply 2-opt improvement for better results
            if (optimized.Count > 4)
            {
                optimized = TwoOptImprovement(optimized);
            }
            
            return optimized;
        }

        private List<Vector2i> NearestNeighborTSP(List<Vector2i> points)
        {
            var optimized = new List<Vector2i>();
            var remaining = new List<Vector2i>(points);
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

        private List<Vector2i> TwoOptImprovement(List<Vector2i> tour)
        {
            var improved = new List<Vector2i>(tour);
            var bestDistance = CalculateTotalDistance(improved);
            
            for (int i = 1; i < improved.Count - 2; i++)
            {
                for (int k = i + 1; k < improved.Count; k++)
                {
                    if (k - i == 1) continue; // Skip adjacent edges
                    
                    var newTour = TwoOptSwap(improved, i, k);
                    var newDistance = CalculateTotalDistance(newTour);
                    
                    if (newDistance < bestDistance)
                    {
                        improved = newTour;
                        bestDistance = newDistance;
                    }
                }
            }
            
            return improved;
        }

        private List<Vector2i> TwoOptSwap(List<Vector2i> tour, int i, int k)
        {
            var newTour = new List<Vector2i>();
            
            // Add route from start to i
            for (int j = 0; j <= i - 1; j++)
                newTour.Add(tour[j]);
            
            // Add route from i to k in reverse
            for (int j = k; j >= i; j--)
                newTour.Add(tour[j]);
            
            // Add route from k+1 to end
            for (int j = k + 1; j < tour.Count; j++)
                newTour.Add(tour[j]);
            
            return newTour;
        }

        private float CalculateTotalDistance(List<Vector2i> tour)
        {
            float totalDistance = 0;
            for (int i = 0; i < tour.Count - 1; i++)
            {
                totalDistance += Vector2.Distance(
                    new Vector2(tour[i].X, tour[i].Y),
                    new Vector2(tour[i + 1].X, tour[i + 1].Y));
            }
            return totalDistance;
        }

        // Generate discovery points for exploration - Python bot style
        public List<Vector2> GenerateDiscoveryPoints(Vector2 playerPos, float radius, int pointCount)
        {
            var points = new List<Vector2>();
            
            // Grid-based point generation for better coverage
            var gridSize = (int)(radius / 4); // 4x4 grid sections
            var sectionsPerSide = 4;
            
            for (int x = -sectionsPerSide; x <= sectionsPerSide; x++)
            {
                for (int y = -sectionsPerSide; y <= sectionsPerSide; y++)
                {
                    if (x == 0 && y == 0) continue; // Skip center (player position)
                    
                    var point = playerPos + new Vector2(x * gridSize, y * gridSize);
                    var distance = Vector2.Distance(playerPos, point);
                    
                    if (distance <= radius && distance >= 20) // Minimum distance from player
                    {
                        points.Add(point);
                    }
                }
            }
            
            // Add some random points for better exploration
            var random = new Random();
            for (int i = 0; i < pointCount / 4; i++)
            {
                var angle = random.NextDouble() * 2 * Math.PI;
                var distance = random.Next(30, (int)radius);
                
                var point = playerPos + new Vector2(
                    (float)(Math.Cos(angle) * distance),
                    (float)(Math.Sin(angle) * distance));
                
                points.Add(point);
            }
            
            // Limit to requested point count
            if (points.Count > pointCount)
            {
                points = points.OrderBy(p => Vector2.Distance(playerPos, p)).Take(pointCount).ToList();
            }
            
            return points;
        }

        public void ClearCache()
        {
            _pathCache.Clear();
        }
    }
}