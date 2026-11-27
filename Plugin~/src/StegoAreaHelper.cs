using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using GameNetcodeStuff;
using UnityEngine;

namespace JPOGStegosaurus
{
    //Checks which players are inside the aggro / attack areas. Only used on the owner (host),
    //player positions are synced so the host can check every player.
    internal class StegoAreaHelper
    {
        private const float CheckInterval = 1f;
        private const int PlayerLayerMask = 1 << 3; // This can be found from the game's Asset Ripper output in Unity

        private readonly JPOGStegosaurusAI stegoAI;
        private readonly Collider[] overlapBuffer = new Collider[16];
        private Coroutine? checkCoroutine;

        public StegoAreaHelper(JPOGStegosaurusAI stegoAI)
        {
            this.stegoAI = stegoAI;
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public void Start()
        {
            Stop();
            checkCoroutine = stegoAI.StartCoroutine(CheckAreasRoutine());
        }

        public void Stop()
        {
            if (checkCoroutine != null)
            {
                stegoAI.StopCoroutine(checkCoroutine);
                checkCoroutine = null;
            }
        }

        private IEnumerator CheckAreasRoutine()
        {
            while (!stegoAI.isEnemyDead)
            {
                CheckAggroArea();
                CheckBackArea();
                CheckFrontArea();
                yield return new WaitForSeconds(CheckInterval);
            }
        }

        //Targetable players inside the area. The boxes are rotated along with the Stegosaurus so front and back stay in place.
        public List<PlayerControllerB> GetPlayersInArea(Transform area, bool useAreaRotation = true)
        {
            var players = new List<PlayerControllerB>();
            Quaternion rotation = useAreaRotation ? area.rotation : Quaternion.identity;
            int count = Physics.OverlapBoxNonAlloc(area.position, area.localScale, overlapBuffer, rotation, PlayerLayerMask);
            for (int i = 0; i < count; i++)
            {
                PlayerControllerB? player = overlapBuffer[i].GetComponent<PlayerControllerB>();
                if (player != null && !players.Contains(player) && stegoAI.CanAttackPlayer(player))
                {
                    players.Add(player);
                }
            }
            return players;
        }

        private void CheckAggroArea()
        {
            if (stegoAI.Irritation.IsMax)
            {
                return;
            }
            foreach (PlayerControllerB player in GetPlayersInArea(stegoAI.aggroArea))
            {
                LogIfDebugBuild($"JPOGStegosaurus: player: [{player.actualClientId}] was in the Aggro Area ");
                if (stegoAI.Irritation.TryIncrease())
                {
                    if (stegoAI.Irritation.IsMax)
                    {
                        stegoAI.targetPlayer = player;
                    }
                    break;
                }
            }
        }

        //The frontal attack should only occur when the Stegosaurus is irritated (in chasing mode).
        private void CheckFrontArea()
        {
            List<PlayerControllerB> players = GetPlayersInArea(stegoAI.attackAreaFront);
            if (players.Count == 0)
            {
                return;
            }
            if (stegoAI.currentBehaviourStateIndex == (int)StegoState.ChasingTarget)
            {
                stegoAI.Attacks.StartStompAttack();
            }
            else if (!stegoAI.Irritation.IsMax)
            {
                stegoAI.Irritation.TryIncrease();
            }
        }

        private void CheckBackArea()
        {
            if (GetPlayersInArea(stegoAI.attackAreaBack).Count == 0)
            {
                return;
            }
            LogIfDebugBuild("JPOGStegosaurus: player was in Back attack area, beginning tail attack!");
            stegoAI.Attacks.StartTailAttack();
            if (!stegoAI.Irritation.IsMax)
            {
                stegoAI.Irritation.TryIncrease();
            }
        }
    }
}
