using System.Collections.Generic;
using UnityEngine;

namespace RowAnOar
{
    public class ShipRowingManager : MonoBehaviour
    {
        private Ship ship;
        private readonly Dictionary<Player, RowingMinigame> rowingPlayers = new Dictionary<Player, RowingMinigame>(8);
        private readonly HashSet<long> remoteRowingPlayers = new HashSet<long>();
        private readonly List<Player> playersToRemove = new List<Player>(8);

        // Physics optimization
        private const float PHYSICS_UPDATE_INTERVAL = 0.05f;
        private float lastPhysicsUpdateTime = 0f;

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
            return ship.GetSpeedSetting() == Ship.Speed.Back ? -ship.transform.forward : ship.transform.forward;
        }

        private void Update()
        {
            if (rowingPlayers.Count == 0) return;

            playersToRemove.Clear();

            bool isPhysicsUpdate = Time.time - lastPhysicsUpdateTime >= PHYSICS_UPDATE_INTERVAL;
            if (isPhysicsUpdate)
                lastPhysicsUpdateTime = Time.time;

            foreach (var kv in rowingPlayers)
            {
                Player player = kv.Key;
                RowingMinigame minigame = kv.Value;

                Ship playerShip = RowAnOar.Instance.GetPlayerShip(player);
                bool isControlling = playerShip != null && RowAnOar.Instance.IsPlayerControllingShip(player, playerShip);
                bool isOnShip = playerShip == ship;
                bool isSeated = RowAnOar.Instance.IsPlayerSeated(player);

                if (!isOnShip || player.IsDead() || isControlling || !isSeated)
                {
                    playersToRemove.Add(player);
                    if (player == Player.m_localPlayer)
                    {
                        RowAnOar.Instance.NetworkManager.SendRowingStateToNetwork(player, ship, false);
                        RowAnOar.Instance.UIManager.HideRowingUI();
                    }
                    continue;
                }

                float rowingForce = minigame.Update();
                if (rowingForce > 0 && isPhysicsUpdate)
                {
                    Vector3 forceDirection = GetForceDirection();

                    if (player == Player.m_localPlayer)
                    {
                        RowAnOar.Instance.NetworkManager.SendRowingForceToNetwork(ship, forceDirection, rowingForce);
                        ship.m_body.AddForce(forceDirection * rowingForce, ForceMode.Force);
                    }
                }
            }

            foreach (Player player in playersToRemove)
                rowingPlayers.Remove(player);
        }
    }
}
