using System.Collections;
using System.Diagnostics;
using GameNetcodeStuff;
using UnityEngine;

namespace JPOGStegosaurus
{
    //Kills the local player and pins stabbed bodies to the tail spikes
    internal class StegoBodyHelper
    {
        private const float BodySpawnTimeout = 2f;
        private const float TurnAwayDuration = 2f;
        private const float TurnAwaySpeed = 60f;
        // Index of the "sliced in half" ragdoll in StartOfRound.playerRagdolls (the one the Clay Surgeon uses)
        private const int SlicedInHalfDeathAnimation = 7;
        private const float SlicedInHalfChance = 0.4f;
        private const float BloodDropInterval = 0.75f;
        private const float BloodDripDuration = 20f;

        private readonly JPOGStegosaurusAI stegoAI;

        public StegoBodyHelper(JPOGStegosaurusAI stegoAI)
        {
            this.stegoAI = stegoAI;
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        //Runs on the client of the player that was hit
        public void KillLocalPlayer(CauseOfDeath causeOfDeath)
        {
            stegoAI.StartCoroutine(KillLocalPlayerRoutine(causeOfDeath));
        }

        private IEnumerator KillLocalPlayerRoutine(CauseOfDeath causeOfDeath)
        {
            PlayerControllerB localPlayer = GameNetworkManager.Instance.localPlayerController;
            if (localPlayer == null || localPlayer.isPlayerDead || stegoAI.isEnemyDead)
            {
                yield break;
            }

            // Sometimes a spiked player is cut in half, which also detaches the head. The victim's client picks, the ragdoll type is synced by the game.
            bool slicedInHalf = causeOfDeath == CauseOfDeath.Stabbing && Random.value < SlicedInHalfChance;
            int deathAnimation = slicedInHalf ? SlicedInHalfDeathAnimation : 0;
            localPlayer.KillPlayer(Vector3.zero, spawnBody: true, causeOfDeath, deathAnimation);

            yield return WaitForDeadBody(localPlayer);
            if (localPlayer.deadBody == null)
            {
                LogIfDebugBuild("JPOGStegosaurus: Player body was not spawned or found within 2 seconds");
                localPlayer.inAnimationWithEnemy = null;
                yield break;
            }

            // Tell the host the body exists, so it can pin it and turn away from it
            stegoAI.PlayerKilledServerRpc((int)localPlayer.playerClientId, causeOfDeath == CauseOfDeath.Stabbing);
        }

        //Runs on the owner (host) after a player was killed
        public void OnPlayerKilled(int playerId, bool stabbed)
        {
            if (stabbed)
            {
                stegoAI.PinBodyToSpikeClientRpc(playerId, stegoAI.enemyRandom.Next(0, 4));
            }
            stegoAI.StartCoroutine(TurnAwayRoutine());
        }

        private IEnumerator TurnAwayRoutine()
        {
            Quaternion rotateTo = Quaternion.Euler(0f, RoundManager.Instance.YRotationThatFacesTheFarthestFromPosition(stegoAI.transform.position + Vector3.up * 0.6f), 0f);
            Quaternion rotateFrom = stegoAI.transform.rotation;
            float startTime = Time.timeSinceLevelLoad;
            while (Time.timeSinceLevelLoad - startTime < TurnAwayDuration && !stegoAI.isEnemyDead)
            {
                yield return null;
                stegoAI.transform.rotation = Quaternion.RotateTowards(rotateFrom, rotateTo, TurnAwaySpeed * Time.deltaTime);
            }
        }

        //Runs on every client
        public void PinBodyToSpike(int playerId, int spikeIndex)
        {
            stegoAI.StartCoroutine(PinBodyToSpikeRoutine(playerId, spikeIndex));
        }

        private IEnumerator PinBodyToSpikeRoutine(int playerId, int spikeIndex)
        {
            PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[playerId];
            yield return WaitForDeadBody(player);
            DeadBodyInfo body = player.deadBody;
            if (body == null)
            {
                LogIfDebugBuild($"JPOGStegosaurus: Something went wrong with attaching player[{playerId}]'s dead body to a spike.");
                yield break;
            }

            Transform[] spikes = { stegoAI.tailSpike1, stegoAI.tailSpike2, stegoAI.tailSpike3, stegoAI.tailSpike4 };
            spikeIndex = Mathf.Clamp(spikeIndex, 0, spikes.Length - 1);
            Transform spike = spikes[spikeIndex];
            body.attachedTo = spike;
            body.attachedLimb = body.bodyParts[5];
            body.matchPositionExactly = true;
            body.MakeCorpseBloody();
            LogIfDebugBuild($"JPOGStegosaurus: Successfully attached player[{playerId}]'s dead body to a spike.");
            // The head is a separate object when the body is cut in half (detachedHead), put it on the next spike
            Transform headSpike = spikes[(spikeIndex + 1) % spikes.Length];
            stegoAI.StartCoroutine(SpikedBodyRoutine(player, body, spike, headSpike));
        }

        //Runs on every client for as long as the body is pinned to its spike
        private IEnumerator SpikedBodyRoutine(PlayerControllerB player, DeadBodyInfo body, Transform spike, Transform headSpike)
        {
            Transform? head = body.detachedHead ? body.detachedHeadObject : null;
            Rigidbody? headRigidbody = head != null ? head.GetComponent<Rigidbody>() : null;
            if (head != null)
            {
                head.SetParent(headSpike);
                head.localPosition = Vector3.zero;
                if (headRigidbody != null)
                {
                    headRigidbody.isKinematic = true;
                }
            }

            // Bodies that are attached to something can't be grabbed unless this is set. Grabbing the body replaces attachedTo, which frees it.
            body.canBeGrabbedBackByPlayers = true;

            float endTime = Time.time + BloodDripDuration;
            // The body is also released when it is grabbed or teleported, then attachedTo is no longer the spike
            while (!stegoAI.isEnemyDead && player.isPlayerDead && player.deadBody == body && body != null && body.attachedTo == spike)
            {
                if (Time.time < endTime)
                {
                    // For a dead player DropBlood drops blood below the dead body
                    player.DropBlood(Vector3.down, leaveBlood: true, leaveFootprint: false);
                }
                yield return new WaitForSeconds(BloodDropInterval);
            }

            if (body != null)
            {
                body.canBeGrabbedBackByPlayers = false;
                if (body.attachedTo == spike)
                {
                    // The Stegosaurus died, so the body falls off the spike
                    body.attachedTo = null;
                    body.attachedLimb = null;
                    body.matchPositionExactly = false;
                }
            }

            if (head != null)
            {
                // Let the head drop where the body ended up, since it's no longer on the spike
                Transform? parent = RoundManager.Instance.mapPropsContainer != null ? RoundManager.Instance.mapPropsContainer.transform : null;
                head.SetParent(parent, worldPositionStays: true);
                if (body != null)
                {
                    head.position = body.bodyParts[0].transform.position;
                }
                if (headRigidbody != null)
                {
                    headRigidbody.isKinematic = false;
                }
            }
        }

        private static IEnumerator WaitForDeadBody(PlayerControllerB player)
        {
            float startTime = Time.timeSinceLevelLoad;
            while (player.deadBody == null && Time.timeSinceLevelLoad - startTime < BodySpawnTimeout)
            {
                yield return null;
            }
        }
    }
}
