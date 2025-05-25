using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.PoEMemory.Components;
using ExileCore2.Shared.Attributes;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;
using GameOffsets2.Native;
using static ExileCore2.Input; // Add this to use Input methods directly

namespace AutoNavigator;

public class AutoNavigatorSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new ToggleNode(true);
    
    // Movement Settings
    public HotkeyNode StartNavigationHotkey { get; set; } = new HotkeyNode(Keys.F1);
    public HotkeyNode StopNavigationHotkey { get; set; } = new HotkeyNode(Keys.F2);
    public ListNode MovementType { get; set; } = new ListNode { Values = new List<string> { "Mouse", "WASD" }, Value = "Mouse" };
    public RangeNode<int> MovementSpeed { get; set; } = new RangeNode<int>(100, 10, 500);
    public RangeNode<float> WaypointTolerance { get; set; } = new RangeNode<float>(5.0f, 1.0f, 20.0f);
    public RangeNode<int> ClickDelay { get; set; } = new RangeNode<int>(50, 10, 200);
    public RangeNode<int> StepSize { get; set; } = new RangeNode<int>(32, 20, 50);
    
    // Pathfinding Settings
    public ToggleNode UseAdvancedPathfinding { get; set; } = new ToggleNode(true);
    public RangeNode<int> PathCropRadius { get; set; } = new RangeNode<int>(25, 10, 50);
    public RangeNode<int> MaxPathLength { get; set; } = new RangeNode<int>(100, 50, 200);
    public RangeNode<int> ExtraPointsCount { get; set; } = new RangeNode<int>(4, 2, 10);
      // Obstacle Handling
    public ToggleNode AutoOpenDoors { get; set; } = new ToggleNode(true);
    public ToggleNode AutoUseTransitions { get; set; } = new ToggleNode(false);
    public RangeNode<float> DoorDetectionRadius { get; set; } = new RangeNode<float>(15.0f, 5.0f, 30.0f);
    public RangeNode<float> TransitionDetectionRadius { get; set; } = new RangeNode<float>(15.0f, 5.0f, 30.0f);    // Combat Settings
    public RangeNode<float> MonsterDetectionRadius { get; set; } = new RangeNode<float>(40.0f, 10.0f, 80.0f);
    public RangeNode<float> AttackRange { get; set; } = new RangeNode<float>(15.0f, 5.0f, 30.0f);
    public RangeNode<int> AttackDelay { get; set; } = new RangeNode<int>(300, 100, 1000); // Much faster
    
    // Exploration Settings
    public RangeNode<float> ExplorationRadius { get; set; } = new RangeNode<float>(50.0f, 20.0f, 100.0f);
    public RangeNode<int> MovementDelay { get; set; } = new RangeNode<int>(1000, 200, 2000); // 1 second default
    public RangeNode<int> ActionRandomization { get; set; } = new RangeNode<int>(200, 50, 500); // Smaller random delays
    
    // Stuck Detection
    public ToggleNode EnableStuckDetection { get; set; } = new ToggleNode(true);
    public RangeNode<int> StuckDetectionThreshold { get; set; } = new RangeNode<int>(10, 5, 20);
    public RangeNode<int> StuckRecoveryDistance { get; set; } = new RangeNode<int>(15, 10, 30);
    public RangeNode<int> TimeoutSeconds { get; set; } = new RangeNode<int>(300, 60, 600);
    
    // Visual Settings
    public ToggleNode DebugMode { get; set; } = new ToggleNode(false);
    public ToggleNode DrawCurrentTarget { get; set; } = new ToggleNode(true);
    public ToggleNode DrawFullPath { get; set; } = new ToggleNode(false);
    public ColorNode PathColor { get; set; } = new ColorNode(Color.Cyan);
    public ColorNode WaypointColor { get; set; } = new ColorNode(Color.Red);
    public ColorNode DebugTextColor { get; set; } = new ColorNode(Color.White);
    
    // Advanced Features
    public ToggleNode AutoNavigateToNearestTarget { get; set; } = new ToggleNode(false);
    public ToggleNode OptimizePathWithTSP { get; set; } = new ToggleNode(false);
    public ToggleNode UseTerrainWeights { get; set; } = new ToggleNode(true);
}

