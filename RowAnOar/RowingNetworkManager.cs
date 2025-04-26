using Jotunn.Entities;
using Jotunn.Managers;
using System.Collections;
using UnityEngine;

namespace RowAnOar
{
    public class RowingNetworkManager
    {
        private CustomRPC RowingForceRPC;
        private CustomRPC RowingStateRPC;
        
        private const float NETWORK_UPDATE_INTERVAL = 0.1f;
        private float lastNetworkUpdateTime = 0f;

        public void RegisterRPCs()
        {
            RowingForceRPC = NetworkManager.Instance.AddRPC("RowingForceRPC", RowingForceServerReceive, RowingForceClientReceive);
            RowingStateRPC = NetworkManager.Instance.AddRPC("RowingStateRPC", RowingStateServerReceive, RowingStateClientReceive);
        }

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

            ZNetView shipView = ship.GetComponent<ZNetView>();
            if (shipView == null) return;

            ZPackage pkg = new ZPackage();
            pkg.Write(player.GetPlayerID());
            pkg.Write(shipView.GetZDO().m_uid);
            pkg.Write(isRowing);

            RowingStateRPC.SendPackage(ZRoutedRpc.instance.GetServerPeerID(), pkg);
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
                ZNetView view = obj.GetComponent<ZNetView>();
                if (view != null)
                {
                    Ship ship = obj.GetComponent<Ship>();
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
                ZNetView view = obj.GetComponent<ZNetView>();
                if (view != null)
                {
                    Ship ship = obj.GetComponent<Ship>();
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
                    ZNetView view = obj.GetComponent<ZNetView>();
                    Ship ship = view?.GetComponent<Ship>();
                    if (ship != null)
                    {
                        ship.GetComponent<ShipRowingManager>()?.UpdateRemotePlayerRowingState(playerID, isRowing);
                    }
                }
            }

            yield return null;
        }
    }
}
