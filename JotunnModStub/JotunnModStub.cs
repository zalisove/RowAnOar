using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace JotunnModStub
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    //[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class JotunnModStub : BaseUnityPlugin
    {
        public const string PluginGUID = "com.zalisove.rowanoar";
        public const string PluginName = "RowAnOar";
        public const string PluginVersion = "0.0.1";

        // Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        private readonly Harmony harmony = new Harmony(PluginGUID);

        // Configuration options
        private ConfigEntry<float> rowingPowerMultiplier;
        private ConfigEntry<float> minigameSuccessSpeed;
        private ConfigEntry<KeyCode> activateKey;

        // Keep track of ship status for local player
        private Ship lastShip = null;
        private bool wasControllingLastFrame = false;

        private void Awake()
        {
            // Jotunn comes with its own Logger class to provide a consistent Log style for all mods using it
            Jotunn.Logger.LogInfo("RowAnOar has landed");

            // Initialize configuration
            rowingPowerMultiplier = Config.Bind("General", "RowingPowerMultiplier", 1.5f,
                "Multiplier for ship speed when rowing is successful");
            minigameSuccessSpeed = Config.Bind("General", "MinigameSuccessSpeed", 1.0f,
                "How quickly players need to row to boost the ship");
            activateKey = Config.Bind("Controls", "ActivateKey", KeyCode.R,
                "Key to activate the rowing minigame");

            PrefabManager.OnVanillaPrefabsAvailable += AddRowingToShips;

            // Apply patches via Harmony
            harmony.PatchAll();
        }

        private void AddRowingToShips()
        {
            // We need to modify all ship prefabs to add our rowing functionality
            var ships = Resources.FindObjectsOfTypeAll<Ship>();
            foreach (var ship in ships)
            {
                // Add a RowingStation component to the ship
                AddRowingStationToShip(ship.gameObject);
            }

            // Unregister from event to prevent duplicate additions
            PrefabManager.OnVanillaPrefabsAvailable -= AddRowingToShips;
        }

        private void AddRowingStationToShip(GameObject shipObject)
        {
            if (!shipObject.GetComponent<ShipRowingManager>())
            {
                shipObject.AddComponent<ShipRowingManager>();
                Jotunn.Logger.LogInfo($"Added rowing capability to ship: {shipObject.name}");
            }
        }

        // Check if player is controlling the ship
        private bool IsPlayerControllingShip(Player player, Ship ship)
        {
            if (player == null || ship == null) return false;

            // In Valheim, the player controls the ship when they interact with the rudder/steering
            // This is usually determined by checking if the player is near the steering position
            // and has interacted with it

            // This is an approximation, might need to be adjusted based on the game's internals
            return false;
        }

        // Update method that gets called every frame
        private void Update()
        {
            // Check for players on ships
            Player localPlayer = Player.m_localPlayer;
            if (localPlayer != null && !localPlayer.IsDead())
            {
                Ship currentShip = localPlayer.GetStandingOnShip();
                bool isControlling = false;

                if (currentShip != null)
                {
                    isControlling = IsPlayerControllingShip(localPlayer, currentShip);
                }

                // Log when player changes ship status
                if (currentShip != lastShip || isControlling != wasControllingLastFrame)
                {
                    // Player just entered or left a ship, or changed roles
                    if (currentShip != null)
                    {
                        if (isControlling)
                        {
                            Jotunn.Logger.LogInfo("Player is now steering the ship");
                            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "You are steering the ship");
                        }
                        else
                        {
                            Jotunn.Logger.LogInfo("Player is now a passenger on the ship");
                            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "You are a passenger. Press R to help row!");
                        }
                    }
                    else if (lastShip != null)
                    {
                        Jotunn.Logger.LogInfo("Player left the ship");
                    }

                    // Update stored state
                    lastShip = currentShip;
                    wasControllingLastFrame = isControlling;
                }

                                        


                // Check for activate key press when on ship as passenger
                if (Input.GetKeyDown(activateKey.Value) )
                {
                    Jotunn.Logger.LogInfo("Preeeeeesssssss");

                    Ship test = localPlayer.GetStandingOnShip();
                    if (currentShip == null)
                    {
                        Debug.Log("Player is NOT standing on a ship");
                    }
                    else
                    {
                        Debug.Log($"Player is standing on a ship: {currentShip.name}");
                    }
                    if (localPlayer.IsAttachedToShip())
                    {
                        Debug.Log("is attash");
                    }
                    else
                    {
                        Debug.Log("momomomo is attash");
                    }
                    if (currentShip != null)
                    {
                        Jotunn.Logger.LogInfo("SHeeeeepppp");
                        // Player is on a ship and not steering it, start/stop rowing
                        ToggleRowingMinigame(localPlayer, currentShip);
                    }


                }
            }
        }

        private void ToggleRowingMinigame(Player player, Ship ship)
        {
            ShipRowingManager rowingManager = ship.GetComponent<ShipRowingManager>();
            if (rowingManager != null)
            {
                // Check if player is already rowing
                if (rowingManager.IsPlayerRowing(player))
                {
                    rowingManager.StopRowing(player);
                    Jotunn.Logger.LogInfo("Player stopped rowing");
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Stopped rowing");
                }
                else
                {
                    rowingManager.StartRowing(player);
                    Jotunn.Logger.LogInfo("Player started rowing");
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Started rowing! Press SPACE when the markers align!");
                }
            }
        }

        // Ship extension to manage rowing players
        public class ShipRowingManager : MonoBehaviour
        {
            private Ship ship;
            private Dictionary<Player, RowingMinigame> rowingPlayers = new Dictionary<Player, RowingMinigame>();

            private void Awake()
            {
                ship = GetComponent<Ship>();
            }

            public bool IsPlayerRowing(Player player)
            {
                return rowingPlayers.ContainsKey(player);
            }

            public void StartRowing(Player player)
            {
                if (!rowingPlayers.ContainsKey(player))
                {
                    RowingMinigame minigame = new RowingMinigame(player, ship);
                    rowingPlayers.Add(player, minigame);
                }
            }

            public void StopRowing(Player player)
            {
                if (rowingPlayers.ContainsKey(player))
                {
                    rowingPlayers.Remove(player);
                }
            }

            private void Update()
            {
                // Check if any rowing players are no longer on the ship
                List<Player> playersToRemove = new List<Player>();
                JotunnModStub mainPlugin = BepInEx.Bootstrap.Chainloader.PluginInfos[JotunnModStub.PluginGUID].Instance as JotunnModStub;

                foreach (var kv in rowingPlayers)
                {
                    Player player = kv.Key;
                    RowingMinigame minigame = kv.Value;
                    bool isControlling = mainPlugin.IsPlayerControllingShip(player, ship);

                    // If player is no longer on the ship or is dead, stop their rowing
                    if (player.GetStandingOnShip() != ship || player.IsDead() || isControlling)
                    {
                        playersToRemove.Add(player);
                        Jotunn.Logger.LogInfo($"Removing player from rowing (left ship: {player.GetStandingOnShip() != ship}, dead: {player.IsDead()}, steering: {isControlling})");
                        continue;
                    }

                    // Update the minigame and apply forces if successful
                    float rowingForce = minigame.Update();
                    if (rowingForce > 0)
                    {
                        // Apply additional force to the ship in its forward direction
                        ship.m_body.AddForce(ship.transform.forward * rowingForce, ForceMode.Force);

                        // Log successful rowing force
                        if (UnityEngine.Random.value < 0.05f)  // Only log occasionally to avoid spam
                        {
                            Jotunn.Logger.LogInfo($"Applied rowing force: {rowingForce}");
                        }
                    }
                }

                // Remove players who are no longer on the ship
                foreach (Player player in playersToRemove)
                {
                    rowingPlayers.Remove(player);
                }
            }
        }

        // Minigame class that handles the rowing mechanics for a single player
        public class RowingMinigame
        {
            private Player player;
            private Ship ship;

            // Minigame state
            private float targetPosition = 0.5f;
            private float currentPosition = 0f;
            private float direction = 1f;
            private float speed = 1f;
            private float lastUIUpdateTime = 0f;
            private bool isSuccess = false;
            private float successTimer = 0f;
            private int successStreak = 0;

            // Constants
            private const float TARGET_WINDOW = 0.1f;
            private const float MAX_POSITION = 1f;
            private const float MIN_POSITION = 0f;
            private const float SUCCESS_DURATION = 1f;
            private const float UI_UPDATE_INTERVAL = 0.5f;

            public RowingMinigame(Player player, Ship ship)
            {
                this.player = player;
                this.ship = ship;

                // Initialize random starting position for the minigame
                targetPosition = UnityEngine.Random.Range(0.3f, 0.7f);
                currentPosition = 0f;

                // Get configured speed
                var pluginObject = BepInEx.Bootstrap.Chainloader.PluginInfos[JotunnModStub.PluginGUID].Instance;
                var plugin = pluginObject as JotunnModStub;
                speed = plugin.minigameSuccessSpeed.Value;

                Jotunn.Logger.LogInfo($"Initialized rowing minigame with target: {targetPosition:F2}, speed: {speed:F2}");
            }

            // Returns the force to apply to the ship
            public float Update()
            {
                // Move the marker back and forth
                currentPosition += direction * speed * Time.deltaTime;

                // Bounce at the edges
                if (currentPosition >= MAX_POSITION)
                {
                    currentPosition = MAX_POSITION;
                    direction = -1f;
                }
                else if (currentPosition <= MIN_POSITION)
                {
                    currentPosition = MIN_POSITION;
                    direction = 1f;
                }

                // Check for player input
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    // Check if player hit the target
                    float distance = Math.Abs(currentPosition - targetPosition);

                    Jotunn.Logger.LogInfo($"Player pressed SPACE! Current: {currentPosition:F2}, Target: {targetPosition:F2}, Distance: {distance:F2}");

                    if (distance < TARGET_WINDOW)
                    {
                        // Success!
                        isSuccess = true;
                        successTimer = SUCCESS_DURATION;
                        successStreak++;

                        Jotunn.Logger.LogInfo($"Rowing success! Streak: {successStreak}");
                        MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"Good rowing! Streak: {successStreak}");

                        // Move target position for next attempt
                        targetPosition = UnityEngine.Random.Range(0.2f, 0.8f);
                    }
                    else
                    {
                        // Failure
                        isSuccess = false;
                        successStreak = 0;

                        Jotunn.Logger.LogInfo("Rowing failed! Missed the target.");
                        MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Missed! Try again.");
                    }
                }

                // Handle success state
                if (isSuccess)
                {
                    successTimer -= Time.deltaTime;
                    if (successTimer <= 0)
                    {
                        isSuccess = false;
                    }

                    // Calculate force based on multiplier and streak
                    var pluginObject = BepInEx.Bootstrap.Chainloader.PluginInfos[JotunnModStub.PluginGUID].Instance;
                    var plugin = pluginObject as JotunnModStub;
                    float multiplier = plugin.rowingPowerMultiplier.Value;

                    // Return force to apply to ship (with streak bonus)
                    float streakBonus = 1f + (successStreak * 0.1f);
                    return multiplier * 100f * streakBonus;
                }

                // Draw UI for the player
                DrawRowingUI();

                return 0f;
            }

            private void DrawRowingUI()
            {
                // Update UI less frequently to avoid message spam
                if (Time.time - lastUIUpdateTime > UI_UPDATE_INTERVAL)
                {
                    lastUIUpdateTime = Time.time;

                    // Create visual representation of the minigame
                    int barLength = 20;
                    int targetPos = (int)(targetPosition * barLength);
                    int currentPos = (int)(currentPosition * barLength);

                    string progressBar = "|";
                    for (int i = 0; i < barLength; i++)
                    {
                        if (i == targetPos && i == currentPos)
                            progressBar += "X"; // Both target and current
                        else if (i == targetPos)
                            progressBar += "O"; // Target
                        else if (i == currentPos)
                            progressBar += "I"; // Current position
                        else
                            progressBar += "-";
                    }
                    progressBar += "|";

                    MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft,
                        $"Rowing: {progressBar} [SPACE]");
                }
            }
        }
    }
}