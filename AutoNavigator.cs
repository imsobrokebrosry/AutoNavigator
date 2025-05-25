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
using static ExileCore2.Input;

namespace AutoNavigator;

public class AutoNavigatorSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new ToggleNode(true);
    
    // Movement Settings
    public HotkeyNode StartNavigationHotkey { get; set; } = new HotkeyNode(Keys.F1);
    public HotkeyNode StopNavigationHotkey { get; set; } = new HotkeyNode(Keys.F2);
    public ListNode MovementType { get; set; } = new ListNode { Values = new List<string> { "Mouse", "WASD" }, Value = "Mouse" };
    public RangeNode<int> MovementSpeed { get; set; } = new RangeNode<int>(100, 10, 500);
    public RangeNode<float> WaypointTolerance { get; set; } = new RangeNode<float>(15.0f, 5.0f, 30.0f);
    public RangeNode<int> ClickDelay { get; set; } = new RangeNode<int>(100, 50, 300);
    public RangeNode<int> StepSize { get; set; } = new RangeNode<int>(32, 20, 50);
    
    // Pathfinding Settings
    public ToggleNode UseAdvancedPathfinding { get; set; } = new ToggleNode(true);
    public RangeNode<int> PathCropRadius { get; set; } = new RangeNode<int>(40, 20, 80);
    public RangeNode<int> MaxPathLength { get; set; } = new RangeNode<int>(150, 50, 300);
    public RangeNode<int> ExtraPointsCount { get; set; } = new RangeNode<int>(6, 3, 12);
    
    // Exploration Settings - Python bot style
    public RangeNode<float> DiscoveryPercent { get; set; } = new RangeNode<float>(0.93f, 0.7f, 1.0f);
    public RangeNode<float> ExplorationRadius { get; set; } = new RangeNode<float>(80.0f, 40.0f, 120.0f);
    public RangeNode<int> TSPPointCount { get; set; } = new RangeNode<int>(20, 10, 40);
    public RangeNode<int> ExplorationUpdateInterval { get; set; } = new RangeNode<int>(2000, 1000, 5000);
    
    // Combat Settings - High priority system like Python
    public RangeNode<float> MonsterDetectionRadius { get; set; } = new RangeNode<float>(50.0f, 20.0f, 100.0f);
    public RangeNode<float> AttackRange { get; set; } = new RangeNode<float>(20.0f, 10.0f, 40.0f);
    public RangeNode<int> AttackDelay { get; set; } = new RangeNode<int>(250, 100, 500);
    public ToggleNode ForceKillRares { get; set; } = new ToggleNode(true);
    public ToggleNode ForceKillBlues { get; set; } = new ToggleNode(true);
    
    // Map Completion Settings
    public RangeNode<int> MaxMapRunTime { get; set; } = new RangeNode<int>(600, 300, 1200); // seconds
    public RangeNode<int> MovementDelay { get; set; } = new RangeNode<int>(200, 100, 500);
    public RangeNode<int> ActionRandomization { get; set; } = new RangeNode<int>(150, 50, 300);
    
    // Obstacle Handling
    public ToggleNode AutoOpenDoors { get; set; } = new ToggleNode(true);
    public ToggleNode AutoUseTransitions { get; set; } = new ToggleNode(false);
    public RangeNode<float> DoorDetectionRadius { get; set; } = new RangeNode<float>(20.0f, 10.0f, 40.0f);
    public RangeNode<float> TransitionDetectionRadius { get; set; } = new RangeNode<float>(25.0f, 10.0f, 50.0f);
    
    // Stuck Detection
    public ToggleNode EnableStuckDetection { get; set; } = new ToggleNode(true);
    public RangeNode<int> StuckDetectionThreshold { get; set; } = new RangeNode<int>(8, 3, 15);
    public RangeNode<int> StuckRecoveryDistance { get; set; } = new RangeNode<int>(20, 10, 40);
    public RangeNode<int> TimeoutSeconds { get; set; } = new RangeNode<int>(300, 60, 600);
    
    // Visual Settings
    public ToggleNode DebugMode { get; set; } = new ToggleNode(false);
    public ToggleNode DrawCurrentTarget { get; set; } = new ToggleNode(true);
    public ToggleNode DrawExplorationPoints { get; set; } = new ToggleNode(true);
    public ToggleNode DrawFullPath { get; set; } = new ToggleNode(false);
    public ColorNode PathColor { get; set; } = new ColorNode(Color.Cyan);
    public ColorNode WaypointColor { get; set; } = new ColorNode(Color.Red);
    public ColorNode ExplorationColor { get; set; } = new ColorNode(Color.Yellow);
    public ColorNode DebugTextColor { get; set; } = new ColorNode(Color.White);
    
    // Advanced Features
    public ToggleNode OptimizePathWithTSP { get; set; } = new ToggleNode(true);
    public ToggleNode UseTerrainWeights { get; set; } = new ToggleNode(true);
    
    // Future Implementation Placeholders
    [Menu("Future Features", "Features to be implemented")]
    public EmptyNode FutureFeaturesHeader { get; set; } = new EmptyNode();
    
    [Menu("Map Preparation", "Waystone upgrading, anointing etc.")]
    public ToggleNode MapPreparation { get; set; } = new ToggleNode(false); // Disabled for now
    
    [Menu("Target Prioritization", "Delirium, essences, rituals, breaches")]
    public ToggleNode TargetPrioritization { get; set; } = new ToggleNode(false); // Disabled for now
    
    [Menu("Stash Management", "Auto stashing and item management")]
    public ToggleNode StashManagement { get; set; } = new ToggleNode(false); // Disabled for now
    
    [Menu("Boss Fight Logic", "Advanced boss encounter handling")]
    public ToggleNode BossFightLogic { get; set; } = new ToggleNode(false); // Disabled for now
}

