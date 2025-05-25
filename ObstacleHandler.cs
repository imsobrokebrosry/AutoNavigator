using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.PoEMemory.Components;
using System.Windows.Forms; // Needed for MouseButtons
using ExileCore2; // Add this for Input class
using static ExileCore2.Input; // Add this to use Input methods directly

namespace AutoNavigator
{
    public class ObstacleHandler
    {
        private readonly AutoNavigator _navigator;
        private readonly List<string> _openedDoors = new();

        public ObstacleHandler(AutoNavigator navigator)
        {
            _navigator = navigator;
        }

        public bool CheckAndOpenDoors(Vector2 playerPos)
        {
            try
            {
                var doors = GetNearbyEntities(playerPos, _navigator.Settings.DoorDetectionRadius.Value, "Door")
                    .Where(e => !_openedDoors.Contains(e.Id.ToString()))
                    .OrderBy(e => Vector2.Distance(playerPos, GetEntityPosition(e)))
                    .ToList();

                if (doors.Any())
                {
                    var nearestDoor = doors.First();
                    OpenDoor(nearestDoor);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _navigator.LogError($"Error checking doors: {ex.Message}");
            }

            return false;
        }

        public bool CheckAndUseTransitions(Vector2 playerPos)
        {
            try
            {
                var transitions = GetNearbyEntities(playerPos, _navigator.Settings.TransitionDetectionRadius.Value, "AreaTransition")
                    .OrderBy(e => Vector2.Distance(playerPos, GetEntityPosition(e)))
                    .ToList();

                if (transitions.Any())
                {
                    var nearestTransition = transitions.First();
                    UseTransition(nearestTransition);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _navigator.LogError($"Error checking transitions: {ex.Message}");
            }

            return false;
        }

        private List<Entity> GetNearbyEntities(Vector2 playerPos, float radius, string entityType)
        {
            var entities = new List<Entity>();
            try
            {
                // Use the correct way for ExileCore2:
                var allEntities = _navigator.GameController.EntityListWrapper.Entities;

                foreach (var entity in allEntities)
                {
                    if (entity?.IsValid != true || !entity.IsTargetable)
                        continue;

                    var entityPos = GetEntityPosition(entity);
                    if (entityPos == Vector2.Zero)
                        continue;

                    var distance = Vector2.Distance(playerPos, entityPos);
                    if (distance <= radius)
                    {
                        var path = entity.Path?.ToLower() ?? "";
                        if ((entityType == "Door" && (path.Contains("door") || path.Contains("gate"))) ||
                            (entityType == "AreaTransition" && path.Contains("transition")))
                        {
                            entities.Add(entity);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _navigator.LogError($"Error getting nearby entities: {ex.Message}");
            }
            return entities;
        }

        private void OpenDoor(Entity door)
        {
            try
            {
                var doorPos = GetEntityPosition(door);                var screenPos = _navigator.GameController.IngameState.Camera.WorldToScreen(
                    new Vector3(doorPos.X * AutoNavigator.GridToWorldMultiplier,
                                doorPos.Y * AutoNavigator.GridToWorldMultiplier, 0));

                // Set cursor position then click
                Input.SetCursorPos(screenPos);
                Input.Click(MouseButtons.Left);
                _openedDoors.Add(door.Id.ToString());

                _navigator.LogMessage($"Opened door at {doorPos}");
            }
            catch (Exception ex)
            {
                _navigator.LogError($"Error opening door: {ex.Message}");
            }
        }

        private void UseTransition(Entity transition)
        {
            try
            {
                var transitionPos = GetEntityPosition(transition);                var screenPos = _navigator.GameController.IngameState.Camera.WorldToScreen(
                    new Vector3(transitionPos.X * AutoNavigator.GridToWorldMultiplier,
                                transitionPos.Y * AutoNavigator.GridToWorldMultiplier, 0));

                Input.SetCursorPos(screenPos);
                Input.Click(MouseButtons.Left);

                _navigator.LogMessage($"Used transition at {transitionPos}");
            }
            catch (Exception ex)
            {
                _navigator.LogError($"Error using transition: {ex.Message}");
            }
        }

        private Vector2 GetEntityPosition(Entity entity)
        {
            try
            {
                var render = entity.GetComponent<Render>();
                if (render != null)
                {
                    return new Vector2(render.Pos.X / AutoNavigator.GridToWorldMultiplier,
                                       render.Pos.Y / AutoNavigator.GridToWorldMultiplier);
                }

                var positioned = entity.GetComponent<Positioned>();
                if (positioned != null)
                {
                    return new Vector2(positioned.GridX, positioned.GridY);
                }
            }
            catch (Exception ex)
            {
                _navigator.LogError($"Error getting entity position: {ex.Message}");
            }

            return Vector2.Zero;
        }

        public void Reset()
        {
            _openedDoors.Clear();
        }
    }
}