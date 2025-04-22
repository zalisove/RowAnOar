using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
        private ConfigEntry<KeyCode> rowingKey;
        private ConfigEntry<float> shipDetectionRadius;

        // Keep track of ship status for local player
        private Ship lastShip = null;
        private bool wasControllingLastFrame = false;
        private Vector3 lastPosition = Vector3.zero;
        private float lastShipCheckTime = 0f;
        private const float SHIP_CHECK_INTERVAL = 0.5f;

        // UI Components
        private GameObject rowingUI;
        private RectTransform progressBarBg;
        private RectTransform progressBarCurrent;
        private RectTransform progressBarTarget;
        private Text instructionText;
        private Text streakText;

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
            rowingKey = Config.Bind("Controls", "RowingKey", KeyCode.Space,
                "Key to press during the rowing minigame");
            shipDetectionRadius = Config.Bind("General", "ShipDetectionRadius", 5f,
                "Radius to check if player is still on the ship when interacting with objects");

            // Register event callbacks
            PrefabManager.OnVanillaPrefabsAvailable += AddRowingToShips;

            // Initialize UI
            GUIManager.OnCustomGUIAvailable += SetupRowingUI;

            // Apply patches via Harmony
            harmony.PatchAll();
        }

        private void SetupRowingUI()
        {
            // Create the main UI container
            rowingUI = GUIManager.Instance.CreateWoodpanel(
                parent: GUIManager.CustomGUIFront.transform,
                anchorMin: new Vector2(0.5f, 0.1f),
                anchorMax: new Vector2(0.5f, 0.1f),
                position: new Vector2(0f, 0f),
                width: 400f,
                height: 120f,
                draggable: true
            );
            rowingUI.name = "RowingMinigameUI";
            rowingUI.SetActive(false);

            // Create title
            GameObject titleGO = GUIManager.Instance.CreateText(
                text: "Rowing Minigame",
                parent: rowingUI.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position: new Vector2(75f, 0f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 18,
                color: Color.white,
                outline: true,
                outlineColor: Color.black,
                width: 300f,
                height: 30f,
                addContentSizeFitter: false
            );

            // Create progress bar background (need to create a panel differently since CreatePanel isn't a method)
            GameObject progressBarBgGO = new GameObject("ProgressBarBg", typeof(RectTransform), typeof(Image));
            progressBarBgGO.transform.SetParent(rowingUI.transform, false);

            progressBarBg = progressBarBgGO.GetComponent<RectTransform>();
            progressBarBg.anchorMin = new Vector2(0.5f, 0.5f);
            progressBarBg.anchorMax = new Vector2(0.5f, 0.5f);
            progressBarBg.sizeDelta = new Vector2(300f, 30f);
            progressBarBg.anchoredPosition = new Vector2(0f, 0f);

            Image progressBarBgImage = progressBarBgGO.GetComponent<Image>();
            progressBarBgImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

            // Create progress bar for current position
            GameObject currentMarkerGO = new GameObject("CurrentMarker", typeof(RectTransform), typeof(Image));
            currentMarkerGO.transform.SetParent(progressBarBg, false);

            progressBarCurrent = currentMarkerGO.GetComponent<RectTransform>();
            progressBarCurrent.anchorMin = new Vector2(0f, 0f);
            progressBarCurrent.anchorMax = new Vector2(0f, 1f);
            progressBarCurrent.sizeDelta = new Vector2(10f, 0f);
            progressBarCurrent.anchoredPosition = new Vector2(150f, 0f);

            currentMarkerGO.GetComponent<Image>().color = Color.yellow;

            // Create progress bar for target position
            GameObject targetMarkerGO = new GameObject("TargetMarker", typeof(RectTransform), typeof(Image));
            targetMarkerGO.transform.SetParent(progressBarBg, false);

            progressBarTarget = targetMarkerGO.GetComponent<RectTransform>();
            progressBarTarget.anchorMin = new Vector2(0f, 0f);
            progressBarTarget.anchorMax = new Vector2(0f, 1f);
            progressBarTarget.sizeDelta = new Vector2(20f, 0f);
            progressBarTarget.anchoredPosition = new Vector2(75f, 0f);

            targetMarkerGO.GetComponent<Image>().color = new Color(0f, 1f, 0f, 0.5f);

            // Create instruction text
            GameObject instructionTextGO = GUIManager.Instance.CreateText(
                text: $"Press {rowingKey.Value} when markers align!",
                parent: rowingUI.transform,
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                position: new Vector2(75f, 30f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 14,
                color: Color.white,
                outline: true,
                outlineColor: Color.black,
                width: 350f,
                height: 30f,
                addContentSizeFitter: false
            );
            instructionText = instructionTextGO.GetComponent<Text>();

            // Create streak text
            GameObject streakTextGO = GUIManager.Instance.CreateText(
                text: "Streak: 0",
                parent: rowingUI.transform,
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                position: new Vector2(150f, 10f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 12,
                color: Color.white,
                outline: true,
                outlineColor: Color.black,
                width: 200f,
                height: 20f,
                addContentSizeFitter: false
            );
            streakText = streakTextGO.GetComponent<Text>();
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
            return false;
        }

        // Improved ship detection method
        private Ship GetPlayerShip(Player player)
        {
            if (player == null) return null;

            // First try standard method
            Ship directShip = player.GetStandingOnShip();
            
            if (directShip != null)
            {
                return directShip;
            }

            // If standard method fails but we recently had a ship, check if player is still near it
            if (lastShip != null && Time.time - lastShipCheckTime < SHIP_CHECK_INTERVAL)
            {
                // Skip additional checks if we've checked recently
                return lastShip;
            }

            lastShipCheckTime = Time.time;

            // Try to find nearby ships if player is interacting with something on the ship
            float checkRadius = shipDetectionRadius.Value;

            // Check if player is still near previous ship
            if (lastShip != null)
            {
                float distToShip = Vector3.Distance(player.transform.position, lastShip.transform.position);
                if (distToShip < checkRadius)
                {
                    return lastShip;
                }
            }

            // Last resort: look for any ship in vicinity 
            // Get all ships in the scene
            var allShips = GameObject.FindObjectsOfType<Ship>();
            Ship closestShip = null;
            float closestDist = checkRadius;

            foreach (var ship in allShips)
            {
                float dist = Vector3.Distance(player.transform.position, ship.transform.position);
                if (dist < closestDist)
                {
                    closestShip = ship;
                    closestDist = dist;
                }
            }
            

            if (closestShip != null)
            {
                Jotunn.Logger.LogInfo($"Found nearby ship at distance: {closestDist}m");
                return closestShip;
            }

            return null;
        }

        // Check if player is seated (attached)
        private bool IsPlayerSeated(Player player)
        {
            return player != null && player.IsAttached();
        }

        // Update method that gets called every frame
        private void Update()
        {  
            // Check for players on ships
            Player localPlayer = Player.m_localPlayer;

            if (localPlayer != null && !localPlayer.IsDead())
            {
                Ship currentShip = GetPlayerShip(localPlayer);
                bool isControlling = false;
                bool isSeated = IsPlayerSeated(localPlayer);

                if (currentShip != null)
                {
                    isControlling = IsPlayerControllingShip(localPlayer, currentShip);
                }

                // Detect if player moved significantly from last frame
                float playerMovement = 0f;
                if (lastPosition != Vector3.zero)
                {
                    playerMovement = Vector3.Distance(localPlayer.transform.position, lastPosition);
                }
                lastPosition = localPlayer.transform.position;

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
                            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"You are a passenger. Take a seat and press {activateKey.Value} to help row!");
                        }
                    }
                    else if (lastShip != null)
                    {
                        Jotunn.Logger.LogInfo("Player left the ship");

                        // Make sure to stop rowing and hide UI if player leaves ship
                        if (lastShip.GetComponent<ShipRowingManager>() != null)
                        {
                            lastShip.GetComponent<ShipRowingManager>().StopRowing(localPlayer);
                        }
                        if (rowingUI != null)
                        {
                            rowingUI.SetActive(false);
                        }
                    }

                    // Update stored state
                    lastShip = currentShip;
                    wasControllingLastFrame = isControlling;
                }

                // Check for activate key press when on ship as passenger
                if (Input.GetKeyDown(activateKey.Value) && currentShip != null && !isControlling)
                {


                    if (isSeated)
                    {
                        // Player is seated on a ship and not steering it, start/stop rowing
                        ToggleRowingMinigame(localPlayer, currentShip);
                    }
                    else
                    {
                        // Inform player they need to sit down
                        MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "You need to sit down to row!");
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

                    // Hide UI
                    if (rowingUI != null)
                    {
                        rowingUI.SetActive(false);
                    }
                }
                else
                {
                    rowingManager.StartRowing(player);
                    Jotunn.Logger.LogInfo("Player started rowing");
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"Started rowing! Press {rowingKey.Value} when the markers align!");

                    // Show UI
                    if (rowingUI != null)
                    {
                        rowingUI.SetActive(true);
                    }
                }
            }
        }

        // Update UI for the rowing minigame
        public void UpdateRowingUI(float currentPosition, float targetPosition, int streak)
        {
            if (rowingUI != null && rowingUI.activeSelf)
            {
                // Update current marker position
                float barWidth = progressBarBg.rect.width;
                progressBarCurrent.anchoredPosition = new Vector2((currentPosition * barWidth), 0);

                // Update target marker position
                progressBarTarget.anchoredPosition = new Vector2((targetPosition * barWidth), 0);

                // Update text
                instructionText.text = $"Press {rowingKey.Value} when markers align!";
                streakText.text = $"Streak: {streak}";
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

                    // Using improved ship detection
                    Ship playerShip = mainPlugin.GetPlayerShip(player);
                    bool isControlling = playerShip != null && mainPlugin.IsPlayerControllingShip(player, playerShip);
                    bool isOnShip = playerShip == ship;
                    bool isSeated = mainPlugin.IsPlayerSeated(player);

                    // If player is no longer on the ship, dead, controlling the ship, or not seated, stop their rowing
                    if (!isOnShip || player.IsDead() || isControlling || !isSeated)
                    {
                        playersToRemove.Add(player);
                        Jotunn.Logger.LogInfo($"Removing player from rowing (not on this ship: {!isOnShip}, dead: {player.IsDead()}, steering: {isControlling}, not seated: {!isSeated})");

                        // Hide UI if this is the local player
                        if (player == Player.m_localPlayer && mainPlugin.rowingUI != null)
                        {
                            mainPlugin.rowingUI.SetActive(false);
                        }

                        continue;
                    }

                    // Update the minigame and apply forces if successful
                    float rowingForce = minigame.Update();
                    if (rowingForce > 0)
                    {
                        // Apply additional force to the ship in its forward direction

                        if(ship.GetSpeedSetting() == Ship.Speed.Back)
                        {
                            ship.m_body.AddForce(-ship.transform.forward * rowingForce, ForceMode.Force);
                        }
                        else
                        {
                            ship.m_body.AddForce(ship.transform.forward * rowingForce, ForceMode.Force);
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
            private bool isSuccess = false;
            private float successTimer = 0f;
            private int successStreak = 0;

            // Constants
            private const float TARGET_WINDOW = 0.1f;
            private const float MAX_POSITION = 1f;
            private const float MIN_POSITION = 0f;
            private const float SUCCESS_DURATION = 1f;

            // Reference to main plugin
            private JotunnModStub mainPlugin;

            public RowingMinigame(Player player, Ship ship)
            {
                this.player = player;
                this.ship = ship;

                // Initialize random starting position for the minigame
                targetPosition = UnityEngine.Random.Range(0.3f, 0.7f);
                currentPosition = 0f;

                // Get reference to main plugin
                mainPlugin = BepInEx.Bootstrap.Chainloader.PluginInfos[JotunnModStub.PluginGUID].Instance as JotunnModStub;

                // Get configured speed
                speed = mainPlugin.minigameSuccessSpeed.Value;

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

                // Update UI for local player
                if (player == Player.m_localPlayer)
                {
                    mainPlugin.UpdateRowingUI(currentPosition, targetPosition, successStreak);
                }

                // Check for player input
                KeyCode rowKey = mainPlugin.rowingKey.Value;
                if (Input.GetKeyDown(rowKey) && player == Player.m_localPlayer)
                {
                    // Check if player hit the target
                    float distance = Math.Abs(currentPosition - targetPosition);

                    Jotunn.Logger.LogInfo($"Player pressed {rowKey}! Current: {currentPosition:F2}, Target: {targetPosition:F2}, Distance: {distance:F2}");

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
                    float multiplier = mainPlugin.rowingPowerMultiplier.Value;

                    // Return force to apply to ship (with streak bonus)
                    float streakBonus = 1f + (successStreak * 0.1f);
                    return multiplier * 100f * streakBonus;
                }

                return 0f;
            }
        }
    }
}