public partial class AutoNavigator : BaseSettingsPlugin<AutoNavigatorSettings>
{
    public const float GridToWorldMultiplier = 250f / 23f;
    
    // Navigation State - Python bot style
    private bool _isNavigating = false;
    private bool _isExploring = false;
    private bool _mapCompleted = false;
    private Entity _currentTarget = null;
    private DateTime _startTime = DateTime.MinValue;
    private DateTime _lastTargetScanTime = DateTime.MinValue;
    private DateTime _lastExplorationUpdate = DateTime.MinValue;
    private DateTime _lastActionTime = DateTime.MinValue;
    private DateTime _lastMovementTime = DateTime.MinValue;
    private DateTime _lastAttackTime = DateTime.MinValue;
    private CancellationTokenSource _navigationCts = new CancellationTokenSource();
    
    // Exploration System - TSP based like Python
    private List<Vector2> _discoveryPoints = new List<Vector2>();
    private List<Vector2> _exploredAreas = new List<Vector2>();
    private Vector2 _currentExplorationTarget = Vector2.Zero;
    private int _currentDiscoveryIndex = 0;
    private Random _random = new Random();
    
    // Combat State
    private List<Entity> _nearbyMonsters = new List<Entity>();
    private Vector2 _lastPlayerPosition = Vector2.Zero;
    
    // Pathfinding
    private PathfindingEngine _pathfinder;
    private ObstacleHandler _obstacleHandler;
    private StuckDetector _stuckDetector;
    
    // Integration with other plugins
    private Func<Vector2, Action<List<Vector2i>>, CancellationToken, Task> _radarLookForRoute;
    private Func<string, int, Vector2[]> _radarClusterTarget;    public override bool Initialise()
    {
        try
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
        catch (Exception ex)
        {
            LogError($"Failed to initialize AutoNavigator: {ex.Message}");
            return false;
        }
    }

    public override void AreaChange(AreaInstance area)
    {
        // Stop navigation when changing areas
        StopNavigation();
        _pathfinder?.ClearCache();
        _obstacleHandler?.Reset();
    }

