using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace JPOGStegosaurus
{
    //Walk/run speed and animations per state, turning towards the target and getting unstuck. Only used on the owner (host).
    internal class StegoMovementHelper
    {
        private const float WalkSpeed = 3f;
        private const float RunSpeed = 6f;
        private const float StuckCheckInterval = 5f;
        private const float StuckDistance = 0.1f;
        private const float TurnSpeed = 4f;
        private const float MaxRoamTurnAngle = 70f;
        private const float MinFallbackRoamDistance = 10f;
        private const float RoamArriveDistance = 5f;
        private const float RoamTimeout = 60f;
        private const int MaxRoamPathAttempts = 8;
        // A new roam point may not be near any of the last 5 to 10 roam points
        private const int MinRememberedRoamPoints = 5;
        private const int MaxRememberedRoamPoints = 10;
        private const float RememberedRoamPointRadius = 15f;

        private readonly JPOGStegosaurusAI stegoAI;
        private float stuckTimer;
        private Vector3 lastPosition;
        private float minRoamDistance = 30f;
        private float maxRoamDistance = 90f;
        private bool hasRoamTarget;
        private Vector3 roamTarget;
        private float roamStartTime;
        private readonly List<Vector3> recentRoamPoints = new List<Vector3>();

        public StegoMovementHelper(JPOGStegosaurusAI stegoAI)
        {
            this.stegoAI = stegoAI;
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public void Configure(float minRoamDistance, float maxRoamDistance)
        {
            this.minRoamDistance = minRoamDistance;
            this.maxRoamDistance = Mathf.Max(maxRoamDistance, minRoamDistance);
        }

        public void ResetRoamTarget()
        {
            hasRoamTarget = false;
        }

        //Roaming is done with our own far away waypoints instead of the vanilla search routine, which always picks the
        //closest unsearched node and makes the Stegosaurus zigzag. Called every AI interval while roaming.
        public void UpdateRoaming()
        {
            if (hasRoamTarget)
            {
                Vector3 toTarget = roamTarget - stegoAI.transform.position;
                toTarget.y = 0;
                if (toTarget.magnitude < RoamArriveDistance || Time.time - roamStartTime > RoamTimeout)
                {
                    hasRoamTarget = false;
                }
            }
            if (!hasRoamTarget)
            {
                PickRoamTarget();
            }
        }

        //Picks a random node that is far away and roughly in front of the Stegosaurus, so it walks in long lines.
        //Nodes near one of the last few roam points are skipped, unless there is nothing else.
        private void PickRoamTarget()
        {
            List<Vector3> candidates = GetRoamCandidates(avoidRecentPoints: true);
            if (candidates.Count == 0)
            {
                candidates = GetRoamCandidates(avoidRecentPoints: false);
            }

            Vector3 position = stegoAI.transform.position;
            for (int i = 0; i < candidates.Count && i < MaxRoamPathAttempts; i++)
            {
                if (stegoAI.SetDestinationToPosition(candidates[i], checkForPath: true))
                {
                    roamTarget = candidates[i];
                    roamStartTime = Time.time;
                    hasRoamTarget = true;
                    RememberRoamPoint(roamTarget);
                    LogIfDebugBuild($"JPOGStegosaurus: new roam target {Vector3.Distance(position, roamTarget):F0}m away");
                    return;
                }
            }
        }

        //Best candidates first, each group in random order
        private List<Vector3> GetRoamCandidates(bool avoidRecentPoints)
        {
            Vector3 position = stegoAI.transform.position;
            Vector3 forward = stegoAI.transform.forward;
            forward.y = 0;

            var ahead = new List<Vector3>();
            var anyDirection = new List<Vector3>();
            var nearby = new List<Vector3>();
            foreach (GameObject node in stegoAI.allAINodes)
            {
                if (node == null || (avoidRecentPoints && IsNearRecentRoamPoint(node.transform.position)))
                {
                    continue;
                }
                Vector3 toNode = node.transform.position - position;
                toNode.y = 0;
                float distance = toNode.magnitude;
                if (distance >= minRoamDistance && distance <= maxRoamDistance)
                {
                    (Vector3.Angle(forward, toNode) <= MaxRoamTurnAngle ? ahead : anyDirection).Add(node.transform.position);
                }
                else if (distance >= MinFallbackRoamDistance && distance < minRoamDistance)
                {
                    nearby.Add(node.transform.position);
                }
            }

            var candidates = new List<Vector3>();
            foreach (List<Vector3> group in new[] { ahead, anyDirection, nearby })
            {
                Shuffle(group);
                candidates.AddRange(group);
            }
            return candidates;
        }

        private bool IsNearRecentRoamPoint(Vector3 point)
        {
            foreach (Vector3 recent in recentRoamPoints)
            {
                if (Vector3.Distance(point, recent) < RememberedRoamPointRadius)
                {
                    return true;
                }
            }
            return false;
        }

        //The amount of points that is remembered differs per pick, so the route doesn't become a fixed loop
        private void RememberRoamPoint(Vector3 point)
        {
            recentRoamPoints.Add(point);
            int rememberCount = stegoAI.enemyRandom.Next(MinRememberedRoamPoints, MaxRememberedRoamPoints + 1);
            while (recentRoamPoints.Count > rememberCount)
            {
                recentRoamPoints.RemoveAt(0);
            }
        }

        private void Shuffle(List<Vector3> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = stegoAI.enemyRandom.Next(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        //Called every frame on the owner
        public void Update()
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer >= StuckCheckInterval)
            {
                stuckTimer = 0f;
                CheckIfStuck();
            }
            TurnTowardsTarget();
        }

        //Only roaming picks its own destination, so only pick a new one there
        private void CheckIfStuck()
        {
            Vector3 currentPosition = stegoAI.transform.position;
            if (stegoAI.currentBehaviourStateIndex == (int)StegoState.Roaming && !stegoAI.IsStunned
                && Vector3.Distance(currentPosition, lastPosition) < StuckDistance)
            {
                LogIfDebugBuild("JPOGStegosaurus: Not moving, navigating to a new position.");
                ResetRoamTarget();
            }
            lastPosition = currentPosition;
        }

        private void TurnTowardsTarget()
        {
            var target = stegoAI.targetPlayer;
            int state = stegoAI.currentBehaviourStateIndex;
            if (target == null || target.isPlayerDead
                || (state != (int)StegoState.AttackEnemy && state != (int)StegoState.ChasingTarget && state != (int)StegoState.Stunned && state != (int)StegoState.SpecialAttack))
            {
                return;
            }
            Vector3 cameraPosition = target.gameplayCamera.transform.position;
            Vector3 directionToTarget = cameraPosition - stegoAI.transform.position;
            directionToTarget.y = 0;
            if (directionToTarget.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
                stegoAI.transform.rotation = Quaternion.Lerp(stegoAI.transform.rotation, targetRotation, TurnSpeed * Time.deltaTime);
            }
            stegoAI.turnCompass.LookAt(cameraPosition);
        }

        public void ApplyStateMovement(StegoState state)
        {
            float speed = state switch
            {
                StegoState.Roaming => WalkSpeed,
                StegoState.AttackEnemy => WalkSpeed,
                StegoState.ChasingTarget => RunSpeed,
                StegoState.RunningAway => RunSpeed,
                _ => 0f // Stunned, SpecialAttack, Idling
            };
            SetSpeed(speed);
        }

        public void SetSpeed(float speed)
        {
            stegoAI.agent.speed = speed;
            if (speed == 0)
            {
                //Coming from a chase we have to stop the run animation instead of the walk animation
                bool wasRunning = stegoAI.previousBehaviourStateIndex == (int)StegoState.ChasingTarget || stegoAI.currentBehaviourStateIndex == (int)StegoState.ChasingTarget;
                stegoAI.DoAnimationClientRpc(wasRunning ? "stopRun" : "stopWalk");
            }
            else
            {
                stegoAI.DoAnimationClientRpc(speed > WalkSpeed ? "startRun" : "startWalk");
            }
        }
    }
}
