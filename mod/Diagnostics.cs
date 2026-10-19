using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WitchCtl
{
    /// <summary>Read-only facts about the game's networking and fight objects (single player runs through a Mirror host).</summary>
    internal static class Diagnostics
    {
        internal static object Read()
        {
            var fight = FightManager.Instance;
            return new
            {
                scene = SceneManager.GetActiveScene().name,
                steamInitialized = SteamManager.Initialized,
                network = new
                {
                    serverActive = NetworkServer.active,
                    clientActive = NetworkClient.active,
                    clientConnected = NetworkClient.isConnected,
                    managerMode = NetworkManager.singleton != null ? NetworkManager.singleton.mode.ToString() : null,
                    transport = Transport.active != null ? Transport.active.GetType().Name : null,
                },
                fight = fight == null ? null : new
                {
                    fightType = fight.fightType.ToString(),
                    isServer = fight.isServer,
                    isClient = fight.isClient,
                    isFake = fight.IsFake,
                    selfIndex = fight.selfIndex,
                    tempRoles = fight.TempRoleList?.Count,
                    statuses = fight.statuses?.Count,
                    level = fight.level,
                },
                fightObjects = Object.FindObjectsOfType<FightObject>(true).Select(o => o.GetType().Name + ":" + o.gameObject.name).ToList(),
                playerInstance = FightPlayer.Instance != null,
            };
        }
    }
}