    public override void Render()
    {
        if (!Settings.Enable)
            return;

        // Handle hotkeys
        if (Settings.StartNavigationHotkey.PressedOnce())
        {
            if (!_isNavigating)
            {
                StartMapExploration();
            }
        }

        if (Settings.StopNavigationHotkey.PressedOnce())
        {
            StopNavigation();
        }

        // Main exploration and combat loop - Python bot style priority system
        if (_isNavigating)
        {
            UpdateMapperRoutine();
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
        
        // Draw exploration points
        if (Settings.DrawExplorationPoints && _discoveryPoints.Count > 0)
        {
            DrawExplorationPoints();
        }
    }

    private void StartMapExploration()
    {
        _isNavigating = true;
        _isExploring = true;
        _mapCompleted = false;
        _startTime = DateTime.Now;
        _navigationCts = new CancellationTokenSource();
        
        // Clear previous exploration data
        _discoveryPoints.Clear();
        _exploredAreas.Clear();
        _currentDiscoveryIndex = 0;
        
        // Generate initial discovery points using TSP
        GenerateDiscoveryPoints();
        
        LogMessage("Starting map exploration with TSP pathfinding - Python bot style!");
    }

    private void StopNavigation()
    {
        _isNavigating = false;
        _isExploring = false;
        _mapCompleted = false;
        _currentTarget = null;
        _currentExplorationTarget = Vector2.Zero;
        _navigationCts?.Cancel();
        _navigationCts = new CancellationTokenSource();
        
        LogMessage("Navigation stopped");
    }

    // Main exploration routine - Python bot style priority system
    private void UpdateMapperRoutine()
    {
        var playerPos = GetPlayerPosition();
        if (playerPos == Vector2.Zero)
            return;

        // Check if map run time exceeded
        if ((DateTime.Now - _startTime).TotalSeconds > Settings.MaxMapRunTime.Value)
        {
            LogMessage("Map run time exceeded, stopping exploration");
            StopNavigation();
            return;
        }

        // Human-like action throttling
        if (DateTime.Now - _lastActionTime < TimeSpan.FromMilliseconds(Settings.MovementDelay.Value))
            return;

        // Update explored areas
        UpdateExploredAreas(playerPos);

        // Python bot priority system:
        // Priority 1: Combat - Scan for monsters frequently
        if (DateTime.Now - _lastTargetScanTime > TimeSpan.FromMilliseconds(400))
        {
            ScanForMonstersWithPriority(playerPos);
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
                // Move towards target
                MoveTowards(targetPos);
                return;
            }
            else
            {
                // Target too far, clear it
                _currentTarget = null;
            }
        }        // Priority 3: Handle obstacles (doors, transitions)
        if (Settings.AutoOpenDoors && _obstacleHandler != null && _obstacleHandler.CheckAndOpenDoors(playerPos))
        {
            return;
        }

        if (Settings.AutoUseTransitions && _obstacleHandler != null && _obstacleHandler.CheckAndUseTransitions(playerPos))
        {
            return;
        }

        // Priority 4: Exploration - TSP based discovery
        if (DateTime.Now - _lastExplorationUpdate > TimeSpan.FromMilliseconds(Settings.ExplorationUpdateInterval.Value))
        {
            UpdateExplorationTarget(playerPos);
            _lastExplorationUpdate = DateTime.Now;
        }

        // Move towards exploration target
        if (_currentExplorationTarget != Vector2.Zero)
        {
            var distanceToExploration = Vector2.Distance(playerPos, _currentExplorationTarget);
            if (distanceToExploration > Settings.WaypointTolerance.Value)
            {
                MoveTowards(_currentExplorationTarget);
            }
            else
            {
                // Reached exploration target, move to next discovery point
                MoveToNextDiscoveryPoint(playerPos);
            }
        }
        else
        {
            // No exploration target, generate new discovery points or check completion
            if (IsMapCompleted(playerPos))
            {
                OnMapFinished();
            }
            else
            {
                GenerateDiscoveryPoints();
                if (_discoveryPoints.Count > 0)
                {
                    _currentExplorationTarget = _discoveryPoints[0];
                }
            }
        }
    }

