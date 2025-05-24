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
using ExileCore2.Shared.Attributes;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;
using GameOffsets2.Native;
using Positioned = ExileCore2.PoEMemory.Components.Positioned;

namespace AutoNavigator;

public class AutoNavigatorSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new ToggleNode(true);
    public HotkeyNode StartNavigationHotkey { get; set; } = new HotkeyNode(Keys.F1);
    public HotkeyNode StopNavigationHotkey { get; set; } = new HotkeyNode(Keys.F2);
    public RangeNode<int> MovementSpeed { get; set; } = new RangeNode<int>(100, 10, 500);
    public RangeNode<float> WaypointTolerance { get; set; } = new RangeNode<float>(5.0f, 1.0f, 20.0f);
    public RangeNode<int> ClickDelay { get; set; } = new RangeNode<int>(50, 10, 200);
    public ToggleNode DebugMode { get; set; } = new ToggleNode(false);
    public ToggleNode AutoNavigateToNearestTarget { get; set; } = new ToggleNode(false);
    public ColorNode PathColor { get; set; } = new ColorNode(Color.Cyan);
    public ToggleNode DrawCurrentTarget { get; set; } = new ToggleNode(true);
}

public partial class AutoNavigator : BaseSettingsPlugin<AutoNavigatorSettings>
{
    private const float GridToWorldMultiplier = 250f / 23f;
    private bool _isNavigating = false;
    private List<Vector2i> _currentPath = new List<Vector2i>();
    private int _currentWaypointIndex = 0;
    private CancellationTokenSource _navigationCts = new CancellationTokenSource();
    private Vector2 _targetLocation = Vector2.Zero;
    private bool _hasValidPath = false;
    
    // Integration with Radar plugin
    private Func<Vector2, Action<List<Vector2i>>, CancellationToken, Task> _radarLookForRoute;
    private Func<string, int, Vector2[]> _radarClusterTarget;

