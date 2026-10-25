using System;
using System.Diagnostics;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace JPOGStegosaurus {

    // The order has to match the behaviour states on the enemy prefab in Unity.
    internal enum StegoState {
        Roaming,
        AttackEnemy, // Currently unused
        ChasingTarget,
        RunningAway, // Currently unused
        Stunned,
        SpecialAttack,
        Idling // Work in progress, nothing switches to it yet
    }

    // You may be wondering, how does the JPOGStegosaurus know it is from class JPOGStegosaurusAI?
    // Well, we give it a reference to to this class in the Unity project where we make the asset bundle.
    // Asset bundles cannot contain scripts, so our script lives here. It is important to get the
    // reference right, or else it will not find this file. See the guide for more information.

    // The behaviour is split over helper classes. All RPCs stay in this class, because they have to be on the NetworkBehaviour.
    class JPOGStegosaurusAI : EnemyAI, IVisibleThreat
    {
        // We set these in our Asset Bundle, so we can disable warning CS0649:
        // Field 'field' is never assigned to, and will always have its default value 'value'
#pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform aggroArea = null!;
        public AudioSource tailSFX = null!;

        public Transform tailSpike1 = null!;
        public Transform tailSpike2 = null!;
        public Transform tailSpike3 = null!;
        public Transform tailSpike4 = null!;
        public Transform tailHitBox = null!;
        public Transform stompHitbox = null!;

        public Transform attackAreaFront = null!;
        public Transform attackAreaBack = null!;
#pragma warning restore 0649

        internal StegoIrritationHelper Irritation { get; private set; } = null!;
        internal StegoMovementHelper Movement { get; private set; } = null!;
        internal StegoAreaHelper Areas { get; private set; } = null!;
        internal StegoAttackHelper Attacks { get; private set; } = null!;
        internal StegoBodyHelper Bodies { get; private set; } = null!;
        internal StegoIdleHelper Idle { get; private set; } = null!;

        internal System.Random enemyRandom = null!;

        private float timeSinceHittingLocalPlayer;
        private bool isDeadAnimationDone;
        private bool inStunAnimation;
        private int handledStateIndex = -1;

        internal bool IsStunned => stunNormalizedTimer > 0f;

        ThreatType IVisibleThreat.type => ThreatType.EyelessDog;

        int IVisibleThreat.SendSpecialBehaviour(int id)
        {
            return 0;
        }

        int IVisibleThreat.GetThreatLevel(Vector3 seenByPosition)
        {
            int threatLevel = enemyHP >= 2 ? 5 : 3;
            if (currentBehaviourStateIndex == (int)StegoState.ChasingTarget)
            {
                threatLevel += 3;
            }
            return threatLevel;
        }

        int IVisibleThreat.GetInterestLevel()
        {
            return 0;
        }

        Transform IVisibleThreat.GetThreatLookTransform()
        {
            return eye;
        }

        Transform IVisibleThreat.GetThreatTransform()
        {
            return transform;
        }

        Vector3 IVisibleThreat.GetThreatVelocity()
        {
            if (IsOwner)
            {
                return agent.velocity;
            }
            return Vector3.zero;
        }

        float IVisibleThreat.GetVisibility()
        {
            if (isEnemyDead)
            {
                return 0f;
            }
            if (currentBehaviourStateIndex == (int)StegoState.ChasingTarget)
            {
                return 1f;
            }
            return 0.75f;
        }

        public GrabbableObject GetHeldObject()
        {
            return null!;
        }

        public bool IsThreatDead()
        {
            return isEnemyDead || enemyHP <= 0;
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text) {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start() {
            CreateHelpers();
            base.Start();
            LogIfDebugBuild("JPOGStegosaurus Spawned");
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            if (IsOwner)
            {
                Areas.Start();
            }
            // We make the enemy start searching. This will make it start wandering around.
            StartSearch(transform.position);
        }

        private void CreateHelpers()
        {
            Irritation = new StegoIrritationHelper(this);
            Movement = new StegoMovementHelper(this);
            Areas = new StegoAreaHelper(this);
            Attacks = new StegoAttackHelper(this);
            Bodies = new StegoBodyHelper(this);
            Idle = new StegoIdleHelper(this);

            var config = Plugin.BoundConfig;
            Irritation.Configure(config.MaxIrritationLevel.Value,
                config.IncreaseAmountIrritation.Value, config.IntervalIrrtationIncrement.Value,
                config.DecreaseAmountIrritation.Value, config.IntervalIrrtationDecrement.Value);
        }

        public override void Update() {
            base.Update();
            if (isEnemyDead)
            {
                // KillEnemy plays the death sound, but we also want the enemy to stop making any other sound.
                if (!isDeadAnimationDone)
                {
                    isDeadAnimationDone = true;
                    creatureVoice.Stop();
                    creatureVoice.PlayOneShot(dieSFX);
                }
                return;
            }

            timeSinceHittingLocalPlayer += Time.deltaTime;

            if (IsOwner)
            {
                Movement.Update();
            }
        }

        public override void DoAIInterval() {
            base.DoAIInterval();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) {
                return;
            }

            if (handledStateIndex != currentBehaviourStateIndex)
            {
                handledStateIndex = currentBehaviourStateIndex;
                OnStateEntered((StegoState)handledStateIndex);
            }

            switch ((StegoState)currentBehaviourStateIndex) {
                case StegoState.Roaming:
                    if (Irritation.IsMax)
                    {
                        SwitchToBehaviourState((int)StegoState.ChasingTarget);
                    }
                    break;

                case StegoState.ChasingTarget:
                    DoChasingState();
                    break;

                case StegoState.Idling:
                    DoIdlingState();
                    break;

                case StegoState.Stunned:
                    DoStunnedState();
                    break;

                case StegoState.SpecialAttack:
                    DoSpecialAttackState();
                    break;

                // AttackEnemy and RunningAway are not used
            }
        }

        // Runs on the owner the first time the AI interval sees a new state
        private void OnStateEntered(StegoState state)
        {
            LogIfDebugBuild($"JPOGStegosaurus: Entered state [{state}]");
            Movement.ApplyStateMovement(state);
            if (state != StegoState.Idling)
            {
                Idle.Reset();
            }

            switch (state)
            {
                case StegoState.Roaming:
                    movingTowardsTargetPlayer = false;
                    if (currentSearch == null || !currentSearch.inProgress)
                    {
                        StartSearch(transform.position);
                    }
                    break;
                case StegoState.ChasingTarget:
                    // We steer the agent ourselves while chasing, so the search routine must not set destinations
                    StopSearch(currentSearch);
                    break;
                case StegoState.SpecialAttack:
                    Attacks.ResetSpecialAttack();
                    break;
            }
        }

        private void DoChasingState()
        {
            if (!ChasePlayer())
            {
                Irritation.SetPercent(20);
                SwitchToBehaviourState((int)StegoState.Roaming);
                return;
            }

            if (Irritation.Level >= Irritation.StopChaseThreshold)
            {
                Irritation.TryDecrease();
            }
            else
            {
                LogIfDebugBuild("JPOGStegosaurus: Irritation level below the chase threshold, stopping chase");
                SwitchToBehaviourState((int)StegoState.Roaming);
            }
        }

        // Keeps the current target if it is still valid, otherwise looks for a new one. Returns false if there is no target.
        private bool ChasePlayer()
        {
            if (targetPlayer != null && !CanAttackPlayer(targetPlayer))
            {
                LogIfDebugBuild($"Current target player {targetPlayer.name} is no longer targetable.");
                targetPlayer = null;
            }

            if (targetPlayer == null)
            {
                bool foundNewTarget = TargetClosestPlayer(1.5f, true, 100f);
                if (!foundNewTarget || targetPlayer == null || !CanAttackPlayer(targetPlayer))
                {
                    targetPlayer = null;
                    LogIfDebugBuild("No valid target found.");
                    return false;
                }
                LogIfDebugBuild($"New target found: {targetPlayer.name}. Chasing.");
            }

            SetDestinationToPosition(targetPlayer.transform.position);
            return true;
        }

        private void DoIdlingState()
        {
            if (!Idle.IsPlaying && !Idle.IsDone)
            {
                Idle.PlayRandom();
            }
            if (Idle.IsDone)
            {
                Idle.Reset();
                SwitchToBehaviourState((int)(Irritation.IsMax ? StegoState.ChasingTarget : StegoState.Roaming));
            }
        }

        private void DoStunnedState()
        {
            if (!IsStunned)
            {
                if (targetPlayer != null)
                {
                    SwitchToBehaviourState((int)StegoState.ChasingTarget);
                }
                else
                {
                    Irritation.SetPercent(50);
                    SwitchToBehaviourState((int)StegoState.Roaming);
                }
            }
            else if (!inStunAnimation)
            {
                inStunAnimation = true;
                StartCoroutine(StunAnimationRoutine());
            }
        }

        private System.Collections.IEnumerator StunAnimationRoutine()
        {
            DoAnimationClientRpc("stunEnemy");
            yield return new WaitForSeconds(2.1f);
            inStunAnimation = false;
        }

        private void DoSpecialAttackState()
        {
            if (!Attacks.SpecialAttackStarted)
            {
                // The tail can only reach a target that is close, otherwise just chase it
                if (!Attacks.IsTargetInSpecialAttackRange())
                {
                    SwitchToBehaviourState((int)StegoState.ChasingTarget);
                    return;
                }
                Attacks.StartSpecialTailAttack();
                return;
            }

            if (Attacks.SpecialAttackFinished)
            {
                if (targetPlayer != null && CanAttackPlayer(targetPlayer))
                {
                    SwitchToBehaviourState((int)StegoState.ChasingTarget);
                }
                else
                {
                    Irritation.SetPercent(40);
                    SwitchToBehaviourState((int)StegoState.Roaming);
                }
            }
        }

        internal void RestartSearch()
        {
            StopSearch(currentSearch);
            StartSearch(transform.position);
        }

        // Players in the ship, on a ladder, in a vehicle or dead cannot be attacked
        internal bool CanAttackPlayer(PlayerControllerB? player)
        {
            return player != null && !(player.isInHangarShipRoom || player.isClimbingLadder || player.isPlayerDead || player.inVehicleAnimation);
        }

        // Everything vanilla uses to find or hit players (TargetClosestPlayer, MeetsStandardPlayerCollisionConditions)
        // goes through this, so players in the ship or in a cruiser seat are never targeted or hit.
        public override bool PlayerIsTargetable(PlayerControllerB playerScript, bool cannotBeInShip = false, bool overrideInsideFactoryCheck = false, bool checkForMineshaftStartTile = true)
        {
            return CanAttackPlayer(playerScript) && base.PlayerIsTargetable(playerScript, cannotBeInShip, overrideInsideFactoryCheck, checkForMineshaftStartTile);
        }

        public override void OnCollideWithPlayer(Collider other) {
            if (timeSinceHittingLocalPlayer < 1f) {
                return;
            }
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null)
            {
                LogIfDebugBuild("JPOGStegosaurus: Collision with Player!");
                timeSinceHittingLocalPlayer = 0f;
                playerControllerB.DamagePlayer(20);
            }
        }

        public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy)
        {
            if (isEnemyDead || !IsOwner || collidedEnemy == null || collidedEnemy.isEnemyDead || !collidedEnemy.enemyType.canDie)
            {
                return;
            }
            base.OnCollideWithEnemy(other, collidedEnemy);
            if (collidedEnemy.enemyType != enemyType)
            {
                LogIfDebugBuild($"JPOGStegosaurus: Collision with [{collidedEnemy.enemyType.enemyName}]!");
                Attacks.StartStompAttack(collidedEnemy);
            }
        }

        // Runs on every client
        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1) {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead) {
                return;
            }
            enemyHP -= force;
            if (!IsOwner) {
                return;
            }

            if (enemyHP <= 0) {
                // Our death sound will be played through creatureVoice when KillEnemy() is called.
                // KillEnemy() will also attempt to call creatureAnimator.SetTrigger("KillEnemy"),
                // so we don't need to call a death animation ourselves.
                Areas.Stop();
                Attacks.StopAll();
                // We need to stop our search coroutine, because the game does not do that by default.
                StopSearch(currentSearch);
                Movement.SetSpeed(0f);
                DoAnimationClientRpc("stopTail");
                KillEnemyOnOwnerClient();
            }
            else if (playerWhoHit != null && currentBehaviourStateIndex != (int)StegoState.ChasingTarget)
            {
                Irritation.SetMax();
                targetPlayer = playerWhoHit;
                SwitchToBehaviourState((int)StegoState.SpecialAttack);
            }
        }

        // Stuns are applied on the client that used the stun item, so the owner has to be told about it
        public override void SetEnemyStunned(bool setToStunned, float setToStunTime = 1, PlayerControllerB? setStunnedByPlayer = null)
        {
            base.SetEnemyStunned(setToStunned, setToStunTime, setStunnedByPlayer);
            if (setToStunned && stunNormalizedTimer > 0f)
            {
                StunnedServerRpc(setStunnedByPlayer != null ? (int)setStunnedByPlayer.playerClientId : -1, setToStunTime);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void StunnedServerRpc(int stunnedByPlayerId, float stunTime)
        {
            if (isEnemyDead)
            {
                return;
            }
            if (stunNormalizedTimer <= 0f)
            {
                stunNormalizedTimer = stunTime;
            }
            if (stunnedByPlayerId >= 0)
            {
                Irritation.SetMax();
                targetPlayer = StartOfRound.Instance.allPlayerScripts[stunnedByPlayerId];
            }
            SwitchToBehaviourState((int)StegoState.Stunned);
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName) {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }

        // Every client checks its own local player, the owner drives the attack from the AttackHelper
        [ClientRpc]
        public void TailHitCheckClientRpc(int attackId)
        {
            Attacks.CheckLocalPlayerHit(StegoAttackType.Tail, attackId);
        }

        [ClientRpc]
        public void StompHitCheckClientRpc(int attackId)
        {
            Attacks.CheckLocalPlayerHit(StegoAttackType.Stomp, attackId);
        }

        // Sent by the client of a killed player once the body exists
        [ServerRpc(RequireOwnership = false)]
        public void PlayerKilledServerRpc(int playerId, bool stabbed)
        {
            Bodies.OnPlayerKilled(playerId, stabbed);
        }

        [ClientRpc]
        public void PinBodyToSpikeClientRpc(int playerId, int spikeIndex)
        {
            Bodies.PinBodyToSpike(playerId, spikeIndex);
        }

        // The methods below are called by animation events on the model. Keep the names in sync with the animation clips.
        private void PlayAudioClip(AudioClip audioClip)
        {
            DoAnimationClientRpc("roar");
            creatureVoice.PlayOneShot(audioClip);
            WalkieTalkie.TransmitOneShotAudio(creatureVoice, audioClip);
        }

        private void PlayAudioSFX(AudioClip audioClip)
        {
            creatureSFX.PlayOneShot(audioClip);
        }

        private void PlayTailSFX(AudioClip audioClip)
        {
            tailSFX.PlayOneShot(audioClip);
        }
    }
}
