using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using UnityEngine;

namespace RowAnOar
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class RowAnOar : BaseUnityPlugin
    {
        public const string PluginGUID = "com.zalisove.rowanoar";
        public const string PluginName = "RowAnOar";
        public const string PluginVersion = "0.0.7";

        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();
        private readonly Harmony harmony = new Harmony(PluginGUID);

        // Configuration entries
        private ConfigEntry<float> rowingPowerMultiplier;
        private ConfigEntry<float> minigameSuccessSpeed;
        private ConfigEntry<KeyCode> activateKey;
        private ConfigEntry<KeyCode> rowingKey;

        // Component references
        private Ship lastShip = null;
        private bool wasControllingLastFrame = false;
        private Vector3 lastPosition = Vector3.zero;

        // Singleton instance
        public static RowAnOar Instance { get; private set; }

        // Component managers
        public RowingUIManager UIManager { get; private set; }
        public RowingNetworkManager NetworkManager { get; private set; }

        // Config property accessors
        public float RowingPowerMultiplier => rowingPowerMultiplier.Value;
        public float MinigameSuccessSpeed => minigameSuccessSpeed.Value;
        public KeyCode ActivateKey => activateKey.Value;
        public KeyCode RowingKey => rowingKey.Value;

        private void Awake()
        {
            Instance = this;
            Jotunn.Logger.LogInfo("RowAnOar has landed");

            InitializeConfig();
            
            UIManager = new RowingUIManager();
            NetworkManager = new RowingNetworkManager();

            PrefabManager.OnVanillaPrefabsAvailable += AddRowingToShips;
            GUIManager.OnCustomGUIAvailable += UIManager.SetupRowingUI;

            NetworkManager.RegisterRPCs();
            
            harmony.PatchAll();
        }

        private void InitializeConfig()
        {
            ConfigurationManagerAttributes isAdminOnly = new ConfigurationManagerAttributes { IsAdminOnly = true };

            rowingPowerMultiplier = Config.Bind("General", "RowingPowerMultiplier", 1.5f,
                new ConfigDescription("Multiplier for ship speed when rowing is successful", new AcceptableValueRange<float>(0f, 2000f), isAdminOnly));
            
            minigameSuccessSpeed = Config.Bind("General", "MinigameSuccessSpeed", 1.0f,
                new ConfigDescription("How quickly players need to row to boost the ship", new AcceptableValueRange<float>(0f, 1.5f), isAdminOnly));
            
            activateKey = Config.Bind("Controls", "ActivateKey", KeyCode.B, 
                "Key to activate the rowing minigame");
            
            rowingKey = Config.Bind("Controls", "RowingKey", KeyCode.N, 
                "Key to press during the rowing minigame");
        }

        private void AddRowingToShips()
        {
            var ships = Resources.FindObjectsOfTypeAll<Ship>();
            foreach (var ship in ships)
            {
                if (!ship.gameObject.GetComponent<ShipRowingManager>())
                {
                    ship.gameObject.AddComponent<ShipRowingManager>();
                }
            }
            PrefabManager.OnVanillaPrefabsAvailable -= AddRowingToShips;
        }

        public bool IsPlayerControllingShip(Player player, Ship ship) => false;

        public Ship GetPlayerShip(Player player)
        {
            if (player == null) return null;
            return player.m_lastGroundBody?.GetComponent<Ship>();
        }

        public bool IsPlayerSeated(Player player) => player != null && player.IsAttached();

        private void Update()
        {
            Player localPlayer = Player.m_localPlayer;
            if (localPlayer == null || localPlayer.IsDead()) return;

            Ship currentShip = GetPlayerShip(localPlayer);
            bool isControlling = currentShip != null && IsPlayerControllingShip(localPlayer, currentShip);
            bool isSeated = IsPlayerSeated(localPlayer);

            // Check if player left the ship while rowing
            if (UIManager.IsRowingUIActive() && !isSeated)
            {
                UIManager.HideRowingUI();
                if (currentShip != null)
                {
                    currentShip.GetComponent<ShipRowingManager>()?.StopRowing(localPlayer);
                    NetworkManager.SendRowingStateToNetwork(localPlayer, currentShip, false);
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Stopped rowing: you need to be seated");
                }
            }

            // Track player movement
            float playerMovement = 0f;
            if (lastPosition != Vector3.zero)
                playerMovement = Vector3.Distance(localPlayer.transform.position, lastPosition);
            lastPosition = localPlayer.transform.position;

            // Handle ship change or control state change
            if (currentShip != lastShip || isControlling != wasControllingLastFrame)
            {
                HandleShipChange(localPlayer, currentShip, isControlling);
            }

            // Handle rowing activation
            if (Input.GetKeyDown(activateKey.Value) && currentShip != null && !isControlling)
            {
                if (isSeated)
                    ToggleRowingMinigame(localPlayer, currentShip);
                else
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "You need to sit down to row!");
            }
        }

        private void HandleShipChange(Player player, Ship currentShip, bool isControlling)
        {
            if (currentShip != null)
            {
                if (isControlling)
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "You are steering the ship");
                else
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"You are a passenger. Take a seat and press {activateKey.Value} to help row!");
            }
            else if (lastShip != null)
            {
                lastShip.GetComponent<ShipRowingManager>()?.StopRowing(player);
                UIManager.HideRowingUI();
            }

            lastShip = currentShip;
            wasControllingLastFrame = isControlling;
        }

        private void ToggleRowingMinigame(Player player, Ship ship)
        {
            ShipRowingManager rowingManager = ship.GetComponent<ShipRowingManager>();
            if (rowingManager == null) return;

            if (rowingManager.IsPlayerRowing(player))
            {
                rowingManager.StopRowing(player);
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Stopped rowing");
                NetworkManager.SendRowingStateToNetwork(player, ship, false);
                UIManager.HideRowingUI();
            }
            else
            {
                rowingManager.StartRowing(player);
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"Started rowing! Press {rowingKey.Value} when the markers align!");
                NetworkManager.SendRowingStateToNetwork(player, ship, true);
                UIManager.ShowRowingUI();
            }
        }
    }
}
