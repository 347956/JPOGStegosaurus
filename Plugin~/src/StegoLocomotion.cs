using UnityEngine;

namespace JPOGStegosaurus
{
    internal sealed class StegoLocomotion
    {
        private const float TurnBlendTime = 0.12f;

        private static readonly int Idle = Animator.StringToHash("IdleWalking");
        private static readonly int Walk = Animator.StringToHash("WalkingAnimation");
        private static readonly int Run = Animator.StringToHash("RunningAnimation");
        private static readonly int BodyIdle = Animator.StringToHash("Empty");
        private static readonly int TailIdle = Animator.StringToHash("TailDefault");
        private static readonly int HeadIdle = Animator.StringToHash("New State");
        private static readonly int TurnAmount = Animator.StringToHash("stegoTurn");
        private static readonly int TurnSpeed = Animator.StringToHash("stegoTurnSpeed");

        private readonly Animator animator;
        private readonly int body;
        private readonly int gait;
        private readonly int tail;
        private readonly int head;
        private readonly int turns;

        public StegoLocomotion(Animator animator)
        {
            this.animator = animator;
            body = animator.GetLayerIndex("MainBodyLayer");
            gait = animator.GetLayerIndex("WalkingLayer");
            tail = animator.GetLayerIndex("TailLayer");
            head = animator.GetLayerIndex("HeadLayer");
            turns = animator.GetLayerIndex("Body Turns");
        }

        public float SignedTurn { get; private set; }

        public static float MeasureTurn(float yawDelta, float deltaTime)
        {
            // Unity's positive yaw turns right. Ignore teleports and rotation resets.
            if (deltaTime <= 0f || Mathf.Abs(yawDelta) >= 45f)
                return 0f;

            float yawRate = yawDelta / deltaTime;
            return Mathf.Sign(yawRate) * Mathf.InverseLerp(8f, 120f, Mathf.Abs(yawRate));
        }

        public bool TryGetGait(out AnimatorStateInfo state)
        {
            state = default;
            if (!CanReadGait() || animator.IsInTransition(gait))
                return false;

            state = animator.GetCurrentAnimatorStateInfo(gait);
            return state.shortNameHash == Walk || state.shortNameHash == Run || state.shortNameHash == Idle;
        }

        private bool CanReadGait() => animator.isActiveAndEnabled && gait >= 0 &&
            !LayerOwnsPose(body, BodyIdle) && !LayerOwnsPose(tail, TailIdle) && !LayerOwnsPose(head, HeadIdle);

        private static bool IsMoving(AnimatorStateInfo state) => state.shortNameHash == Walk || state.shortNameHash == Run;

        public static bool IsRunning(AnimatorStateInfo state) => state.shortNameHash == Run;
        public static bool IsStanding(AnimatorStateInfo state) => state.shortNameHash == Idle;

        private bool LayerOwnsPose(int layer, int idle)
        {
            if (layer < 0 || (layer != body && animator.GetLayerWeight(layer) <= 0f))
                return false;

            return animator.GetCurrentAnimatorStateInfo(layer).shortNameHash != idle ||
                (animator.IsInTransition(layer) && animator.GetNextAnimatorStateInfo(layer).shortNameHash != idle);
        }

        public void UpdateTurns(float turn, float speed, bool suspended, float deltaTime)
        {
            if (turns < 0 || !animator.isActiveAndEnabled)
                return;

            if (suspended || !CanReadGait())
            {
                SuspendTurns();
                return;
            }

            // Follow the outgoing stride through a crossfade. From idle, use the incoming stride.
            var state = animator.GetCurrentAnimatorStateInfo(gait);
            if (!IsMoving(state) && animator.IsInTransition(gait))
                state = animator.GetNextAnimatorStateInfo(gait);

            bool moving = IsMoving(state);
            float response = 1f - Mathf.Exp(-13f * deltaTime);
            SignedTurn = Mathf.Lerp(SignedTurn, moving ? turn : 0f, response);
            animator.SetFloat(TurnAmount, SignedTurn);
            animator.SetFloat(TurnSpeed, speed, TurnBlendTime, deltaTime);
            float weight = moving ? Mathf.InverseLerp(0.15f, 0.8f, speed) : 0f;
            animator.SetLayerWeight(turns, Mathf.MoveTowards(animator.GetLayerWeight(turns), weight, deltaTime / TurnBlendTime));

            if (!moving)
                return;

            // The local pose layer has a stationary clock. Advance it to the exact same normalized
            // time as the walking layer will reach on this evaluation, including its playback rate.
            // Hash 0 adjusts the current state's time without entering it again.
            float phase = state.normalizedTime + deltaTime * animator.speed * state.speed * state.speedMultiplier / state.length;
            animator.Play(0, turns, Mathf.Repeat(phase, 1f));
        }

        public void SuspendTurns()
        {
            SignedTurn = 0f;
            if (turns >= 0 && animator.isActiveAndEnabled && animator.GetLayerWeight(turns) != 0f)
                animator.SetLayerWeight(turns, 0f);
        }
    }
}