public partial class AutoNavigator : BaseSettingsPlugin<AutoNavigatorSettings>
{
    public const float GridToWorldMultiplier = 250f / 23f;
      // Navigation State
    private bool _isNavigating = false;
    private bool _isExploring = false;
    private bool _isCombating = false;
    private Entity _currentTarget = null;
    private Vector2 _explorationTarget = Vector2.Zero;
    private DateTime _lastTargetScanTime = DateTime.MinValue;
    private DateTime _lastExplorationUpdate = DateTime.MinValue;
    private CancellationTokenSource _navigationCts = new CancellationTokenSource();
      // Combat State
    private DateTime _lastAttackTime = DateTime.MinValue;
    private DateTime _lastMovementTime = DateTime.MinValue;
    private DateTime _lastActionTime = DateTime.MinValue;
    private List<Entity> _nearbyMonsters = new List<Entity>();
    private List<Vector2> _exploredAreas = new List<Vector2>();
    private Vector2 _lastPlayerPosition = Vector2.Zero;
    private Random _random = new Random();
    
    // Pathfinding
    private PathfindingEngine _pathfinder;
    private ObstacleHandler _obstacleHandler;
    private StuckDetector _stuckDetector;
    
    // Integration with other plugins
    private Func<Vector2, Action<List<Vector2i>>, CancellationToken, Task> _radarLookForRoute;
    private Func<string, int, Vector2[]> _radarClusterTarget;