    public override bool Initialise()
    {
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
                LogError("Failed to connect to Radar plugin. Make sure Radar plugin is loaded first.");
            }
        }
        catch (Exception ex)
        {
            LogError($"Error connecting to Radar plugin: {ex.Message}");
        }

        return true;
    }

    public override void AreaChange(AreaInstance area)
    {
        // Stop navigation when changing areas
        StopNavigation();
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
                StartNavigationToNearestTarget();
            }
        }

        if (Settings.StopNavigationHotkey.PressedOnce())
        {
            StopNavigation();
        }

        // Update navigation
        if (_isNavigating && _hasValidPath)
        {
            UpdateNavigation();
        }

        // Draw debug information
        if (Settings.DebugMode)
        {
            DrawDebugInfo();
        }

        // Draw current path and target
        if (Settings.DrawCurrentTarget && _hasValidPath)
        {
            DrawCurrentPath();
        }
    }

    private void StartNavigationToNearestTarget()
    {
        if (_radarLookForRoute == null)
        {
            LogError("Radar plugin not available!");
            return;
        }

        var playerPos = GetPlayerPosition();
        if (playerPos == Vector2.Zero)
        {
            LogError("Could not get player position!");
            return;
        }

        // For now, let's navigate to a point near the player for testing
        // In a real implementation, you'd get this from Radar's target system
        var testTarget = playerPos + new Vector2(50, 50);
        
        StartNavigationToTarget(testTarget);
    }

    private async void StartNavigationToTarget(Vector2 target)
    {
        if (_isNavigating)
        {
            StopNavigation();
        }

        _isNavigating = true;
        _targetLocation = target;
        _navigationCts = new CancellationTokenSource();
        
        LogMessage($"Starting navigation to target: {target}");

        try
        {
            // Request path from Radar plugin
            await _radarLookForRoute(target, OnPathReceived, _navigationCts.Token);
        }
        catch (Exception ex)
        {
            LogError($"Error starting navigation: {ex.Message}");
            StopNavigation();
        }
    }

    private void OnPathReceived(List<Vector2i> path)
    {
        if (path == null || path.Count == 0)
        {
            LogError("Received empty path from Radar!");
            _hasValidPath = false;
            return;
        }

        _currentPath = new List<Vector2i>(path);
        _currentWaypointIndex = 0;
        _hasValidPath = true;
        
        LogMessage($"Received path with {path.Count} waypoints");
    }

    private void UpdateNavigation()
    {
        if (!_hasValidPath || _currentPath.Count == 0)
            return;

        var playerPos = GetPlayerPosition();
        if (playerPos == Vector2.Zero)
            return;

        // Check if we've reached the current waypoint
        if (_currentWaypointIndex < _currentPath.Count)
        {
            var currentWaypoint = _currentPath[_currentWaypointIndex];
            var waypointWorldPos = new Vector2(currentWaypoint.X, currentWaypoint.Y);
            var distanceToWaypoint = Vector2.Distance(playerPos, waypointWorldPos);

            if (distanceToWaypoint <= Settings.WaypointTolerance)
            {
                // Move to next waypoint
                _currentWaypointIndex++;
                LogMessage($"Reached waypoint {_currentWaypointIndex}/{_currentPath.Count}");

                if (_currentWaypointIndex >= _currentPath.Count)
                {
                    LogMessage("Navigation completed!");
                    StopNavigation();
                    return;
                }
            }
            else
            {
                // Move towards current waypoint
                MoveTowardsWaypoint(waypointWorldPos);
            }
        }
    }

    private async void MoveTowardsWaypoint(Vector2 waypointPos)
    {
        try
        {
            // Convert grid position to screen position for clicking
            var screenPos = GameController.IngameState.Camera.WorldToScreen(
                new Vector3(waypointPos.X * GridToWorldMultiplier, waypointPos.Y * GridToWorldMultiplier, 0));

            // Ensure the click position is within the game window
            var windowRect = GameController.Window.GetWindowRectangle();
            if (screenPos.X >= windowRect.Left && screenPos.X <= windowRect.Right &&
                screenPos.Y >= windowRect.Top && screenPos.Y <= windowRect.Bottom)
            {
                // Perform mouse click to move
                Input.Click(MouseButtons.Left, screenPos, Settings.ClickDelay);
                
                if (Settings.DebugMode)
                {
                    LogMessage($"Clicking at screen position: {screenPos}");
                }

                // Wait for movement delay
                await Task.Delay(Settings.MovementSpeed, _navigationCts.Token);
            }
        }
        catch (Exception ex)
        {
            LogError($"Error moving towards waypoint: {ex.Message}");
        }
    }

    private void StopNavigation()
    {
        _isNavigating = false;
        _hasValidPath = false;
        _currentPath.Clear();
        _currentWaypointIndex = 0;
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
    }

    private void DrawCurrentPath()
    {
        if (!_hasValidPath || _currentPath.Count == 0)
            return;

        var player = GameController.Game.IngameState.Data.LocalPlayer;
        var playerRender = player?.GetComponent<ExileCore2.PoEMemory.Components.Render>();
        if (playerRender == null)
            return;

        // Draw path lines in world
        for (int i = Math.Max(0, _currentWaypointIndex - 1); i < _currentPath.Count - 1; i++)
        {
            var current = _currentPath[i];
            var next = _currentPath[i + 1];

            var currentWorldPos = new Vector3(current.X * GridToWorldMultiplier, current.Y * GridToWorldMultiplier, 0);
            var nextWorldPos = new Vector3(next.X * GridToWorldMultiplier, next.Y * GridToWorldMultiplier, 0);

            var currentScreenPos = GameController.IngameState.Camera.WorldToScreen(currentWorldPos);
            var nextScreenPos = GameController.IngameState.Camera.WorldToScreen(nextWorldPos);

            // Draw line between waypoints
            Graphics.DrawLine(currentScreenPos, nextScreenPos, 3, Settings.PathColor);
        }

        // Draw current target waypoint
        if (_currentWaypointIndex < _currentPath.Count)
        {
            var targetWaypoint = _currentPath[_currentWaypointIndex];
            var targetWorldPos = new Vector3(targetWaypoint.X * GridToWorldMultiplier, targetWaypoint.Y * GridToWorldMultiplier, 0);
            var targetScreenPos = GameController.IngameState.Camera.WorldToScreen(targetWorldPos);

            // Draw circle around current target
            Graphics.DrawCircle(targetScreenPos, 10, Color.Red, 3);
        }
    }

    private void DrawDebugInfo()
    {
        var startY = 100;
        var lineHeight = 20;
        var currentY = startY;

        Graphics.DrawText($"AutoNavigator Debug Info:", new Vector2(10, currentY), Color.White);
        currentY += lineHeight;

        Graphics.DrawText($"Navigation Active: {_isNavigating}", new Vector2(10, currentY), Color.White);
        currentY += lineHeight;

        Graphics.DrawText($"Has Valid Path: {_hasValidPath}", new Vector2(10, currentY), Color.White);
        currentY += lineHeight;

        Graphics.DrawText($"Current Waypoint: {_currentWaypointIndex}/{_currentPath.Count}", new Vector2(10, currentY), Color.White);
        currentY += lineHeight;

        Graphics.DrawText($"Target Location: {_targetLocation}", new Vector2(10, currentY), Color.White);
        currentY += lineHeight;

        var playerPos = GetPlayerPosition();
        Graphics.DrawText($"Player Position: {playerPos}", new Vector2(10, currentY), Color.White);
        currentY += lineHeight;

        Graphics.DrawText($"Radar Plugin Connected: {_radarLookForRoute != null}", new Vector2(10, currentY), Color.White);
    }

    public override void DrawSettings()
    {
        base.DrawSettings();
        
        if (ImGuiNET.ImGui.Button("Start Navigation Test"))
        {
            StartNavigationToNearestTarget();
        }
        
        ImGuiNET.ImGui.SameLine();
        
        if (ImGuiNET.ImGui.Button("Stop Navigation"))
        {
            StopNavigation();
        }
    }

    public override void OnClose()
    {
        StopNavigation();
        base.OnClose();
    }
}