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

        private readonly JPOGStegosaurusAI stegoAI;
        private float stuckTimer;
        private Vector3 lastPosition;

        public StegoMovementHelper(JPOGStegosaurusAI stegoAI)
        {
            this.stegoAI = stegoAI;
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
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

        //Roaming is the only state that depends on the search routine, so only restart it there
        private void CheckIfStuck()
        {
            Vector3 currentPosition = stegoAI.transform.position;
            if (stegoAI.currentBehaviourStateIndex == (int)StegoState.Roaming && !stegoAI.IsStunned
                && Vector3.Distance(currentPosition, lastPosition) < StuckDistance)
            {
                LogIfDebugBuild("JPOGStegosaurus: Not moving, navigating to a new position.");
                stegoAI.RestartSearch();
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