    public override bool Initialise()
    {
        _pathfinder = new PathfindingEngine(this);
        _obstacleHandler = new ObstacleHandler(this);
        _stuckDetector = new StuckDetector(this);
        
        // Register hotkeys
        Input.RegisterKey(Settings.StartNavigationHotkey);
        Input.RegisterKey(Settings.StopNavigationHotkey);
        
        // Hook up hotkey events
        Settings.StartNavigationHotkey.OnValueChanged += () => { Input.RegisterKey(Settings.StartNavigationHotkey); };
        Settings.StopNavigationHotkey.OnValueChanged += () => { Input.RegisterKey(Settings.StopNavigationHotkey); };
        
        // Try to get Radar plugin methods from PluginBridge
        try
        {
            _radarLookForRoute = GameController.PluginBridge.GetMethod<Func<Vector2, Action<List<Vector2i>>, CancellationToken, Task>>("Radar.LookForRoute");
            _radarClusterTarget = GameController.PluginBridge.GetMethod<Func<string, int, Vector2[]>>("Radar.ClusterTarget");
            
            if (_radarLookForRoute != null)
            {
                LogMessage("Successfully connected to Radar plugin!");
            }
            else
            {
                LogMessage("Radar plugin not found - using fallback pathfinding");
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to connect to Radar plugin: {ex.Message}");
        }
        
        return true;
    }

    public override void AreaChange(AreaInstance area)
    {
        // Stop navigation when changing areas
        StopNavigation();
        _pathfinder?.ClearCache();
        _obstacleHandler?.Reset();
    }    public override void Render()
    {
        if (!Settings.Enable)
            return;

        // Handle hotkeys
        if (Settings.StartNavigationHotkey.PressedOnce())
        {
            if (!_isNavigating)
            {
                StartExploration();
            }
        }

        if (Settings.StopNavigationHotkey.PressedOnce())
        {
            StopNavigation();
        }

        // Main exploration and combat loop
        if (_isNavigating)
        {
            UpdateExplorationAndCombat();
        }

        // Draw debug information
        if (Settings.DebugMode)
        {
            DrawDebugInfo();
        }

        // Draw current target
        if (Settings.DrawCurrentTarget && _currentTarget != null)
        {
            DrawCurrentTarget();
        }
    }    private void StartNavigationToNearestTarget()
    {
        // This method is no longer needed - replaced by StartExploration
        StartExploration();
    }private void StopNavigation()
    {
        _isNavigating = false;
        _isExploring = false;
        _isCombating = false;
        _currentTarget = null;
        _explorationTarget = Vector2.Zero;
        _navigationCts?.Cancel();
        _navigationCts = new CancellationTokenSource();
        
        LogMessage("Navigation stopped");
    }

    private Vector2 GetPlayerPosition()
    {
        try
        {
            var player = GameController.Game.IngameState.Data.LocalPlayer;
            var playerPositionComponent = player?.GetComponent<Positioned>();
            if (playerPositionComponent == null)
                return Vector2.Zero;
            
            return new Vector2(playerPositionComponent.GridX, playerPositionComponent.GridY);
        }
        catch
        {
            return Vector2.Zero;
        }
    }    // Drawing methods
    private void DrawCurrentTarget()
    {
        if (_currentTarget == null || !IsTargetValid(_currentTarget))
            return;

        try
        {
            var targetPos = GetEntityPosition(_currentTarget);
            var screenPos = GameController.IngameState.Camera.WorldToScreen(
                new Vector3(targetPos.X * GridToWorldMultiplier, targetPos.Y * GridToWorldMultiplier, 0));

            // Draw target circle
            Graphics.DrawCircle(screenPos, 20, Color.Red, 3);
            
            // Draw target name/info
            Graphics.DrawText($"Target: {_currentTarget.Path}", 
                new Vector2(screenPos.X + 25, screenPos.Y), Color.Red);
        }
        catch (Exception ex)
        {
            LogError($"Error drawing current target: {ex.Message}");
        }
    }    private void DrawDebugInfo()
    {
        var startY = 100;
        var lineHeight = 20;
        var currentY = startY;

        Graphics.DrawText($"AutoNavigator Debug Info:", new Vector2(10, currentY), Settings.DebugTextColor);
        currentY += lineHeight;

        Graphics.DrawText($"Navigation Active: {_isNavigating}", new Vector2(10, currentY), Settings.DebugTextColor);
        currentY += lineHeight;

        Graphics.DrawText($"Exploring: {_isExploring}", new Vector2(10, currentY), Settings.DebugTextColor);
        currentY += lineHeight;

        Graphics.DrawText($"Radar Connected: {_radarClusterTarget != null}", new Vector2(10, currentY), Settings.DebugTextColor);
        currentY += lineHeight;

        Graphics.DrawText($"Current Target: {(_currentTarget?.Path ?? "None")}", new Vector2(10, currentY), Settings.DebugTextColor);
        currentY += lineHeight;

        Graphics.DrawText($"Nearby Monsters: {_nearbyMonsters.Count}", new Vector2(10, currentY), Settings.DebugTextColor);
        currentY += lineHeight;

        Graphics.DrawText($"Exploration Target: {_explorationTarget}", new Vector2(10, currentY), Settings.DebugTextColor);
        currentY += lineHeight;

        Graphics.DrawText($"Explored Areas: {_exploredAreas.Count}", new Vector2(10, currentY), Settings.DebugTextColor);
        currentY += lineHeight;

        var playerPos = GetPlayerPosition();
        Graphics.DrawText($"Player Position: {playerPos}", new Vector2(10, currentY), Settings.DebugTextColor);
        currentY += lineHeight;

        if (_currentTarget != null)
        {
            var targetPos = GetEntityPosition(_currentTarget);
            var distance = Vector2.Distance(playerPos, targetPos);
            Graphics.DrawText($"Distance to Target: {distance:F1}", new Vector2(10, currentY), Settings.DebugTextColor);
            currentY += lineHeight;
        }

        // Show last action times
        var timeSinceLastAction = (DateTime.Now - _lastActionTime).TotalMilliseconds;
        Graphics.DrawText($"Time Since Last Action: {timeSinceLastAction:F0}ms", new Vector2(10, currentY), Settings.DebugTextColor);
        currentY += lineHeight;

        var timeSinceLastMovement = (DateTime.Now - _lastMovementTime).TotalMilliseconds;
        Graphics.DrawText($"Time Since Last Movement: {timeSinceLastMovement:F0}ms", new Vector2(10, currentY), Settings.DebugTextColor);
        currentY += lineHeight;

        var timeSinceLastAttack = (DateTime.Now - _lastAttackTime).TotalMilliseconds;
        Graphics.DrawText($"Time Since Last Attack: {timeSinceLastAttack:F0}ms", new Vector2(10, currentY), Settings.DebugTextColor);
    }public override void DrawSettings()
    {
        base.DrawSettings();
        
        if (ImGuiNET.ImGui.Button("Start Exploration"))
        {
            StartExploration();
        }
        
        ImGuiNET.ImGui.SameLine();
        
        if (ImGuiNET.ImGui.Button("Stop Navigation"))
        {
            StopNavigation();
        }
        
        ImGuiNET.ImGui.Separator();
        
        if (ImGuiNET.ImGui.CollapsingHeader("Status"))
        {
            ImGuiNET.ImGui.Text($"Navigation Active: {_isNavigating}");
            ImGuiNET.ImGui.Text($"Exploring: {_isExploring}");
            ImGuiNET.ImGui.Text($"Current Target: {(_currentTarget?.Path ?? "None")}");
            ImGuiNET.ImGui.Text($"Nearby Monsters: {_nearbyMonsters.Count}");
            ImGuiNET.ImGui.Text($"Explored Areas: {_exploredAreas.Count}");
        }
    }

    public override void OnClose()
    {
        StopNavigation();
        base.OnClose();
    }

    private void StartExploration()
    {
        _isNavigating = true;
        _isExploring = true;
        _isCombating = false;
        _navigationCts = new CancellationTokenSource();
        _exploredAreas.Clear();
        
        LogMessage("Starting map exploration and monster hunting!");
    }    private void UpdateExplorationAndCombat()
    {
        var playerPos = GetPlayerPosition();
        if (playerPos == Vector2.Zero)
            return;

        // Human-like action throttling - prevent too many actions
        if (DateTime.Now - _lastActionTime < TimeSpan.FromMilliseconds(Settings.MovementDelay.Value))
            return;

        // Update explored areas
        UpdateExploredAreas(playerPos);

        // Priority 1: Combat - Look for nearby monsters more frequently
        if (DateTime.Now - _lastTargetScanTime > TimeSpan.FromMilliseconds(500)) // Scan more frequently
        {
            ScanForMonsters(playerPos);
            _lastTargetScanTime = DateTime.Now;
        }

        // Priority 2: Attack current target if in range
        if (_currentTarget != null && IsTargetValid(_currentTarget))
        {
            var targetPos = GetEntityPosition(_currentTarget);
            var distanceToTarget = Vector2.Distance(playerPos, targetPos);

            if (distanceToTarget <= Settings.AttackRange.Value)
            {
                AttackTarget(_currentTarget);
                return;
            }
            else if (distanceToTarget <= Settings.MonsterDetectionRadius.Value)
            {
                // Move towards target with delay
                MoveTowards(targetPos);
                return;
            }
            else
            {
                // Target too far, clear it
                _currentTarget = null;
            }
        }

        // Priority 3: Exploration - Find unexplored areas more frequently
        if (DateTime.Now - _lastExplorationUpdate > TimeSpan.FromSeconds(2)) // Shorter delay
        {
            UpdateExplorationTarget(playerPos);
            _lastExplorationUpdate = DateTime.Now;
        }

        // Move towards exploration target with caution
        if (_explorationTarget != Vector2.Zero)
        {
            var distanceToExploration = Vector2.Distance(playerPos, _explorationTarget);
            if (distanceToExploration > 10f) // Smaller tolerance for faster movement
            {
                MoveTowards(_explorationTarget);
            }
            else
            {
                // Reached exploration target, find new one
                _explorationTarget = Vector2.Zero;
            }
        }
    }private void ScanForMonsters(Vector2 playerPos)
    {
        _nearbyMonsters.Clear();
        
        try
        {
            // Use Radar to find monsters if available
            if (_radarClusterTarget != null)
            {
                try
                {
                    var radarMonsters = _radarClusterTarget("monsters", (int)Settings.MonsterDetectionRadius.Value);
                    if (radarMonsters != null && radarMonsters.Length > 0)
                    {
                        // Convert radar results to our monster list (we need to find the actual entities)
                        var allEntities = GameController.EntityListWrapper.Entities;
                        
                        foreach (var radarPos in radarMonsters)
                        {
                            // Find entities near radar positions
                            var nearbyEntity = allEntities
                                .Where(e => IsValidMonster(e))
                                .Where(e => Vector2.Distance(GetEntityPosition(e), radarPos) < 10f)
                                .FirstOrDefault();
                            
                            if (nearbyEntity != null)
                            {
                                _nearbyMonsters.Add(nearbyEntity);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Error using Radar for monster detection: {ex.Message}");
                }
            }
            
            // Fallback: Manual entity scanning if Radar doesn't work
            if (_nearbyMonsters.Count == 0)
            {
                var allEntities = GameController.EntityListWrapper.Entities;

                foreach (var entity in allEntities)
                {
                    if (!IsValidMonster(entity))
                        continue;

                    var entityPos = GetEntityPosition(entity);
                    if (entityPos == Vector2.Zero)
                        continue;

                    var distance = Vector2.Distance(playerPos, entityPos);
                    if (distance <= Settings.MonsterDetectionRadius.Value)
                    {
                        _nearbyMonsters.Add(entity);
                    }
                }
            }

            // Prioritize monsters (stronger/rarer first)
            if (_nearbyMonsters.Count > 0)
            {
                _currentTarget = _nearbyMonsters
                    .OrderByDescending(e => GetMonsterPriority(e)) // Highest priority first
                    .ThenBy(e => Vector2.Distance(playerPos, GetEntityPosition(e))) // Then by distance
                    .First();

                if (Settings.DebugMode)
                {
                    LogMessage($"Target acquired: {_currentTarget.Path} at distance {Vector2.Distance(playerPos, GetEntityPosition(_currentTarget)):F1}");
                }
            }
            else if (_currentTarget != null)
            {
                if (Settings.DebugMode)
                {
                    LogMessage("No monsters in range, clearing target");
                }
                _currentTarget = null;
            }
        }
        catch (Exception ex)
        {
            LogError($"Error scanning for monsters: {ex.Message}");
        }
    }

    private int GetMonsterPriority(Entity entity)
    {
        try
        {
            var path = entity.Path?.ToLower() ?? "";
            
            // Prioritize rare/strong monsters
            if (path.Contains("unique") || path.Contains("boss")) return 100;
            if (path.Contains("rare") || path.Contains("magic")) return 50;
            if (path.Contains("champion") || path.Contains("elite")) return 30;
            
            // Regular monsters
            return 10;
        }
        catch
        {
            return 1;
        }
    }

    private bool IsValidMonster(Entity entity)
    {
        try
        {
            if (entity?.IsValid != true)
                return false;

            if (!entity.IsTargetable || !entity.IsHostile)
                return false;

            if (!entity.IsAlive)
                return false;

            // Check if it's a monster (not a player, NPC, etc.)
            var path = entity.Path?.ToLower() ?? "";
            
            // Skip common non-monster entities
            if (path.Contains("player") || path.Contains("npc") || 
                path.Contains("decoration") || path.Contains("effect"))
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }    private void AttackTarget(Entity target)
    {
        // Add significant delay between attacks to avoid "too many actions"
        if (DateTime.Now - _lastAttackTime < TimeSpan.FromMilliseconds(Settings.AttackDelay.Value))
            return;

        // Add additional random delay to be more human-like
        var randomDelay = _random.Next(100, Settings.ActionRandomization.Value);
        if (DateTime.Now - _lastActionTime < TimeSpan.FromMilliseconds(randomDelay))
            return;

        try
        {
            var targetPos = GetEntityPosition(target);
            var screenPos = GameController.IngameState.Camera.WorldToScreen(
                new Vector3(targetPos.X * GridToWorldMultiplier, targetPos.Y * GridToWorldMultiplier, 0));

            // Check if screen position is valid
            var windowRect = GameController.Window.GetWindowRectangle();
            if (screenPos.X >= windowRect.Left && screenPos.X <= windowRect.Right &&
                screenPos.Y >= windowRect.Top && screenPos.Y <= windowRect.Bottom)
            {
                // Attack with left click - but only once per target acquisition
                Input.SetCursorPos(screenPos);
                
                // Add small delay before click to be more human-like
                Task.Delay(50 + _random.Next(50)).ContinueWith(_ => 
                {
                    Input.Click(MouseButtons.Left);
                });
                
                _lastAttackTime = DateTime.Now;
                _lastActionTime = DateTime.Now;

                if (Settings.DebugMode)
                {
                    LogMessage($"Attacking target at {screenPos}");
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Error attacking target: {ex.Message}");
        }
    }    private void UpdateExplorationTarget(Vector2 playerPos)
    {
        // Smart exploration logic - find the direction with least explored areas
        var bestDirection = Vector2.Zero;
        var maxUnexploredScore = 0f;
        
        // Check 8 directions around the player
        for (int angle = 0; angle < 360; angle += 45)
        {
            var radians = angle * Math.PI / 180;
            var direction = new Vector2((float)Math.Cos(radians), (float)Math.Sin(radians));
            
            // Check multiple distances in this direction
            var unexploredScore = 0f;
            for (int distance = 20; distance <= Settings.ExplorationRadius.Value; distance += 10)
            {
                var checkPos = playerPos + direction * distance;
                
                // Higher score for areas we haven't been to
                if (!IsAreaExplored(checkPos, 15f))
                {
                    unexploredScore += distance; // Prefer farther unexplored areas
                }
            }
            
            // Prefer this direction if it has more unexplored areas
            if (unexploredScore > maxUnexploredScore)
            {
                maxUnexploredScore = unexploredScore;
                bestDirection = direction;
            }
        }
        
        // Set exploration target in the best direction
        if (bestDirection != Vector2.Zero)
        {
            var targetDistance = Math.Min(40f, Settings.ExplorationRadius.Value * 0.8f);
            _explorationTarget = playerPos + bestDirection * targetDistance;
            
            if (Settings.DebugMode)
            {
                LogMessage($"Smart exploration target: {_explorationTarget} (score: {maxUnexploredScore})");
            }
        }
        else
        {
            // Fallback: pick a random unexplored direction
            for (int attempts = 0; attempts < 8; attempts++)
            {
                var angle = _random.NextDouble() * 2 * Math.PI;
                var distance = _random.Next(25, (int)Settings.ExplorationRadius.Value);
                
                var targetPos = playerPos + new Vector2(
                    (float)(Math.Cos(angle) * distance),
                    (float)(Math.Sin(angle) * distance));

                if (!IsAreaExplored(targetPos, 15f))
                {
                    _explorationTarget = targetPos;
                    
                    if (Settings.DebugMode)
                    {
                        LogMessage($"Fallback exploration target: {targetPos}");
                    }
                    break;
                }
            }
        }
    }

    private void UpdateExploredAreas(Vector2 playerPos)
    {
        // Add current position to explored areas
        if (_exploredAreas.Count == 0 || 
            Vector2.Distance(_lastPlayerPosition, playerPos) > 10f)
        {
            _exploredAreas.Add(playerPos);
            _lastPlayerPosition = playerPos;

            // Limit explored areas list size
            if (_exploredAreas.Count > 100)
            {
                _exploredAreas.RemoveAt(0);
            }
        }
    }

    private bool IsAreaExplored(Vector2 position, float radius)
    {
        return _exploredAreas.Any(explored => 
            Vector2.Distance(explored, position) < radius);
    }    private void MoveTowards(Vector2 targetPos)
    {
        // Prevent too frequent movement
        if (DateTime.Now - _lastMovementTime < TimeSpan.FromMilliseconds(Settings.MovementDelay.Value))
            return;

        // Add random delay to movement to be more human-like
        var randomDelay = _random.Next(100, Settings.ActionRandomization.Value);
        if (DateTime.Now - _lastActionTime < TimeSpan.FromMilliseconds(randomDelay))
            return;

        try
        {
            // Simple direct movement - no complex pathfinding
            var screenPos = GameController.IngameState.Camera.WorldToScreen(
                new Vector3(targetPos.X * GridToWorldMultiplier, targetPos.Y * GridToWorldMultiplier, 0));
            
            ExecuteMovement(screenPos, targetPos);
        }
        catch (Exception ex)
        {
            LogError($"Error moving towards target: {ex.Message}");
        }
    }    private void ExecuteMovement(Vector2 screenPos, Vector2 worldPos)
    {
        var windowRect = GameController.Window.GetWindowRectangle();
        if (screenPos.X >= windowRect.Left && screenPos.X <= windowRect.Right &&
            screenPos.Y >= windowRect.Top && screenPos.Y <= windowRect.Bottom)
        {
            // Human-like movement: set cursor position first, then wait, then click
            Input.SetCursorPos(screenPos);
            
            // Add small random delay before clicking (much faster)
            var clickDelay = 50 + _random.Next(50, 150);
            Task.Delay(clickDelay).ContinueWith(_ => 
            {
                Input.Click(MouseButtons.Left);
            });

            _lastMovementTime = DateTime.Now;
            _lastActionTime = DateTime.Now;

            if (Settings.DebugMode)
            {
                LogMessage($"Moving towards: {worldPos} (screen: {screenPos}, delay: {clickDelay}ms)");
            }
        }
    }

    private bool IsTargetValid(Entity target)
    {
        try
        {
            return target?.IsValid == true && target.IsAlive && target.IsTargetable;
        }
        catch
        {
            return false;
        }
    }

    private Vector2 GetEntityPosition(Entity entity)
    {
        try
        {
            var render = entity.GetComponent<Render>();
            if (render != null)
            {
                return new Vector2(render.Pos.X / GridToWorldMultiplier,
                                   render.Pos.Y / GridToWorldMultiplier);
            }

            var positioned = entity.GetComponent<Positioned>();
            if (positioned != null)
            {
                return new Vector2(positioned.GridX, positioned.GridY);
            }
        }
        catch (Exception ex)
        {
            LogError($"Error getting entity position: {ex.Message}");
        }

        return Vector2.Zero;
    }
}