    // TSP-based discovery point generation - Python bot style
    private void GenerateDiscoveryPoints()
    {
        var playerPos = GetPlayerPosition();
        if (playerPos == Vector2.Zero) return;

        _discoveryPoints.Clear();
        var tempPoints = new List<Vector2>();

        // Generate points in a grid pattern around unexplored areas
        var radius = Settings.ExplorationRadius.Value;
        var pointCount = Settings.TSPPointCount.Value;
        
        // Create exploration points in 8 directions
        for (int angle = 0; angle < 360; angle += 45)
        {
            var radians = angle * Math.PI / 180;
            var direction = new Vector2((float)Math.Cos(radians), (float)Math.Sin(radians));
            
            // Multiple distances in each direction
            for (int distance = 30; distance <= radius; distance += 20)
            {
                var point = playerPos + direction * distance;
                
                // Only add if not already explored
                if (!IsAreaExplored(point, 25f))
                {
                    tempPoints.Add(point);
                }
            }
        }

        // If we have points, optimize with TSP
        if (tempPoints.Count > 0)
        {
            // Limit to max point count for performance
            if (tempPoints.Count > pointCount)
            {
                // Sort by distance and take closest ones
                tempPoints = tempPoints
                    .OrderBy(p => Vector2.Distance(playerPos, p))
                    .Take(pointCount)
                    .ToList();
            }

            // Apply TSP optimization if enabled
            if (Settings.OptimizePathWithTSP && tempPoints.Count > 3)
            {
                _discoveryPoints = OptimizePointsWithTSP(tempPoints, playerPos);
            }
            else
            {
                // Simple nearest neighbor ordering
                _discoveryPoints = OrderPointsByDistance(tempPoints, playerPos);
            }

            _currentDiscoveryIndex = 0;
            
            if (Settings.DebugMode)
            {
                LogMessage($"Generated {_discoveryPoints.Count} discovery points");
            }
        }
    }

    // TSP optimization for discovery points
    private List<Vector2> OptimizePointsWithTSP(List<Vector2> points, Vector2 startPoint)
    {
        if (points.Count <= 2) return points;

        var optimized = new List<Vector2>();
        var remaining = new List<Vector2>(points);
        var current = startPoint;

        // Find nearest starting point
        var nearest = remaining.OrderBy(p => Vector2.Distance(current, p)).First();
        optimized.Add(nearest);
        remaining.Remove(nearest);
        current = nearest;

        // Continue with nearest neighbor
        while (remaining.Count > 0)
        {
            nearest = remaining.OrderBy(p => Vector2.Distance(current, p)).First();
            optimized.Add(nearest);
            remaining.Remove(nearest);
            current = nearest;
        }

        return optimized;
    }

    private List<Vector2> OrderPointsByDistance(List<Vector2> points, Vector2 startPoint)
    {
        return points.OrderBy(p => Vector2.Distance(startPoint, p)).ToList();
    }

    private void UpdateExplorationTarget(Vector2 playerPos)
    {
        if (_discoveryPoints.Count == 0)
        {
            GenerateDiscoveryPoints();
            return;
        }

        if (_currentDiscoveryIndex < _discoveryPoints.Count)
        {
            _currentExplorationTarget = _discoveryPoints[_currentDiscoveryIndex];
        }
        else
        {
            // Finished current discovery points, generate new ones
            GenerateDiscoveryPoints();
            if (_discoveryPoints.Count > 0)
            {
                _currentExplorationTarget = _discoveryPoints[0];
            }
        }
    }

