using System;
using System.Collections.Generic;
using System.Numerics;

namespace AutoNavigator
{
    public enum StuckAction
    {
        Continue,
        Regenerate,
        MoveAround,
        Stop
    }

    public struct StuckDetectionResult
    {
        public bool IsStuck { get; set; }
        public string Reason { get; set; }
        public StuckAction RecommendedAction { get; set; }
    }

    public class StuckDetector
    {
        private readonly AutoNavigator _navigator;
        private readonly Queue<Vector2> _recentPositions = new();
        private Vector2 _lastPosition = Vector2.Zero;
        private float _lastDistance = float.MaxValue;
        private int _sameDistanceCount = 0;
        private DateTime _lastMovementTime = DateTime.Now;

        public StuckDetector(AutoNavigator navigator)
        {
            _navigator = navigator;
        }

        public StuckDetectionResult CheckIfStuck(Vector2 currentPos, Vector2 targetPos, float distanceToTarget)
        {
            var result = new StuckDetectionResult { IsStuck = false };

            // Track recent positions
            _recentPositions.Enqueue(currentPos);
            if (_recentPositions.Count > 10)
                _recentPositions.Dequeue();

            // Check if player hasn't moved significantly
            if (Vector2.Distance(currentPos, _lastPosition) < 1.0f)
            {
                if ((DateTime.Now - _lastMovementTime).TotalSeconds > 3)
                {
                    result.IsStuck = true;
                    result.Reason = "Player hasn't moved for 3 seconds";
                    result.RecommendedAction = StuckAction.MoveAround;
                }
            }
            else
            {
                _lastMovementTime = DateTime.Now;
            }

            // Check if distance to target is not decreasing
            if (Math.Abs(distanceToTarget - _lastDistance) < 0.5f)
            {
                _sameDistanceCount++;
                if (_sameDistanceCount > _navigator.Settings.StuckDetectionThreshold.Value)
                {
                    result.IsStuck = true;
                    result.Reason = "Distance to target not decreasing";
                    result.RecommendedAction = StuckAction.Regenerate;
                }
            }
            else
            {
                _sameDistanceCount = 0;
            }

            // Check if player is oscillating between positions
            if (_recentPositions.Count >= 6)
            {
                var positions = new List<Vector2>(_recentPositions);
                var oscillating = true;
                for (int i = 0; i < 3; i++)
                {
                    if (Vector2.Distance(positions[i], positions[i + 3]) > 2.0f)
                    {
                        oscillating = false;
                        break;
                    }
                }

                if (oscillating)
                {
                    result.IsStuck = true;
                    result.Reason = "Player is oscillating between positions";
                    result.RecommendedAction = StuckAction.MoveAround;
                }
            }

            _lastPosition = currentPos;
            _lastDistance = distanceToTarget;

            return result;
        }

        public Vector2 FindAvoidancePoint(Vector2 currentPos, int radius)
        {
            // Find a point around the current position to move to
            var angle = Random.Shared.NextDouble() * 2 * Math.PI;
            var distance = Random.Shared.Next(radius / 2, radius);
            
            var x = currentPos.X + (float)(Math.Cos(angle) * distance);
            var y = currentPos.Y + (float)(Math.Sin(angle) * distance);
            
            return new Vector2(x, y);
        }
    }
}