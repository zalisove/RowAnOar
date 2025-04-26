using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace JotunnModStub
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class JotunnModStub : BaseUnityPlugin
    {
        public const string PluginGUID = "com.zalisove.rowanoar";
        public const string PluginName = "RowAnOar";
        public const string PluginVersion = "0.0.6";

        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();
        private readonly Harmony harmony = new Harmony(PluginGUID);

        private ConfigEntry<float> rowingPowerMultiplier;
        private ConfigEntry<float> minigameSuccessSpeed;
        private ConfigEntry<KeyCode> activateKey;
        private ConfigEntry<KeyCode> rowingKey;
        private ConfigEntry<float> shipDetectionRadius;

        private Ship lastShip = null;
        private bool wasControllingLastFrame = false;
        private Vector3 lastPosition = Vector3.zero;
        private float lastShipCheckTime = 0f;
        private const float SHIP_CHECK_INTERVAL = 0.5f;
        private const float NETWORK_UPDATE_INTERVAL = 0.1f;
        private float lastNetworkUpdateTime = 0f;

        // Кешовані списки та об'єкти
        private readonly List<Ship> cachedShips = new List<Ship>();
        private readonly Dictionary<GameObject, Ship> cachedShipComponents = new Dictionary<GameObject, Ship>();
        private readonly Dictionary<GameObject, ZNetView> cachedZNetViews = new Dictionary<GameObject, ZNetView>();

        private GameObject rowingUI;
        private RectTransform progressBarBg;
        private RectTransform progressBarCurrent;
        private RectTransform progressBarTarget;
        private Text instructionText;
        private Text streakText;

        // Кешування значень для UI
        private float lastUICurrentPosition = -1f;
        private float lastUITargetPosition = -1f;
        private int lastUIStreak = -1;

        private CustomRPC RowingForceRPC;
        private CustomRPC RowingStateRPC;

        public static readonly WaitForSeconds OneSecondWait = new WaitForSeconds(1f);
        public static readonly WaitForSeconds HalfSecondWait = new WaitForSeconds(0.5f);
        public static readonly WaitForSeconds NetworkWait = new WaitForSeconds(NETWORK_UPDATE_INTERVAL);

        private void Awake()
        {
            Jotunn.Logger.LogInfo("RowAnOar has landed");

            ConfigurationManagerAttributes isAdminOnly = new ConfigurationManagerAttributes { IsAdminOnly = true };
            AcceptableValueRange<float> floatRange = new AcceptableValueRange<float>(0f, 200f);

            rowingPowerMultiplier = Config.Bind("General", "RowingPowerMultiplier", 1.5f,
                new ConfigDescription("Multiplier for ship speed when rowing is successful", new AcceptableValueRange<float>(0f, 2000f), isAdminOnly));
            minigameSuccessSpeed = Config.Bind("General", "MinigameSuccessSpeed", 1.0f,
                new ConfigDescription("How quickly players need to row to boost the ship", new AcceptableValueRange<float>(0f, 1.5f), isAdminOnly));
            shipDetectionRadius = Config.Bind("General", "ShipDetectionRadius", 5f,
                new ConfigDescription("Radius to check if player is still on the ship when interacting with objects",
                new AcceptableValueRange<float>(1f, 10f), isAdminOnly));
            activateKey = Config.Bind("Controls", "ActivateKey", KeyCode.B, "Key to activate the rowing minigame");
            rowingKey = Config.Bind("Controls", "RowingKey", KeyCode.N, "Key to press during the rowing minigame");

            PrefabManager.OnVanillaPrefabsAvailable += AddRowingToShips;
            GUIManager.OnCustomGUIAvailable += SetupRowingUI;

            RowingForceRPC = NetworkManager.Instance.AddRPC("RowingForceRPC", RowingForceServerReceive, RowingForceClientReceive);
            RowingStateRPC = NetworkManager.Instance.AddRPC("RowingStateRPC", RowingStateServerReceive, RowingStateClientReceive);

            harmony.PatchAll();
        }

        private void Start()
        {
            // Ініціалізуємо кешований список кораблів при старті
            RefreshCachedShips();

            // Запускаємо корутину для періодичного оновлення кешу кораблів
            StartCoroutine(UpdateCachedShipsRoutine());
        }

        private IEnumerator UpdateCachedShipsRoutine()
        {
            while (true)
            {
                yield return OneSecondWait;
                RefreshCachedShips();
            }
        }

        private void RefreshCachedShips()
        {
            cachedShips.Clear();
            var ships = GameObject.FindObjectsOfType<Ship>();
            cachedShips.AddRange(ships);
        }

        private IEnumerator RowingForceServerReceive(long sender, ZPackage package)
        {
            ZDOID shipZDOID = package.ReadZDOID();
            Vector3 forceDirection = package.ReadVector3();
            float forceAmount = package.ReadSingle();

            ZPackage broadcastPackage = new ZPackage();
            broadcastPackage.Write(shipZDOID);
            broadcastPackage.Write(forceDirection);
            broadcastPackage.Write(forceAmount);

            RowingForceRPC.SendPackage(ZNet.instance.m_peers, broadcastPackage);

            GameObject obj = ZNetScene.instance.FindInstance(shipZDOID);
            if (obj != null)
            {
                ZNetView view = GetCachedZNetView(obj);
                if (view != null)
                {
                    Ship ship = GetCachedShip(obj);
                    if (ship != null)
                    {
                        ship.m_body.AddForce(forceDirection * forceAmount, ForceMode.Force);
                    }
                }
            }

            yield return null;
        }

        private IEnumerator RowingForceClientReceive(long sender, ZPackage package)
        {
            ZDOID shipZDOID = package.ReadZDOID();
            Vector3 forceDirection = package.ReadVector3();
            float forceAmount = package.ReadSingle();

            GameObject obj = ZNetScene.instance.FindInstance(shipZDOID);
            if (obj != null)
            {
                ZNetView view = GetCachedZNetView(obj);
                if (view != null)
                {
                    Ship ship = GetCachedShip(obj);
                    if (ship != null)
                    {
                        ship.m_body.AddForce(forceDirection * forceAmount, ForceMode.Force);
                    }
                }
            }

            yield return null;
        }

        private IEnumerator RowingStateServerReceive(long sender, ZPackage package)
        {
            RowingStateRPC.SendPackage(ZNet.instance.m_peers, new ZPackage(package.GetArray()));
            yield return null;
        }

        private IEnumerator RowingStateClientReceive(long sender, ZPackage package)
        {
            long playerID = package.ReadLong();
            ZDOID shipZDOID = package.ReadZDOID();
            bool isRowing = package.ReadBool();

            if (playerID != ZNet.instance.m_characterID.UserID)
            {
                GameObject obj = ZNetScene.instance.FindInstance(shipZDOID);
                if (obj != null)
                {
                    ZNetView view = GetCachedZNetView(obj);
                    Ship ship = view?.GetComponent<Ship>();
                    if (ship != null)
                    {
                        ship.GetComponent<ShipRowingManager>()?.UpdateRemotePlayerRowingState(playerID, isRowing);
                    }
                }
            }

            yield return null;
        }

        private void SetupRowingUI()
        {
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

            GameObject progressBarBgGO = new GameObject("ProgressBarBg", typeof(RectTransform), typeof(Image));
            progressBarBgGO.transform.SetParent(rowingUI.transform, false);

            progressBarBg = progressBarBgGO.GetComponent<RectTransform>();
            progressBarBg.anchorMin = new Vector2(0.5f, 0.5f);
            progressBarBg.anchorMax = new Vector2(0.5f, 0.5f);
            progressBarBg.sizeDelta = new Vector2(300f, 30f);
            progressBarBg.anchoredPosition = new Vector2(0f, 0f);
            progressBarBgGO.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

            GameObject currentMarkerGO = new GameObject("CurrentMarker", typeof(RectTransform), typeof(Image));
            currentMarkerGO.transform.SetParent(progressBarBg, false);
            progressBarCurrent = currentMarkerGO.GetComponent<RectTransform>();
            progressBarCurrent.anchorMin = new Vector2(0f, 0f);
            progressBarCurrent.anchorMax = new Vector2(0f, 1f);
            progressBarCurrent.sizeDelta = new Vector2(10f, 0f);
            progressBarCurrent.anchoredPosition = new Vector2(150f, 0f);
            currentMarkerGO.GetComponent<Image>().color = Color.yellow;

            GameObject targetMarkerGO = new GameObject("TargetMarker", typeof(RectTransform), typeof(Image));
            targetMarkerGO.transform.SetParent(progressBarBg, false);
            progressBarTarget = targetMarkerGO.GetComponent<RectTransform>();
            progressBarTarget.anchorMin = new Vector2(0f, 0f);
            progressBarTarget.anchorMax = new Vector2(0f, 1f);
            progressBarTarget.sizeDelta = new Vector2(20f, 0f);
            progressBarTarget.anchoredPosition = new Vector2(75f, 0f);
            targetMarkerGO.GetComponent<Image>().color = new Color(0f, 1f, 0f, 0.5f);

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

            // Ініціалізація кешованих значень UI
            lastUICurrentPosition = 0f;
            lastUITargetPosition = 0f;
            lastUIStreak = 0;
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

            // Оновлюємо кеш кораблів після додавання компонентів
            RefreshCachedShips();
        }

        private bool IsPlayerControllingShip(Player player, Ship ship) => false;

        private Ship GetPlayerShip(Player player)
        {
            if (player == null) return null;

            // Використовуємо прямий метод, який є найшвидшим
            Ship directShip = player.GetStandingOnShip();
            if (directShip != null) return directShip;

            // Використовуємо кешований корабель, якщо час перевірки не минув
            if (lastShip != null && Time.time - lastShipCheckTime < SHIP_CHECK_INTERVAL)
                return lastShip;

            lastShipCheckTime = Time.time;
            float checkRadius = shipDetectionRadius.Value;

            // Першочергово перевіряємо останній відомий корабель
            if (lastShip != null && Vector3.Distance(player.transform.position, lastShip.transform.position) < checkRadius)
                return lastShip;

            // Використовуємо кешований список кораблів замість пошуку щоразу
            Ship closestShip = null;
            float closestDist = checkRadius;

            int shipCount = cachedShips.Count;
            for (int i = 0; i < shipCount; i++)
            {
                Ship ship = cachedShips[i];
                if (ship == null) continue;

                float dist = Vector3.Distance(player.transform.position, ship.transform.position);
                if (dist < closestDist)
                {
                    closestShip = ship;
                    closestDist = dist;
                }
            }

            return closestShip;
        }

        private Ship GetCachedShip(GameObject obj)
        {
            if (obj == null) return null;

            Ship ship;
            if (cachedShipComponents.TryGetValue(obj, out ship))
                return ship;

            ship = obj.GetComponent<Ship>();
            if (ship != null)
                cachedShipComponents[obj] = ship;

            return ship;
        }

        private ZNetView GetCachedZNetView(GameObject obj)
        {
            if (obj == null) return null;

            ZNetView view;
            if (cachedZNetViews.TryGetValue(obj, out view))
                return view;

            view = obj.GetComponent<ZNetView>();
            if (view != null)
                cachedZNetViews[obj] = view;

            return view;
        }

        private bool IsPlayerSeated(Player player) => player != null && player.IsAttached();

        public void SendRowingForceToNetwork(Ship ship, Vector3 direction, float force)
        {
            if (Time.time - lastNetworkUpdateTime < NETWORK_UPDATE_INTERVAL)
                return;

            lastNetworkUpdateTime = Time.time;

            ZNetView shipView = ship?.GetComponent<ZNetView>();
            if (shipView == null) return;

            ZPackage pkg = new ZPackage();
            pkg.Write(shipView.GetZDO().m_uid);
            pkg.Write(direction);
            pkg.Write(force);

            RowingForceRPC.SendPackage(ZRoutedRpc.instance.GetServerPeerID(), pkg);
        }

        public void SendRowingStateToNetwork(Player player, Ship ship, bool isRowing)
        {
            if (player == null || ship == null) return;

            ZNetView shipView = GetCachedZNetView(ship.gameObject);
            if (shipView == null) return;

            ZPackage pkg = new ZPackage();
            pkg.Write(player.GetPlayerID());
            pkg.Write(shipView.GetZDO().m_uid);
            pkg.Write(isRowing);

            RowingStateRPC.SendPackage(ZRoutedRpc.instance.GetServerPeerID(), pkg);
        }

        private void Update()
        {
            Player localPlayer = Player.m_localPlayer;
            if (localPlayer == null || localPlayer.IsDead()) return;

            Ship currentShip = GetPlayerShip(localPlayer);
            bool isControlling = currentShip != null && IsPlayerControllingShip(localPlayer, currentShip);
            bool isSeated = IsPlayerSeated(localPlayer);

            if (rowingUI != null && rowingUI.activeSelf && !isSeated)
            {
                rowingUI.SetActive(false);
                if (currentShip != null)
                {
                    currentShip.GetComponent<ShipRowingManager>()?.StopRowing(localPlayer);
                    SendRowingStateToNetwork(localPlayer, currentShip, false);
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Stopped rowing: you need to be seated");
                }
            }


            float playerMovement = 0f;
            if (lastPosition != Vector3.zero)
                playerMovement = Vector3.Distance(localPlayer.transform.position, lastPosition);
            lastPosition = localPlayer.transform.position;

            if (currentShip != lastShip || isControlling != wasControllingLastFrame)
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
                    lastShip.GetComponent<ShipRowingManager>()?.StopRowing(localPlayer);
                    if (rowingUI != null)
                        rowingUI.SetActive(false);
                }

                lastShip = currentShip;
                wasControllingLastFrame = isControlling;
            }

            if (Input.GetKeyDown(activateKey.Value) && currentShip != null && !isControlling)
            {
                if (isSeated)
                    ToggleRowingMinigame(localPlayer, currentShip);
                else
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "You need to sit down to row!");
            }
        }

        private void ToggleRowingMinigame(Player player, Ship ship)
        {
            ShipRowingManager rowingManager = ship.GetComponent<ShipRowingManager>();
            if (rowingManager == null) return;

            if (rowingManager.IsPlayerRowing(player))
            {
                rowingManager.StopRowing(player);
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Stopped rowing");
                SendRowingStateToNetwork(player, ship, false);
                if (rowingUI != null)
                    rowingUI.SetActive(false);
            }
            else
            {
                rowingManager.StartRowing(player);
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"Started rowing! Press {rowingKey.Value} when the markers align!");
                SendRowingStateToNetwork(player, ship, true);
                if (rowingUI != null)
                    rowingUI.SetActive(true);
            }
        }

        public void UpdateRowingUI(float currentPosition, float targetPosition, int streak)
        {
            if (rowingUI == null || !rowingUI.activeSelf) return;

            // Перевірка чи потрібно оновлювати UI
            bool needsUpdate =
                Math.Abs(lastUICurrentPosition - currentPosition) > 0.01f ||
                Math.Abs(lastUITargetPosition - targetPosition) > 0.01f ||
                lastUIStreak != streak;

            if (!needsUpdate) return;

            // Оновлення кешованих значень
            lastUICurrentPosition = currentPosition;
            lastUITargetPosition = targetPosition;
            lastUIStreak = streak;

            float barWidth = progressBarBg.rect.width;
            progressBarCurrent.anchoredPosition = new Vector2((currentPosition * barWidth), 0);
            progressBarTarget.anchoredPosition = new Vector2((targetPosition * barWidth), 0);
            instructionText.text = $"Press {rowingKey.Value} when markers align!";
            streakText.text = $"Streak: {streak}";
        }

        public class ShipRowingManager : MonoBehaviour
        {
            private Ship ship;
            private readonly Dictionary<Player, RowingMinigame> rowingPlayers = new Dictionary<Player, RowingMinigame>(8);
            private readonly HashSet<long> remoteRowingPlayers = new HashSet<long>();
            private readonly List<Player> playersToRemove = new List<Player>(8);

            // Для оптимізації фізики
            private const float PHYSICS_UPDATE_INTERVAL = 0.05f;
            private float lastPhysicsUpdateTime = 0f;

            // Кешування напрямку руху
            private Vector3 cachedForceDirection = Vector3.forward;

            private void Awake() => ship = GetComponent<Ship>();

            public bool IsPlayerRowing(Player player) => rowingPlayers.ContainsKey(player);

            public void StartRowing(Player player)
            {
                if (!rowingPlayers.ContainsKey(player))
                    rowingPlayers.Add(player, new RowingMinigame(player, ship));
            }

            public void StopRowing(Player player)
            {
                if (rowingPlayers.ContainsKey(player))
                    rowingPlayers.Remove(player);
            }

            public void UpdateRemotePlayerRowingState(long playerID, bool isRowing)
            {
                if (isRowing)
                    remoteRowingPlayers.Add(playerID);
                else
                    remoteRowingPlayers.Remove(playerID);
            }

            private Vector3 GetForceDirection()
            {
                return ship.GetSpeedSetting() == Ship.Speed.Back ? -ship.transform.forward: ship.transform.forward;
            }

            private void Update()
            {
                if (rowingPlayers.Count == 0) return;

                playersToRemove.Clear();
                JotunnModStub mainPlugin = BepInEx.Bootstrap.Chainloader.PluginInfos[JotunnModStub.PluginGUID].Instance as JotunnModStub;

                bool isPhysicsUpdate = Time.time - lastPhysicsUpdateTime >= PHYSICS_UPDATE_INTERVAL;
                if (isPhysicsUpdate)
                    lastPhysicsUpdateTime = Time.time;

                foreach (var kv in rowingPlayers)
                {
                    Player player = kv.Key;
                    RowingMinigame minigame = kv.Value;

                    Ship playerShip = mainPlugin.GetPlayerShip(player);
                    bool isControlling = playerShip != null && mainPlugin.IsPlayerControllingShip(player, playerShip);
                    bool isOnShip = playerShip == ship;
                    bool isSeated = mainPlugin.IsPlayerSeated(player);

                    if (!isOnShip || player.IsDead() || isControlling || !isSeated)
                    {
                        playersToRemove.Add(player);
                        if (player == Player.m_localPlayer)
                        {
                            mainPlugin.SendRowingStateToNetwork(player, ship, false);
                            if (mainPlugin.rowingUI != null)
                                mainPlugin.rowingUI.SetActive(false);
                        }
                        continue;
                    }

                    float rowingForce = minigame.Update();
                    if (rowingForce > 0 && isPhysicsUpdate)
                    {
                        Vector3 forceDirection = GetForceDirection();

                        if (player == Player.m_localPlayer)
                        {
                            mainPlugin.SendRowingForceToNetwork(ship, forceDirection, rowingForce);
                            ship.m_body.AddForce(forceDirection * rowingForce, ForceMode.Force);
                        }
                    }
                }

                foreach (Player player in playersToRemove)
                    rowingPlayers.Remove(player);
            }
        }

        public class RowingMinigame
        {
            private Player player;
            private Ship ship;
            private float targetPosition = 0.5f;
            private float currentPosition = 0f;
            private float direction = 1f;
            private float speed = 1f;
            private bool isSuccess = false;
            private float successTimer = 0f;
            private int successStreak = 0;

            private const float TARGET_WINDOW = 0.1f;
            private const float MAX_POSITION = 1f;
            private const float MIN_POSITION = 0f;
            private const float SUCCESS_DURATION = 1f;
            private const float UI_UPDATE_FREQUENCY = 0.05f;
            private float lastUIUpdateTime = 0f;

            private JotunnModStub mainPlugin;

            // Для оптимізації мережевого коду
            private float lastRowingActionTime = 0f;
            private const float ROWING_ACTION_COOLDOWN = 0.2f;

            // Для кешування напрямку сили
            private Vector3 cachedDirection = Vector3.zero;

            public RowingMinigame(Player player, Ship ship)
            {
                this.player = player;
                this.ship = ship;
                targetPosition = UnityEngine.Random.Range(0.3f, 0.7f);
                currentPosition = 0f;
                mainPlugin = BepInEx.Bootstrap.Chainloader.PluginInfos[JotunnModStub.PluginGUID].Instance as JotunnModStub;
                speed = mainPlugin.minigameSuccessSpeed.Value + (successStreak * 0.01f);
                lastUIUpdateTime = Time.time;
                lastRowingActionTime = 0f;
            }

            public float Update()
            {
                currentPosition += direction * speed * Time.deltaTime;

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

                if (player == Player.m_localPlayer)
                {
                    lastUIUpdateTime = Time.time;
                    mainPlugin.UpdateRowingUI(currentPosition, targetPosition, successStreak);
                }

                KeyCode rowKey = mainPlugin.rowingKey.Value;
                if (Input.GetKeyDown(rowKey) && player == Player.m_localPlayer && Time.time - lastRowingActionTime >= ROWING_ACTION_COOLDOWN)
                {
                    lastRowingActionTime = Time.time;
                    float distance = Math.Abs(currentPosition - targetPosition);

                    if (distance < TARGET_WINDOW)
                    {
                        isSuccess = true;
                        successTimer = SUCCESS_DURATION;
                        successStreak++;
                        MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"Good rowing! Streak: {successStreak}");
                        targetPosition = UnityEngine.Random.Range(0.2f, 0.8f);
                    }
                    else
                    {
                        isSuccess = false;
                        successStreak = 0;
                        MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Missed! Try again.");
                    }
                }

                if (isSuccess)
                {
                    successTimer -= Time.deltaTime;
                    if (successTimer <= 0)
                        isSuccess = false;

                    float multiplier = mainPlugin.rowingPowerMultiplier.Value;
                    float streakBonus = 1f + (successStreak * 0.1f);
                    return multiplier * 100f * streakBonus;
                }

                return 0f;
            }
        }
    }
}