    private void MoveToNextDiscoveryPoint(Vector2 playerPos)
    {
        _currentDiscoveryIndex++;
        if (_currentDiscoveryIndex < _discoveryPoints.Count)
        {
            _currentExplorationTarget = _discoveryPoints[_currentDiscoveryIndex];
            if (Settings.DebugMode)
            {
                LogMessage($"Moving to discovery point {_currentDiscoveryIndex}/{_discoveryPoints.Count}: {_currentExplorationTarget}");
            }
        }
        else
        {
            _currentExplorationTarget = Vector2.Zero;
        }
    }    // Python bot style monster scanning with priority system
    private void ScanForMonstersWithPriority(Vector2 playerPos)
    {
        _nearbyMonsters.Clear();
        
        try
        {
            // Use Radar if available, otherwise fallback to manual scanning
            if (_radarClusterTarget != null)
            {
                try
                {
                    var radarMonsters = _radarClusterTarget("monsters", (int)Settings.MonsterDetectionRadius.Value);
                    if (radarMonsters != null && radarMonsters.Length > 0)
                    {
                        var allEntities = GameController?.EntityListWrapper?.Entities;
                        if (allEntities != null)
                        {
                            foreach (var radarPos in radarMonsters)
                            {
                                var nearbyEntity = allEntities
                                    .Where(e => IsValidMonster(e))
                                    .Where(e => Vector2.Distance(GetEntityPosition(e), radarPos) < 15f)
                                    .FirstOrDefault();
                                
                                if (nearbyEntity != null)
                                {
                                    _nearbyMonsters.Add(nearbyEntity);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Error using Radar for monster detection: {ex.Message}");
                }
            }
            
            // Fallback: Manual entity scanning
            if (_nearbyMonsters.Count == 0)
            {
                var allEntities = GameController?.EntityListWrapper?.Entities;
                if (allEntities != null)
                {
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
            }

            // Prioritize monsters - Python bot style priority system
            if (_nearbyMonsters.Count > 0)
            {
                _currentTarget = _nearbyMonsters
                    .Where(e => ShouldTargetMonster(e))
                    .OrderByDescending(e => GetMonsterPriority(e)) // Highest priority first
                    .ThenBy(e => Vector2.Distance(playerPos, GetEntityPosition(e))) // Then by distance
                    .FirstOrDefault();

                if (_currentTarget != null && Settings.DebugMode)
                {
                    LogMessage($"Target acquired: {_currentTarget.Path} (Priority: {GetMonsterPriority(_currentTarget)}) at distance {Vector2.Distance(playerPos, GetEntityPosition(_currentTarget)):F1}");
                }
            }
            else if (_currentTarget != null)
            {
                _currentTarget = null;
            }
        }
        catch (Exception ex)
        {
            LogError($"Error scanning for monsters: {ex.Message}");
        }
    }

    // Python bot style monster priority system
    private int GetMonsterPriority(Entity entity)
    {
        try
        {
            var path = entity.Path?.ToLower() ?? "";
            var renderName = entity.RenderName?.ToLower() ?? "";
            
            // Highest priority - Unique/Boss monsters
            if (path.Contains("unique") || path.Contains("boss") || renderName.Contains("boss"))
                return 100;
            
            // High priority - Rare monsters (force_kill_rares equivalent)
            if (Settings.ForceKillRares && (path.Contains("rare") || entity.IsHostile))
                return 80;
            
            // Medium priority - Magic monsters (force_kill_blues equivalent) 
            if (Settings.ForceKillBlues && (path.Contains("magic") || path.Contains("champion")))
                return 60;
            
            // TODO: Future implementation - Essence monsters, Breach monsters, etc.
            // if (path.Contains("essence")) return 70;
            // if (path.Contains("breach")) return 65;
            
            // Regular monsters
            return 20;
        }
        catch
        {
            return 10;
        }
    }

    private bool ShouldTargetMonster(Entity entity)
    {
        try
        {
            var priority = GetMonsterPriority(entity);
            
            // Always target high priority monsters
            if (priority >= 80) return true;
            
            // Target medium priority if force kill settings are enabled
            if (priority >= 60 && Settings.ForceKillBlues) return true;
            if (priority >= 20 && Settings.ForceKillRares) return true;
            
            return false;
        }
        catch
        {
            return false;
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
                path.Contains("decoration") || path.Contains("effect") ||
                path.Contains("portal") || path.Contains("waypoint"))
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void AttackTarget(Entity target)
    {
        // Rate limiting for attacks
        if (DateTime.Now - _lastAttackTime < TimeSpan.FromMilliseconds(Settings.AttackDelay.Value))
            return;

        // Human-like random delay
        var randomDelay = _random.Next(50, Settings.ActionRandomization.Value);
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
                // Attack with left click
                Input.SetCursorPos(screenPos);
                
                // Human-like delay before click
                Task.Delay(30 + _random.Next(30)).ContinueWith(_ => 
                {
                    Input.Click(MouseButtons.Left);
                });
                
                _lastAttackTime = DateTime.Now;
                _lastActionTime = DateTime.Now;

                if (Settings.DebugMode)
                {
                    LogMessage($"Attacking {target.RenderName ?? "Monster"} at {screenPos}");
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Error attacking target: {ex.Message}");
        }
    }

    private void MoveTowards(Vector2 targetPos)
    {
        // Rate limiting for movement
        if (DateTime.Now - _lastMovementTime < TimeSpan.FromMilliseconds(Settings.MovementDelay.Value))
            return;

        // Human-like random delay
        var randomDelay = _random.Next(50, Settings.ActionRandomization.Value);
        if (DateTime.Now - _lastActionTime < TimeSpan.FromMilliseconds(randomDelay))
            return;

        try
        {
            var screenPos = GameController.IngameState.Camera.WorldToScreen(
                new Vector3(targetPos.X * GridToWorldMultiplier, targetPos.Y * GridToWorldMultiplier, 0));
            
            ExecuteMovement(screenPos, targetPos);
        }
        catch (Exception ex)
        {
            LogError($"Error moving towards target: {ex.Message}");
        }
    }

    private void ExecuteMovement(Vector2 screenPos, Vector2 worldPos)
    {
        var windowRect = GameController.Window.GetWindowRectangle();
        if (screenPos.X >= windowRect.Left && screenPos.X <= windowRect.Right &&
            screenPos.Y >= windowRect.Top && screenPos.Y <= windowRect.Bottom)
        {
            // Human-like movement
            Input.SetCursorPos(screenPos);
            
            var clickDelay = Settings.ClickDelay.Value + _random.Next(50);
            Task.Delay(clickDelay).ContinueWith(_ => 
            {
                Input.Click(MouseButtons.Left);
            });

            _lastMovementTime = DateTime.Now;
            _lastActionTime = DateTime.Now;

            if (Settings.DebugMode)
            {
                LogMessage($"Moving towards: {worldPos} (screen: {screenPos})");
            }
        }
    }

    private void UpdateExploredAreas(Vector2 playerPos)
    {
        // Add current position to explored areas
        if (_exploredAreas.Count == 0 || 
            Vector2.Distance(_lastPlayerPosition, playerPos) > 15f)
        {
            _exploredAreas.Add(playerPos);
            _lastPlayerPosition = playerPos;

            // Limit explored areas list size for performance
            if (_exploredAreas.Count > 200)
            {
                _exploredAreas.RemoveRange(0, 50); // Remove oldest 50 entries
            }
        }
    }

    private bool IsAreaExplored(Vector2 position, float radius)
    {
        return _exploredAreas.Any(explored => 
            Vector2.Distance(explored, position) < radius);
    }

    // Map completion check - Python bot style
    private bool IsMapCompleted(Vector2 playerPos)
    {
        // Check exploration percentage
        var explorationPercent = CalculateExplorationPercent();
        if (explorationPercent >= Settings.DiscoveryPercent.Value)
        {
            if (Settings.DebugMode)
            {
                LogMessage($"Map exploration completed: {explorationPercent:P1} >= {Settings.DiscoveryPercent.Value:P1}");
            }
            return true;
        }

        // TODO: Future implementation - check for specific completion criteria
        // - No more discovery points available
        // - All high priority targets cleared
        // - Ritual completion check
        // - Boss killed check

        return false;
    }

    private float CalculateExplorationPercent()
    {
        // Simple estimation based on explored areas vs total area
        // In a real implementation, this would use terrain data
        var totalEstimatedArea = Math.PI * Math.Pow(Settings.ExplorationRadius.Value, 2);
        var exploredArea = _exploredAreas.Count * Math.PI * Math.Pow(25f, 2); // 25f radius per explored point
        
        return Math.Min(1.0f, (float)(exploredArea / totalEstimatedArea));
    }

    private void OnMapFinished()
    {
        _mapCompleted = true;
        LogMessage($"Map exploration completed! Explored {_exploredAreas.Count} areas in {(DateTime.Now - _startTime).TotalMinutes:F1} minutes");
        
        // TODO: Future implementation
        // - Open portal and return to hideout
        // - Stash management
        // - Map preparation for next run
        
        StopNavigation();
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
    }    private Vector2 GetEntityPosition(Entity entity)
    {
        try
        {
            if (entity?.IsValid != true)
                return Vector2.Zero;

            var render = entity.GetComponent<Render>();
            if (render != null && render.Pos != null)
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
            if (Settings.DebugMode)
                LogError($"Error getting entity position: {ex.Message}");
        }

        return Vector2.Zero;
    }

    // Drawing methods
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
            Graphics.DrawCircle(screenPos, 25, Color.Red, 3);
            
            // Draw target priority and name
            var priority = GetMonsterPriority(_currentTarget);
            Graphics.DrawText($"Target: {_currentTarget.RenderName ?? "Monster"} (P:{priority})", 
                new Vector2(screenPos.X + 30, screenPos.Y), Color.Red);
        }
        catch (Exception ex)
        {
            LogError($"Error drawing current target: {ex.Message}");
        }
    }    private void DrawExplorationPoints()
    {
        try
        {
            for (int i = 0; i < _discoveryPoints.Count; i++)
            {
                var point = _discoveryPoints[i];
                var screenPos = GameController.IngameState.Camera.WorldToScreen(
                    new Vector3(point.X * GridToWorldMultiplier, point.Y * GridToWorldMultiplier, 0));

                var color = i == _currentDiscoveryIndex ? Color.Yellow : Settings.ExplorationColor.Value;
                Graphics.DrawCircle(screenPos, 10, color, 2);
                
                if (i == _currentDiscoveryIndex)
                {
                    Graphics.DrawText($"Target: {i + 1}/{_discoveryPoints.Count}", 
                        new Vector2(screenPos.X + 15, screenPos.Y), Color.Yellow);
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Error drawing exploration points: {ex.Message}");
        }
    }    private void DrawDebugInfo()
    {
        try
        {
            var startY = 100;
            var lineHeight = 18;
            var currentY = startY;

            Graphics.DrawText("AutoNavigator - Python Bot Style", new Vector2(10, currentY), Settings.DebugTextColor.Value);
            currentY += lineHeight;

            Graphics.DrawText($"Navigation: {_isNavigating} | Exploring: {_isExploring}", new Vector2(10, currentY), Settings.DebugTextColor.Value);
            currentY += lineHeight;

            Graphics.DrawText($"Current Target: {(_currentTarget?.RenderName ?? "None")}", new Vector2(10, currentY), Settings.DebugTextColor.Value);
            currentY += lineHeight;

            Graphics.DrawText($"Nearby Monsters: {_nearbyMonsters.Count}", new Vector2(10, currentY), Settings.DebugTextColor.Value);
            currentY += lineHeight;

            Graphics.DrawText($"Discovery Points: {_currentDiscoveryIndex}/{_discoveryPoints.Count}", new Vector2(10, currentY), Settings.DebugTextColor.Value);
            currentY += lineHeight;

            Graphics.DrawText($"Explored Areas: {_exploredAreas.Count}", new Vector2(10, currentY), Settings.DebugTextColor.Value);
            currentY += lineHeight;

            var explorationPercent = CalculateExplorationPercent();
            Graphics.DrawText($"Exploration: {explorationPercent:P1} / {Settings.DiscoveryPercent.Value:P1}", new Vector2(10, currentY), Settings.DebugTextColor.Value);
            currentY += lineHeight;

            var playerPos = GetPlayerPosition();
            Graphics.DrawText($"Player Position: {playerPos}", new Vector2(10, currentY), Settings.DebugTextColor.Value);
            currentY += lineHeight;

            if (_currentTarget != null)
            {
                var targetPos = GetEntityPosition(_currentTarget);
                var distance = Vector2.Distance(playerPos, targetPos);
                var priority = GetMonsterPriority(_currentTarget);
                Graphics.DrawText($"Target Distance: {distance:F1} | Priority: {priority}", new Vector2(10, currentY), Settings.DebugTextColor.Value);
                currentY += lineHeight;
            }

            var runTime = (DateTime.Now - _startTime).TotalMinutes;
            Graphics.DrawText($"Run Time: {runTime:F1}m / {Settings.MaxMapRunTime.Value / 60:F1}m", new Vector2(10, currentY), Settings.DebugTextColor.Value);
        }
        catch (Exception ex)
        {
            LogError($"Error drawing debug info: {ex.Message}");
        }
    }

    public override void DrawSettings()
    {
        base.DrawSettings();
        
        if (ImGuiNET.ImGui.Button("Start Map Exploration"))
        {
            StartMapExploration();
        }
        
        ImGuiNET.ImGui.SameLine();
        
        if (ImGuiNET.ImGui.Button("Stop Navigation"))
        {
            StopNavigation();
        }
        
        ImGuiNET.ImGui.Separator();
        
        if (ImGuiNET.ImGui.CollapsingHeader("Current Status"))
        {
            ImGuiNET.ImGui.Text($"Navigation Active: {_isNavigating}");
            ImGuiNET.ImGui.Text($"Map Completed: {_mapCompleted}");
            ImGuiNET.ImGui.Text($"Current Target: {(_currentTarget?.RenderName ?? "None")}");
            ImGuiNET.ImGui.Text($"Nearby Monsters: {_nearbyMonsters.Count}");
            ImGuiNET.ImGui.Text($"Discovery Points: {_currentDiscoveryIndex}/{_discoveryPoints.Count}");
            ImGuiNET.ImGui.Text($"Explored Areas: {_exploredAreas.Count}");
            ImGuiNET.ImGui.Text($"Exploration: {CalculateExplorationPercent():P1}");
            
            if (_startTime != DateTime.MinValue)
            {
                var runTime = (DateTime.Now - _startTime).TotalMinutes;
                ImGuiNET.ImGui.Text($"Run Time: {runTime:F1} minutes");
            }
        }
        
        if (ImGuiNET.ImGui.CollapsingHeader("Performance Stats"))
        {
            var timeSinceLastAction = (DateTime.Now - _lastActionTime).TotalMilliseconds;
            ImGuiNET.ImGui.Text($"Last Action: {timeSinceLastAction:F0}ms ago");
            
            var timeSinceLastMovement = (DateTime.Now - _lastMovementTime).TotalMilliseconds;
            ImGuiNET.ImGui.Text($"Last Movement: {timeSinceLastMovement:F0}ms ago");
            
            var timeSinceLastAttack = (DateTime.Now - _lastAttackTime).TotalMilliseconds;
            ImGuiNET.ImGui.Text($"Last Attack: {timeSinceLastAttack:F0}ms ago");
        }
    }    public override void OnClose()
    {
        StopNavigation();
        base.OnClose();
    }
}