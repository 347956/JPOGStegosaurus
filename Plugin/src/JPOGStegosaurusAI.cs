using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using GameNetcodeStuff;
using JPOGStegosaurus.Configuration;
using Unity.Netcode;
using UnityEngine;

namespace JPOGStegosaurus {

    // You may be wondering, how does the JPOGStegosaurus know it is from class JPOGStegosaurusAI?
    // Well, we give it a reference to to this class in the Unity project where we make the asset bundle.
    // Asset bundles cannot contain scripts, so our script lives here. It is important to get the
    // reference right, or else it will not find this file. See the guide for more information.

    class JPOGStegosaurusAI : EnemyAI, IVisibleThreat
    {
        // We set these in our Asset Bundle, so we can disable warning CS0649:
        // Field 'field' is never assigned to, and will always have its default value 'value'
#pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform aggroArea = null!;
        private List<DeadBodyInfo> spikedBodies = new List<DeadBodyInfo>();
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
        float timeSinceHittingLocalPlayer;
        float timeSinceNewRandPos;
        Vector3 positionRandomness;
        Vector3 StalkPos;
        System.Random enemyRandom = null!;
        bool isDeadAnimationDone;

        float irritationLevel = 0f;
        float irritationMaxLevel;
        float irritationIncrementAmount;
        float irritationDecrementAmount;
        float irritationDecrementinterval;
        float lastIrritationDecreaseTime;
        private State previousState = State.Idling;
        private bool inRandomIdleAnimation = false;
        private bool isDoneIdling = false;
        private float timeToIdle = 30f;
        private bool inTailAttack;
        private List<int> tailHitPlayerIds = new List<int>();
        private List<int> stompHitPlayerIds = new List<int>();
        private float lastIrritationIncrementTime;
        private float irritationIncrementInterval;
        private bool inStompAttack;
        private float roamingStartTime;
        private float movementCheckInterval = 5.0f; // Interval in seconds to check movement
        private Vector3 lastPosition;
        private float movingThreshold = 0.1f; // Threshold distance to consider as movement
        private bool inStunAnimation = false;
        private bool inSpecialTailAttack;
        private float stopChaseHeight = 7f;
        private bool isStunned = false;
        private bool readyToChaseFromSpecialAttack = false;
        private Quaternion originalRotation;
        private bool readyToChaseFromStunned;
        private bool specialAttackCanHitPlayer = false;
        private bool specialAttackHasHitPlayer = false;
        private EnemyAI? targetEntiy;

        ThreatType IVisibleThreat.type => ThreatType.EyelessDog;

        int IVisibleThreat.SendSpecialBehaviour(int id)
        {
            return 0;
        }

        int IVisibleThreat.GetThreatLevel(Vector3 seenByPosition)
        {
            int num = 0;
            num = ((enemyHP >= 2) ? 5 : 3);
            if (creatureAnimator.GetBool("StartedChase"))
            {
                num += 3;
            }
            return num;
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
            return base.transform;
        }

        Vector3 IVisibleThreat.GetThreatVelocity()
        {
            if (base.IsOwner)
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
            if (creatureAnimator.GetBool("StartedChase"))
            {
                return 1f;
            }
            return 0.75f;
        }

        enum State {
            Roaming,
            AttackEnemy,
            ChasingTarget,
            RunningAway,
            Stunned,
            SpecialAttack,
            Idling
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text) {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start() {
            AssignConfigVariables();
            LogIfDebugBuild("JPOGStegosaurus Spawned");
            base.Start();
            StartCoroutine(checkAggroAndAttackAreas());
            timeSinceHittingLocalPlayer = 0;
            timeSinceNewRandPos = 0;
            positionRandomness = new Vector3(0, 0, 0);
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            isDeadAnimationDone = false;
            // NOTE: Add your behavior states in your enemy script in Unity, where you can configure fun stuff
            // like a voice clip or an sfx clip to play when changing to that specific behavior state.
            SwitchToBehaviourServerRpc((int)State.Roaming);
            // We make the enemy start searching. This will make it start wandering around.

            StartSearch(transform.position);
        }

        public override void Update() {
            base.Update();
            if (isEnemyDead)
            {
                // For some weird reason I can't get an RPC to get called from HitEnemy() (works from other methods), so we do this workaround. We just want the enemy to stop playing the song.
                if (!isDeadAnimationDone)
                {
                    LogIfDebugBuild("Stopping enemy voice with janky code.");
                    isDeadAnimationDone = true;
                    creatureVoice.Stop();
                    creatureVoice.PlayOneShot(dieSFX);
                }
                return;
            }
            if (enemyHP <= 0 && !isEnemyDead)
            {
                return;
            }

            timeSinceHittingLocalPlayer += Time.deltaTime;
            timeSinceNewRandPos += Time.deltaTime;


            var state = currentBehaviourStateIndex;
            if (timeSinceNewRandPos >= movementCheckInterval && !inStunAnimation)
            {
                timeSinceNewRandPos = 0f;
                CheckMovementServerRpc();
            }
            if (targetPlayer != null && (state == (int)State.AttackEnemy || state == (int)State.ChasingTarget || state == (int)State.Stunned || state == (int)State.SpecialAttack)) {
                LookAtTargetServerRpc();
            }
            if (stunNormalizedTimer > 0f)
            {
                isStunned = true;
            }
            else {
                isStunned = false;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void PlayStunnedAnimationServerRpc()
        {
            inStunAnimation = true;
            StartCoroutine(BeginStunAnimation());
        }

        IEnumerator BeginStunAnimation()
        {
            DoAnimationClientRpc("stunEnemy");
            yield return new WaitForSeconds(2.1f);
            inStunAnimation = false;
            yield break;
        }

        [ServerRpc(RequireOwnership = false)]
        private void CheckMovementServerRpc()
        {
            Vector3 currentPosition = transform.position;
            if (Vector3.Distance(currentPosition, lastPosition) < movingThreshold)
            {
                LogIfDebugBuild("JPOGStegosaurus: Not moving, navigating to a new position.");
                StopSearch(currentSearch);
                StartSearch(currentPosition);
            }
            lastPosition = currentPosition;
        }

        public override void DoAIInterval() {

            base.DoAIInterval();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) {
                return;
            };

            switch (currentBehaviourStateIndex) {
                case (int)State.Roaming:
                    StateChangeHelperServerRpc(State.Roaming);
                    if (irritationLevel == irritationMaxLevel)
                    {
                        SwitchToBehaviourServerRpc((int)State.ChasingTarget);
                        break;
                    }
                    //CheckIdleTimeServerRpc(); //WIP

                    /*                    if (FoundClosestPlayerInRange(25f, 3f)){
                                            LogIfDebugBuild("Start Target Player");
                                            StopSearch(currentSearch);
                                            SwitchToBehaviourClientRpc((int)State.AttackEnemy);
                                        }*/
                    break;

                case (int)State.AttackEnemy:
                    StateChangeHelperServerRpc(State.AttackEnemy);
                    // Keep targeting closest player, unless they are over 20 units away and we can't see them.
                    if (!TargetClosestPlayerInAnyCase() || (Vector3.Distance(transform.position, targetPlayer.transform.position) > 20 && !CheckLineOfSightForPosition(targetPlayer.transform.position))) {
                        LogIfDebugBuild("JPOGStegosaurus: Stop Target Player");
                        StartSearch(transform.position);
                        SwitchToBehaviourClientRpc((int)State.Roaming);
                        return;
                    }
                    //StickingInFrontOfPlayer();
                    break;

                case (int)State.ChasingTarget:
                    StateChangeHelperServerRpc(State.ChasingTarget);
                    ChasePlayerServerRpc();
                    if(targetPlayer == null)
                    {
                        irritationLevel = irritationMaxLevel / 100 * 20;
                        SwitchToBehaviourClientRpc((int)State.Roaming);
                        break;
                    }
                    if (irritationLevel >= 20)
                    {
                        DecreaseIrritationServerRpc();
                        break;
                    }
                    else if (irritationLevel < irritationMaxLevel / 100 * 20)
                    {
                        LogIfDebugBuild("JPOGStegosaurus: Irritation Level below 20, stopping chase");
                        SwitchToBehaviourClientRpc((int)State.Roaming);
                        break;
                    }
                    break;

                case (int)State.RunningAway:
                    StateChangeHelperServerRpc(State.RunningAway);
                    // We don't care about doing anything here
                    break;

                case (int)State.Idling:
                    StateChangeHelperServerRpc(State.Idling);
                    if (!inRandomIdleAnimation)
                    {
                        LogIfDebugBuild("JPOGStegosaurus: playing random idle animation");
                        PlayRandomIdleAnimationClientRpc();
                    }
                    if (isDoneIdling)
                    {
                        LogIfDebugBuild("JPOGStegosaurus: done playing random idle animation");
                        isDoneIdling = false; //Reseting isDoneIdling for the next time the Stegosaurus enters this behaviour state
                        if (irritationLevel == irritationMaxLevel)
                        {
                            SwitchToBehaviourServerRpc((int)State.ChasingTarget);
                        }
                        else
                        {
                            SwitchToBehaviourServerRpc((int)State.Roaming);
                        }
                    }
                    break;

                case (int)State.Stunned:
                    StateChangeHelperServerRpc(State.Stunned);
                    if (!isStunned)
                    {
                        LogIfDebugBuild("JPOGStegosaurus: Is No longer stunned attempting to chase the target!");
                        if (targetPlayer != null)
                        {
                            SwitchToBehaviourServerRpc((int)State.ChasingTarget);
                            break;
                        }
                        else
                        {
                            irritationLevel = irritationMaxLevel / 100 * 50;
                            SwitchToBehaviourServerRpc((int)State.Roaming);
                            break;
                        }
                    }
                    if (isStunned && !inStunAnimation)
                    {
                        LogIfDebugBuild("JPOGStegosaurus: Is stunned and not in a stun animation, begining stun animation Coroutine");
                        PlayStunnedAnimationServerRpc();
                    }
                    break;

                case (int)State.SpecialAttack:
                    StateChangeHelperServerRpc(State.SpecialAttack);
                    //The stegosaurus should enter this logic if an hostile entity is in range (<=10f) and when it is not already targetting a player.
/*                    if(targetEntiy != null && targetPlayer == null)
                    {
                        if (!CheckIfInAttackAnimation()) //Check if the Stegosaurus is not already in an animation.
                        {
                            PerformSpecialTailAttackServerRpc();
                            break;
                        }
                        if (readyToChaseFromSpecialAttack)
                        {
                            readyToChaseFromSpecialAttack = false; //Reset ready to chase to it's original value.
                            targetEntiy = null; //Reset Target Entity
                            SwitchToBehaviourServerRpc((int)State.Roaming); //After attacking an enemy the Stegosaurus should return to it's roaming state.
                            break;
                        }
                    }*/
                    if (!specialAttackCanHitPlayer)
                    {
                        SwitchToBehaviourServerRpc((int)State.ChasingTarget);
                        break;
                    }

                    if (readyToChaseFromSpecialAttack)
                    {
                        LogIfDebugBuild($"JPOGStegosaurus: special attack is done = [{readyToChaseFromSpecialAttack}] checking if the target is still !null ");
                        if (specialAttackHasHitPlayer)
                        {

                            readyToChaseFromSpecialAttack = false;
                            specialAttackHasHitPlayer = false;
                            irritationLevel = irritationMaxLevel / 100 * 40;
                            specialAttackCanHitPlayer = false;
                            SwitchToBehaviourServerRpc((int)State.Roaming);
                            break;
                        }
                        else if(targetPlayer != null)
                        {
                            readyToChaseFromSpecialAttack = false;
                            specialAttackHasHitPlayer = false;
                            specialAttackCanHitPlayer = false;
                            SwitchToBehaviourServerRpc((int)State.ChasingTarget);
                            break;
                        }
                        else {
                            irritationLevel = irritationMaxLevel / 100 * 40;
                            specialAttackCanHitPlayer = false;
                            SwitchToBehaviourServerRpc((int)State.Roaming);
                            break;
                        }
/*                        if (targetPlayer != null)
                        {
                            readyToChaseFromSpecialAttack = false;
                            break;
                        }
                        else
                        {
                            readyToChaseFromSpecialAttack = false;
                            irritationLevel = irritationMaxLevel / 100 * 80;
                            SwitchToBehaviourServerRpc((int)State.Roaming);
                            break;
                        }*/
                    }
                    if (!CheckIfInAttackAnimation())
                    {
                        LogIfDebugBuild($"JPOGStegosaurus: special attack is done = [{readyToChaseFromSpecialAttack}] starting the special attack");
                        if (targetPlayer != null)
                        {
                            LookAtTargetServerRpc();
                            PerformSpecialTailAttackServerRpc();
                        }
                    }
                    break;

                default:
                    LogIfDebugBuild("JPOGStegosaurus: This Behavior State doesn't exist!");
                    break;
            }
        }
        [ServerRpc(RequireOwnership = false)]
        private void CheckIfSpecialAttackCanHitPlayerServerRpc()
        {
            CheckIfSpecialAttackCanHitPlayer();
        }

        private void CheckIfSpecialAttackCanHitPlayer()
        {
            if(targetPlayer == null)
            {
                return;
            }
            float distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.transform.position);
            if (distanceToPlayer <= 10f)
            {
                specialAttackCanHitPlayer = true;
            }
            else
            {
                specialAttackCanHitPlayer = false;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void ChasePlayerServerRpc()
        {
            bool foundNewTarget = false;

            if (targetPlayer != null)
            {
                if (!CheckIfPlayerIsTargetable(targetPlayer)) // Check if the current target player is targetable
                {
                    LogIfDebugBuild($"Current target player {targetPlayer.name} is no longer targetable.");
                    targetPlayer = null; // Invalidate the current target
                }
            }

            if (targetPlayer == null) // Check for a new target if no valid target exists
            {
                foundNewTarget = TargetClosestPlayer(1.5f, true, 100f);
                if (targetPlayer != null)
                {
                    if (foundNewTarget && CheckIfPlayerIsTargetable(targetPlayer))
                    {
                        LogIfDebugBuild($"New target found: {targetPlayer.name}. Chasing.");
                    }
                    else
                    {
                        LogIfDebugBuild($"New found target [{targetPlayer.name}] was not valid, setting targetPlayer to null");
                        targetPlayer = null; // Invalidate target if not targetable
                    }
                }
            }
            if (targetPlayer != null)
            {
                ChasePlayerClientRpc(targetPlayer.transform.position); // Notify clients to chase player
                //LogIfDebugBuild($"Chasing player: {targetPlayer.name} at position {targetPlayer.transform.position}");
            }
            else if (!foundNewTarget || targetPlayer == null)
            {
                irritationLevel = irritationMaxLevel / 100 * 20;
                LogIfDebugBuild("No valid target found.");
            }
        }

        [ClientRpc]
        private void ChasePlayerClientRpc(Vector3 targetPosition)
        {
            SetDestinationToPosition(targetPosition); // Set destination to the target player's position
        }

        private bool CheckIfPlayerIsTargetable(PlayerControllerB player)
        {
            bool playerIsTargetable = false;
            if (player != null)
            {
                if (player.isInHangarShipRoom || player.isClimbingLadder || player.isPlayerDead)
                {
                    LogIfDebugBuild($"JPOGStegosaurus: player[{player.actualClientId}] is not targetable");
                    playerIsTargetable = false;
                }
                else
                {
                    LogIfDebugBuild($"JPOGStegosaurus: player[{player.actualClientId}] is targetable");
                    playerIsTargetable = true;
                }
            }
            return playerIsTargetable;
        }

        private bool CheckHeightDifference()
        {
            bool playerCanBeReached = false;
            if (targetPlayer != null) {
                float heightDifference = Mathf.Abs(transform.position.y - targetPlayer.transform.position.y);
                if (heightDifference < stopChaseHeight)
                {
                    playerCanBeReached = true;
                }
                else
                {
                    LogIfDebugBuild($"JPOGStegosaurus: TargetPlayer is too high to reach!");
                }

            }
            return playerCanBeReached;
        }

        bool FoundClosestPlayerInRange(float range, float senseRange) {
            TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true);
            if (targetPlayer == null) {
                // Couldn't see a player, so we check if a player is in sensing distance instead
                TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }
            return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
        }

        bool TargetClosestPlayerInAnyCase() {
            mostOptimalDistance = 2000f;
            targetPlayer = null;
            for (int i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++)
            {
                tempDist = Vector3.Distance(transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position);
                if (tempDist < mostOptimalDistance)
                {
                    mostOptimalDistance = tempDist;
                    targetPlayer = StartOfRound.Instance.allPlayerScripts[i];
                }
            }
            if (targetPlayer == null) return false;
            return true;
        }

        void StickingInFrontOfPlayer() {
            // We only run this method for the host because I'm paranoid about randomness not syncing I guess
            // This is fine because the game does sync the position of the enemy.
            // Also the attack is a ClientRpc so it should always sync
            if (targetPlayer == null || !IsOwner) {
                return;
            }
            if (timeSinceNewRandPos > 0.7f) {
                timeSinceNewRandPos = 0;
                if (enemyRandom.Next(0, 5) == 0) {
                    // Attack
                    StartCoroutine(SwingAttack());
                }
                else {
                    // Go in front of player
                    positionRandomness = new Vector3(enemyRandom.Next(-2, 2), 0, enemyRandom.Next(-2, 2));
                    StalkPos = targetPlayer.transform.position - Vector3.Scale(new Vector3(-5, 0, -5), targetPlayer.transform.forward) + positionRandomness;
                }
                SetDestinationToPosition(StalkPos, checkForPath: false);
            }
        }


        IEnumerator checkAggroAndAttackAreas()
        {
            while (!isEnemyDead)
            {
                //LogIfDebugBuild($"JPOGStegosaurus: Checking Aggro Area for players");
                CheckForPlayersInAggroAreaServerRpc();
                //LogIfDebugBuild($"JPOGStegosaurus: Checking Back Attack Area for players");
                CheckForPlayersInAttackAreaBackServerRpc();
                //LogIfDebugBuild($"JPOGStegosaurus: Checking Front Attack Area for players");
                CheckForPlayersInAttackAreaFrontServerRpc();
                //LogIfDebugBuild($"JPOGStegosaurus: Checking Aggro Area for players");
                //CheckForEntitiesInAggroRangeServerRpc();
                yield return new WaitForSeconds(1.0f);
            }
            yield break;
        }

        [ServerRpc(RequireOwnership =false)]
        private void CheckForEntitiesInAggroRangeServerRpc()
        {
            CheckForEntitiesInAggroRangeClientRpc();
        }

        [ClientRpc]
        private void CheckForEntitiesInAggroRangeClientRpc()
        {
            CheckForEntitiesInAggroRange();
        }

        private void CheckForEntitiesInAggroRange()
        {
            //If a target enemy is already set, the StegoSaurus should not attempt to target a new enemy untill the target is dead/attack finished etc.
            if (targetEntiy != null) {
                return;
            }
            var state = currentBehaviourStateIndex;
            if (state == (int)State.Roaming)
            {
                foreach (EnemyAI entity in RoundManager.Instance.SpawnedEnemies)
                {

                    LogIfDebugBuild($"JPOGStegosaurus: own type = [{this.enemyType.name}] entity enemy type = [{entity.enemyType.name}]");
                    if (this.enemyType.name.Equals(entity.enemyType.name) || entity.isEnemyDead || !entity.enemyType.canDie)
                    {

                        LogIfDebugBuild($"JPOGStegosaurus: entity is a stegosaurus, already dead or cannot die");
                        continue;
                    }
                    float distanceToEntiy = Vector3.Distance(entity.transform.position, transform.position);
                    LogIfDebugBuild($"JPOGStegosaurus: [{entity.enemyType.name}] distance = [{distanceToEntiy}]");
                    if (distanceToEntiy <= 10f)
                    {
                        targetEntiy = entity;
                        LookAtTargetEntityServerRpc();
                        SwitchToBehaviourServerRpc((int)State.SpecialAttack);
                    }
                }
            }
        }
        [ServerRpc(RequireOwnership = false)]
        private void LookAtTargetEntityServerRpc()
        {
            LookAtTargetEntityClientRpc();
        }
        [ClientRpc]
        private void LookAtTargetEntityClientRpc()
        {
            LookAtTargetEntity();
        }
        private void LookAtTargetEntity()
        {
            if (targetEntiy != null)
            {
                turnCompass.LookAt(targetEntiy.transform.position);
            }
        }

        IEnumerator SwingAttack() {
            SwitchToBehaviourClientRpc((int)State.AttackEnemy);
            StalkPos = targetPlayer.transform.position;
            SetDestinationToPosition(StalkPos);
            yield return new WaitForSeconds(0.5f);
            if (isEnemyDead) {
                yield break;
            }
            DoAnimationClientRpc("swingAttack");
            yield return new WaitForSeconds(0.35f);
            SwingAttackHitClientRpc();
            // In case the player has already gone away, we just yield break (basically same as return, but for IEnumerator)
            if (currentBehaviourStateIndex != (int)State.AttackEnemy) {
                yield break;
            }
            SwitchToBehaviourClientRpc((int)State.AttackEnemy);
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
            if (isEnemyDead)
            {
                return;
            }
            if(collidedEnemy == null || collidedEnemy.isEnemyDead || !collidedEnemy.enemyType.canDie)
            {
                return;
            }
            base.OnCollideWithEnemy(other, collidedEnemy);
            if (collidedEnemy.enemyType != enemyType)
            {
                LogIfDebugBuild($"JPOGStegosaurus: Collision with [{collidedEnemy.enemyType.enemyName}]!");
                if (!inSpecialAnimation && !inStompAttack && !inSpecialTailAttack) //Should not stomp attack an enemyEntity if already in an attack animation
                {
                    targetEntiy = collidedEnemy;
                    LookAtTargetEntityServerRpc();
                    StartCoroutine(BeginStompAttack());
                }
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1) {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead) {
                return;
            }
            enemyHP -= force;
            if (IsOwner) {
                if (enemyHP <= 0 && !isEnemyDead) {
                    // Our death sound will be played through creatureVoice when KillEnemy() is called.
                    // KillEnemy() will also attempt to call creatureAnimator.SetTrigger("KillEnemy"),
                    // so we don't need to call a death animation ourselves.

                    StopCoroutine(checkAggroAndAttackAreas());
                    // We need to stop our search coroutine, because the game does not do that by default.
                    agent.speed = 0;
                    SetWalkingAnimtionClientRpc(agent.speed);
                    SetWalkingAnimtionServerRpc(agent.speed);
                    DoAnimationClientRpc("stopTail");
                    StopCoroutine(searchCoroutine);
                    KillEnemyOnOwnerClient();
                }
                else if (playerWhoHit != null && currentBehaviourStateIndex != (int)State.ChasingTarget)
                {
                    irritationLevel = irritationMaxLevel;
                    targetPlayer = playerWhoHit;
                    SwitchToBehaviourServerRpc((int)State.SpecialAttack);
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void PerformSpecialTailAttackServerRpc()
        {
            readyToChaseFromSpecialAttack = false;
            originalRotation = transform.rotation;
            StartCoroutine(BeginSpecialTailAttack());
        }

        [ServerRpc(RequireOwnership = false)]
        public void LookAtTargetServerRpc()
        {
            LookAtTargetClientRpc();
        }


        [ClientRpc]
        public void LookAtTargetClientRpc()
        {
            if (targetPlayer != null)
            {
                Vector3 targetPosition = targetPlayer.gameplayCamera.transform.position;
                Vector3 directionToTarget = targetPosition - transform.position;
                directionToTarget.y = 0;

                Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
                transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, 4f * Time.deltaTime);

                //LogIfDebugBuild("JPOGStegosaurus: facing target player!");
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
            }
        }

        public override void SetEnemyStunned(bool setToStunned, float setToStunTime = 1, PlayerControllerB? setStunnedByPlayer = null)
        {
            base.SetEnemyStunned(setToStunned, setToStunTime, setStunnedByPlayer);
            SwitchToBehaviourServerRpc((int)State.Stunned);
            if (setStunnedByPlayer != null)
            {
                irritationLevel = irritationMaxLevel;
                targetPlayer = setStunnedByPlayer;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void CheckForPlayersInAggroAreaServerRpc()
        {
            CheckForPlayersInAggroAreaClientRpc();
        }

        [ClientRpc]
        private void CheckForPlayersInAggroAreaClientRpc()
        {
            CheckForPlayersInAggroArea();
        }

        private void CheckForPlayersInAggroArea()
        {
            int playerLayer = 1 << 3;
            Collider[] hitColliders = Physics.OverlapBox(aggroArea.position, aggroArea.localScale, Quaternion.identity, playerLayer);
            if (hitColliders.Length > 0)
            {
                foreach (var player in hitColliders)
                {
                    PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(player);
                    if (playerControllerB != null && irritationLevel != irritationMaxLevel)
                    {
                        LogIfDebugBuild($"JPOGStegosaurus: player: [{playerControllerB.actualClientId}] was in the Aggro Area ");
                        IncreaseIrritationServerRpc();
                        if (irritationLevel == irritationMaxLevel)
                        {
                            targetPlayer = playerControllerB;
                            break;
                        }
                    }

                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void CheckForPlayersInAttackAreaFrontServerRpc()
        {
            var state = currentBehaviourStateIndex;
            bool playerInFrontAttackArea = CheckForPlayersInAttackAreaFront();
            if (playerInFrontAttackArea)
            {
                if (!inStompAttack || !inTailAttack)
                {
                    if (state == (int)State.ChasingTarget) //The frontal attack should only occur when the StegoSaurus is irritated (in chasing mode).
                    {
                        StartCoroutine(BeginStompAttack());
                    }
                }
            }

        }

        private bool CheckForPlayersInAttackAreaFront()
        {
            bool playerInFrontAttackArea = false;
            int playerLayer = 1 << 3;
            Collider[] hitColliders = Physics.OverlapBox(attackAreaFront.position, attackAreaFront.localScale, Quaternion.identity, playerLayer);
            if (hitColliders.Length > 0)
            {
                foreach (var player in hitColliders)
                {
                    PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(player);
                    if (playerControllerB != null && irritationLevel != irritationMaxLevel)
                    {
                        LogIfDebugBuild($"JPOGStegosaurus: player: [{playerControllerB.actualClientId}] was in Frontal Attack Area ");
                        IncreaseIrritationServerRpc();
                    }
                }
                playerInFrontAttackArea = true;
            }
            return playerInFrontAttackArea;
        }


        [ServerRpc(RequireOwnership = false)]
        private void CheckForPlayersInAttackAreaBackServerRpc()
        {
            bool playerInBackAttackArea = CheckForPlayersInAttackAreaBack();
            if (playerInBackAttackArea) {
                LogIfDebugBuild($"JPOGStegosaurus: player was in Back attack area, Calling Coroutine to begin tail attack!");
                if (!inStompAttack || !inTailAttack)
                {
                    StartCoroutine(BeginTailAttack());
                }
                IncreaseIrritationServerRpc();
            }
        }

        private bool CheckForPlayersInAttackAreaBack()
        {
            bool playerInBackAttackArea = false;
            int playerLayer = 1 << 3;
            Collider[] hitColliders = Physics.OverlapBox(attackAreaBack.position, attackAreaBack.localScale, Quaternion.identity, playerLayer);
            if (hitColliders.Length > 0)
            {
                foreach (var player in hitColliders)
                {
                    PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(player);
                    if (playerControllerB != null && irritationLevel != irritationMaxLevel)
                    {
                        LogIfDebugBuild($"JPOGStegosaurus: player: [{playerControllerB.actualClientId}] was in Back Attack Area ");
                        IncreaseIrritationServerRpc();
                    }
                }
                playerInBackAttackArea = true;
            }
            return playerInBackAttackArea;
        }

        [ClientRpc]
        private void CheckIfStompAttackHitPlayersClientRpc()
        {
            bool hitPlayer = CheckIfStompAttackHitPlayers();
            if (hitPlayer)
            {
                LogIfDebugBuild($"JPOGStegosaurus: stomp attack hitPlayer = [{hitPlayer}] calling to kill all hit players");
                KillPlayersByStompClientRpc();
            }
            else
            {
                //LogIfDebugBuild($"JPOGStegosaurus: No players have been hit");
            }
        }

        [ClientRpc]
        private void KillPlayersByStompClientRpc()
        {
            if (stompHitPlayerIds.Count > 0)
            {
                foreach (int playerId in stompHitPlayerIds)
                {
                    StartCoroutine(KillPlayer(playerId, CauseOfDeath.Crushing));
                }
                stompHitPlayerIds.Clear();
            }
        }

        private bool CheckIfStompAttackHitPlayers()
        {
            bool hitPlayer = false;
            int playerLayer = 1 << 3;
            Collider[] hitColliders = Physics.OverlapBox(stompHitbox.position, stompHitbox.localScale, Quaternion.identity, playerLayer);
            if (hitColliders.Length > 0)
            {
                foreach (var player in hitColliders)
                {
                    PlayerControllerB? playerControllerB = MeetsStandardPlayerCollisionConditions(player);
                    if (playerControllerB != null && CheckIfPlayerIsTargetable(playerControllerB))
                    {
                        int playerId = (int)playerControllerB.actualClientId;
                        LogIfDebugBuild($"JPOGStegosaurus: Checking if player[{playerControllerB.actualClientId}] has not yet been hit");
                        if (!stompHitPlayerIds.Contains((int)playerControllerB.playerClientId))
                        {
                            LogIfDebugBuild($"JPOGStegosaurus: Player[{playerControllerB.actualClientId}] was hit by stomp attack");
                            stompHitPlayerIds.Add((int)playerControllerB.playerClientId);
                            hitPlayer = true;
                        }
                        else
                        {
                            LogIfDebugBuild($"JPOGStegosaurus: Player[{playerControllerB.actualClientId}] has already been hit by stomp attack");
                        }
                    }
                    else
                    {
                        LogIfDebugBuild("JPOGStegosaurus: PlayerControllerB is null or player does not meet collision conditions");
                    }
                }
            }
            return hitPlayer;
        }


        private bool CheckIfInAttackAnimation()
        {
            bool inAttackAnimation = true;
            if(!inSpecialTailAttack && !inStompAttack && !inTailAttack)
            {
                inAttackAnimation = false;
            }
            return inAttackAnimation;
        }

        IEnumerator BeginSpecialTailAttack()
        {
            if (!CheckIfInAttackAnimation())
            {
                inSpecialTailAttack = true;
                StartCoroutine(BeginSpecialTailAnimation());
                while (inSpecialTailAttack)
                {
                    CheckIfTailAttackHitPlayersClientRpc();
                    CheckIfTailAttackHitEntitiesClientRpc(); //Entities in range of the tail attack should also be hit/killed.
                    yield return null;                    
                }
            }
            //transform.rotation = originalRotation;
            transform.rotation = Quaternion.Euler(0, 0, 0);
            readyToChaseFromSpecialAttack = true;
            yield break;
        }

        [ClientRpc]
        private void CheckIfTailAttackHitEntitiesClientRpc()
        {
            CheckIfTailAttackHitEntities();
        }

        private void CheckIfTailAttackHitEntities()
        {
            foreach (EnemyAI entity in RoundManager.Instance.SpawnedEnemies)
            {
                if (this.enemyType.name.Equals(entity.enemyType.name) || entity.isEnemyDead || entity.enemyType.canDie)
                {
                    continue;
                }
                float tailDistanceToEntiy = Vector3.Distance(entity.transform.position, tailSpike1.transform.position);
                if (tailDistanceToEntiy < 5f)
                {
                    LogIfDebugBuild($"JPOGStegosaurus: entity enemy type = [{entity.enemyType.name}] will be hit. disatance = [{tailDistanceToEntiy}]");
                    DamageEntity(entity);
                }
            }
        }

        IEnumerator BeginSpecialTailAttackOnEntity()
        {
            agent.speed = 0f;
            SetWalkingAnimtionServerRpc(agent.speed);
            if (!CheckIfInAttackAnimation())
            {
                inSpecialTailAttack = true;
                StartCoroutine(BeginSpecialTailAnimation());
                yield return new WaitForSeconds(5.5f);
                agent.speed = 3f;
                SetWalkingAnimtionServerRpc(agent.speed);
            }

            yield break;
        }

        IEnumerator BeginSpecialTailAnimation()
        {
            LogIfDebugBuild("JPOGStegosaurus: Beginning Special tail attack animation!");
            DoAnimationClientRpc("tailFrontalAttack");
            yield return new WaitForSeconds(5.7f);
            LogIfDebugBuild("JPOGStegosaurus: Special tail attack complete!");
            inSpecialTailAttack = false;
            yield break;
        }

        IEnumerator BeginStompAttack()
        {
            if (!CheckIfInAttackAnimation())
            {
                inStompAttack = true;
                StartCoroutine(BeginStompAnimation());
                yield return new WaitForSeconds(2f);
                while (inStompAttack)
                {
                    CheckIfStompAttackHitPlayersClientRpc();
                    yield return null;
                }
                if(targetEntiy != null)
                {
                    targetEntiy.HitEnemy(5, null, true, -1);
                    targetEntiy = null;
                }
            }
            yield break;
        }

        IEnumerator BeginStompAnimation()
        {
            DoAnimationClientRpc("stompAttack");
            yield return new WaitForSeconds(2.2f);
            inStompAttack = false;
            yield break;
        }

        IEnumerator BeginTailAttack()
        {
            if (!CheckIfInAttackAnimation())
            {
                inTailAttack = true;
                StartCoroutine(BeginTailAttackAnimation());
                yield return new WaitForSeconds(0.3f);
                while (inTailAttack)
                {
                    CheckIfTailAttackHitPlayersClientRpc();
                    yield return null;
                }
            }
           yield break;
        }

        IEnumerator BeginTailAttackAnimation()
        {
            DoAnimationClientRpc("tailAttack");
            yield return new WaitForSeconds(2.8f);
            inTailAttack = false;
            yield break;
        }

        [ServerRpc(RequireOwnership = false)]
        private void CheckIfTailAttackHitPlayersServerRpc()
        {
            CheckIfTailAttackHitPlayersClientRpc();
        }

        [ClientRpc]
        private void CheckIfTailAttackHitPlayersClientRpc()
        {
            bool hitPlayer = CheckIfTailAttackHitPlayers();
            if (hitPlayer)
            {
                specialAttackHasHitPlayer = true;
                LogIfDebugBuild($"JPOGStegosaurus: hitPlayer = [{hitPlayer}] calling to kill all hit players");
                KillPlayersByTailClientRpc();                
            }
            else
            {
                //LogIfDebugBuild($"JPOGStegosaurus: No players have been hit");
            }
        }

        private bool CheckIfTailAttackHitPlayers()
        {
            bool hitPlayer = false;
            int playerLayer = 1 << 3;
            Collider[] hitColliders = Physics.OverlapBox(tailHitBox.position, tailHitBox.localScale, Quaternion.identity, playerLayer);
            if (hitColliders.Length > 0)
            {
                foreach (var player in hitColliders)
                {
                    PlayerControllerB? playerControllerB = MeetsStandardPlayerCollisionConditions(player);
                    if (playerControllerB != null && CheckIfPlayerIsTargetable(playerControllerB))
                    {
                        int playerId = (int)playerControllerB.actualClientId;
                        LogIfDebugBuild($"JPOGStegosaurus: Checking if player[{playerControllerB.actualClientId}] has not yet been hit");
                        if (!tailHitPlayerIds.Contains((int)playerControllerB.playerClientId))
                        {
                            LogIfDebugBuild($"JPOGStegosaurus: Player[{playerControllerB.actualClientId}] was hit by tail attack ");
                            tailHitPlayerIds.Add((int)playerControllerB.playerClientId);

                            hitPlayer = true;
                        }
                        else
                        {
                            LogIfDebugBuild($"JPOGStegosaurus: Player[{playerControllerB.actualClientId}] has already been hit");
                        }
                    }
                    else
                    {
                        LogIfDebugBuild("JPOGStegosaurus: PlayerControllerB is null or player does not meet collision conditions");
                    }
                }
            }
            return hitPlayer;
        }

        [ServerRpc(RequireOwnership = false)]
        private void CheckIdleTimeServerRpc()
        {
            LogIfDebugBuild($"JPOGStegosaurus: Checking if It's idling time. current time - roaming start time = [{Time.time - roamingStartTime}] || timeToIdle = [{timeToIdle}]");
            if (Time.time - roamingStartTime >= timeToIdle && isDoneIdling)
            {
                LogIfDebugBuild($"JPOGStegosaurus: started roaming at [{roamingStartTime}] time roaming, time to idle = [{timeToIdle}]. Switching to idle state.");
                SwitchToBehaviourServerRpc((int)State.Idling);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void IncreaseIrritationServerRpc()
        {

            var state = currentBehaviourStateIndex;
            LogIfDebugBuild($"JPOGStegosaurus: Checking time before increasing irritation. actual time = [{Time.time}] || lastIrritationIncrementTime = [{lastIrritationIncrementTime}] || irritationIncrementInterval = [{irritationIncrementInterval}]");
            if ((Time.time - lastIrritationIncrementTime >= irritationIncrementInterval) && (state != (int)State.ChasingTarget))
            {
                irritationLevel = Mathf.Clamp(irritationLevel + irritationIncrementAmount, 0, irritationMaxLevel);
                lastIrritationIncrementTime = Time.time;
                LogIfDebugBuild($"JPOGStegosaurus: IrritationLevel increased to = [{irritationLevel}]");
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void DecreaseIrritationServerRpc()
        {
            if (Time.time - lastIrritationDecreaseTime >= irritationDecrementinterval)
            {
                irritationLevel = Mathf.Clamp(irritationLevel - irritationDecrementinterval, 0, irritationMaxLevel);
                lastIrritationDecreaseTime = Time.time;
                LogIfDebugBuild($"JPOGStegosaurus: IrritationLevel decreased to = [{irritationLevel}]");
            }
        }


        [ClientRpc]
        public void DoAnimationClientRpc(string animationName) {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }

        [ClientRpc]
        public void SwingAttackHitClientRpc() {
            LogIfDebugBuild("SwingAttackHitClientRPC");
            int playerLayer = 1 << 3; // This can be found from the game's Asset Ripper output in Unity
            Collider[] hitColliders = Physics.OverlapBox(attackAreaFront.position, attackAreaFront.localScale, Quaternion.identity, playerLayer);
            if(hitColliders.Length > 0){
                foreach (var player in hitColliders){
                    PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(player);
                    if (playerControllerB != null)
                    {
                        LogIfDebugBuild("Swing attack hit player!");
                        timeSinceHittingLocalPlayer = 0f;
                        playerControllerB.DamagePlayer(40);
                    }
                }
            }
        }

        [ClientRpc]
        private void PlayRandomIdleAnimationClientRpc()
        {
            inRandomIdleAnimation = true;
            string animationName = GetAnimationNameForIdle();
            LogIfDebugBuild($"JPOGStegosaurus: idle animation to play = [{animationName}]");
            switch (animationName)
            {
                case "tailshow":
                    StartCoroutine(PlayTailShowIdle());
                    break;
                case "scratch":
                    StartCoroutine(PlayScratchIdle());
                    break;
                case "stretch":
                    StartCoroutine(PlayStretchIdle());
                    break;
                default:
                    StartCoroutine(PlayTailShowIdle());
                    break;
            }
        }

        private string GetAnimationNameForIdle()
        {
            string[] animations = { "scratch", "stretch", "scratch" };
            int animationNumber = enemyRandom.Next(0, animations.Length);
            return animations[animationNumber];
        }

        IEnumerator PlayTailShowIdle()
        {
            LogIfDebugBuild($"JPOGStegosaurus: Beginning tailshow idle");
            DoAnimationClientRpc("tailShow");
            yield return new WaitForSeconds(3.8f);
            inRandomIdleAnimation = false;
            isDoneIdling = true;
            yield break;
        }

        IEnumerator PlayScratchIdle()
        {
            LogIfDebugBuild($"JPOGStegosaurus: Beginning scratch idle");
            DoAnimationClientRpc("scratch");
            yield return new WaitForSeconds(4.5f);
            inRandomIdleAnimation = false;
            isDoneIdling = true;
            yield break;
        } 

        IEnumerator PlayStretchIdle()
        {
            LogIfDebugBuild($"JPOGStegosaurus: Beginning stretch idle");
            DoAnimationClientRpc("stretch");
            yield return new WaitForSeconds(4.1f);
            inRandomIdleAnimation = false;
            isDoneIdling = true;
            yield break;
        }

        private IEnumerator RoamingStateCoroutine()
        {
            LogIfDebugBuild($"JPOGStegosaurus: Roaming State Coroutine started");
            yield return new WaitForSeconds(timeToIdle);
            LogIfDebugBuild($"JPOGStegosaurus: Roaming State Coroutine Ended, entering idling state");
            SwitchToBehaviourClientRpc((int)State.Idling);
        }



        [ServerRpc(RequireOwnership = false)]
        private void StateChangeHelperServerRpc(State state)
        {
            //LogIfDebugBuild($"JPOGStegosaurus: Checking if state changed: previous state = [{previousState}] || entered state = [{state}]");
            if (previousState != state)
            {
                SetWallkingAnimationPerSate(state);
                previousState = state;
                StateChangeHelperClientRpc(state);
                if (state == State.Roaming)
                {
                    movingTowardsTargetPlayer = false;
                    roamingStartTime = Time.time;
                }
                else if(state == State.SpecialAttack)
                {
                    CheckIfSpecialAttackCanHitPlayerServerRpc();
                }
                else if (state != State.Idling || state != State.Roaming)
                {
                    inRandomIdleAnimation = false;
                    isDoneIdling = true;
                }
                else if (state == State.ChasingTarget) {
                    movingTowardsTargetPlayer = true;
                }
            }
        }


        [ClientRpc]
        private void StateChangeHelperClientRpc(State state)
        {
            LogIfDebugBuild($"JPOGStegosaurus: Entered state [{state}]");
            if (state != State.Idling)
            {
                //Idle animation coroutine should alway be stopped when entering any other coroutine excpet for Idling
                StopCoroutine(PlayStretchIdle());
                StopCoroutine(PlayTailShowIdle());
                StopCoroutine(PlayScratchIdle());
            }
            else if (state == State.Idling)
            {
                isDoneIdling = false;
            }
        }

        private void SetWallkingAnimationPerSate(State state)
        {
            if (state == State.Roaming)
            {
                agent.speed = 3;
                SetWalkingAnimtion(agent.speed);
            }
            else if (state == State.Stunned)
            {
                agent.speed = 0;
                SetWalkingAnimtion(agent.speed);
            }
            else if (state == State.SpecialAttack)
            {
                agent.speed = 0;
                SetWalkingAnimtion(agent.speed);
            }
            else if (state == State.ChasingTarget)
            {
                agent.speed = 6;
                SetWalkingAnimtion(agent.speed);
            }
            else if (state == State.AttackEnemy)
            {
                agent.speed = 3;
                SetWalkingAnimtion(agent.speed);
            }
            else if (state == State.RunningAway)
            {
                agent.speed = 6;
                SetWalkingAnimtion(agent.speed);
            }
            else if (state == State.Idling)
            {
                agent.speed = 0;
                SetWalkingAnimtion(agent.speed);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetWalkingAnimtionServerRpc(float agentSpeed)
        {
            SetWalkingAnimtion(agentSpeed);
        }

        [ClientRpc]
        private void SetWalkingAnimtionClientRpc(float agentSpeed)
        {
            SetWalkingAnimtion(agentSpeed);
        }

        private void SetWalkingAnimtion(float agentSpeed)
        {
            if (agentSpeed == 0 && (currentBehaviourStateIndex == (int)State.ChasingTarget)) {

                LogIfDebugBuild($"JPOGStegosaurus: Stopping running animation");
                DoAnimationClientRpc("stopRun");
            }
            else if (agentSpeed == 0)
            {
                LogIfDebugBuild($"JPOGStegosaurus: Stopping walking animation");
                DoAnimationClientRpc("stopWalk");
                //DoAnimationClientRpc("stopRun");
            }
            else if (agentSpeed > 0 && agentSpeed <= 3)
            {
                LogIfDebugBuild($"JPOGStegosaurus: Beginning walking animation");
                DoAnimationClientRpc("startWalk");
            }
            else if (agentSpeed > 3)
            {
                LogIfDebugBuild($"JPOGStegosaurus: Beginning running animation");
                DoAnimationClientRpc("startRun");
            }

        }

        private void AssignConfigVariables()
        {
            irritationMaxLevel = PluginConfig.Instance.MaxIrritationLevel.Value;
            irritationDecrementinterval = PluginConfig.Instance.IntervalIrrtationDecrement.Value;
            irritationIncrementInterval = PluginConfig.Instance.IntervalIrrtationIncrement.Value;
            irritationIncrementAmount = PluginConfig.Instance.IncreaseAmountIrritation.Value;
            irritationDecrementAmount = PluginConfig.Instance.DecreaseAmountIrritation.Value;
            //timeToIdle = PluginConfig.Instance.IntervalIdling.Value;
        }

        [ClientRpc]
        private void KillPlayersByTailClientRpc()
        {
            if (tailHitPlayerIds.Count > 0)
            {
                foreach (int playerId in tailHitPlayerIds)
                {
                    StartCoroutine(KillPlayer(playerId, CauseOfDeath.Stabbing));
                }
                tailHitPlayerIds.Clear();
            }
        }

        private IEnumerator KillPlayer(int playerId, CauseOfDeath causeOfDeath)
        {
            LogIfDebugBuild($"JPOGStegosaurus: begin Killing hit player(s)");
            PlayerControllerB killPlayer = StartOfRound.Instance.allPlayerScripts[playerId];

            if (killPlayer == null || killPlayer.isPlayerDead)
            {
                LogIfDebugBuild($"JPOGStegosaurus: Player [{playerId}] is not valid or already dead.");
                yield break;
            }

            if (!isEnemyDead)
            {
                LogIfDebugBuild("JPOGStegosaurus: Stegosaurus is still alive, killing player Continues");

                if (GameNetworkManager.Instance.localPlayerController == killPlayer)
                {
                    killPlayer.KillPlayer(Vector3.zero, spawnBody: true, causeOfDeath, 0);
                }

                float startTime = Time.timeSinceLevelLoad;
                yield return new WaitUntil(() => killPlayer.deadBody != null || Time.timeSinceLevelLoad - startTime > 2f);

                if (killPlayer.deadBody == null)
                {
                    LogIfDebugBuild("JPOGStegosaurus: Player body was not spawned or found withing 2 seconds");
                    killPlayer.inAnimationWithEnemy = null;
                    yield break;
                }

                if (killPlayer.deadBody.causeOfDeath == CauseOfDeath.Stabbing){
                    PinBodyToSpikeServerRpc(playerId);
                }

                startTime = Time.timeSinceLevelLoad;

                Quaternion rotateTo = Quaternion.Euler(new Vector3(0f, RoundManager.Instance.YRotationThatFacesTheFarthestFromPosition(base.transform.position + Vector3.up * 0.6f), 0f));
                Quaternion rotateFrom = base.transform.rotation;

                while (Time.timeSinceLevelLoad - startTime < 2f)
                {
                    yield return null;
                    if (base.IsOwner)
                    {
                        base.transform.rotation = Quaternion.RotateTowards(rotateFrom, rotateTo, 60f * Time.deltaTime);
                    }
                }
            }
            yield break;
        }

        [ServerRpc(RequireOwnership = false)]
        private void PinBodyToSpikeServerRpc(int playerId)
        {
            LogIfDebugBuild($"JPOGStegosaurus: Pinning boddy of player [{playerId}] to a random spike");
            PinBodyToSpike(playerId); // Perform server-side operations

            // Notify all clients to update their state
            AttachBodyToSpikeClientRpc(playerId);
            AddToSpikedBodiesClientRpc(playerId);
        }

        private void PinBodyToSpike(int playerId)
        {
            AttachBodyToSpike(playerId);
            AddToSpikedBodies(playerId);
        }


        [ClientRpc]
        private void AttachBodyToSpikeClientRpc(int playerId)
        {
            AttachBodyToSpike(playerId);
        }

        [ClientRpc]
        private void AddToSpikedBodiesClientRpc(int playerId) {
            AddToSpikedBodies(playerId);
        }

        private void AttachBodyToSpike(int playerId)
        {
            int spikeToPinTo = enemyRandom.Next(1, 5); // Changed to 5 to include the possibility of 4
            DeadBodyInfo killedPlayerBody = StartOfRound.Instance.allPlayerScripts[playerId].deadBody;
            Transform selectedSpike;
            switch (spikeToPinTo)
            {
                case 1:
                    selectedSpike = tailSpike1;
                    break;
                case 2:
                    selectedSpike = tailSpike2;
                    break;
                case 3:
                    selectedSpike = tailSpike3;
                    break;
                case 4:
                    selectedSpike = tailSpike4;
                    break;
                default:
                    selectedSpike = tailSpike1;
                    break;
            }

            if (killedPlayerBody != null)
            {
                killedPlayerBody.attachedTo = selectedSpike;
                killedPlayerBody.attachedLimb = killedPlayerBody.bodyParts[5];
                killedPlayerBody.matchPositionExactly = true;
                killedPlayerBody.MakeCorpseBloody();
                LogIfDebugBuild($"JPOGStegosaurus: Succesfully attached player[{playerId}]'s dead body to a spike.");
            }
            else
            {
                LogIfDebugBuild($"JPOGStegosaurus: Something went wrong with attaching player[{playerId}]'s deady body to a spike.");
            }
        }

        private void AddToSpikedBodies(int playerId)
        {
            LogIfDebugBuild($"JPOGStegosaurus: Adding dead body to spiked bodies");
            DeadBodyInfo killedPlayerBody = StartOfRound.Instance.allPlayerScripts[playerId].deadBody;
            if(killedPlayerBody != null)
            {
                spikedBodies.Add(killedPlayerBody);
                if (spikedBodies.Contains(killedPlayerBody))
                {
                    LogIfDebugBuild($"JPOGStegosaurus: successfully added dead body [{killedPlayerBody.playerObjectId}] to spiked bodies");
                }
                else
                {
                    LogIfDebugBuild($"JPOGStegosaurus: failed to add dead body [{killedPlayerBody.playerObjectId}] to spiked bodies");
                }
            }
        }


        private void DamageEntity(EnemyAI entity)
        {
            entity.HitEnemy(5, null, true, -1);
        }

        private void PlayAudioClip(AudioClip audioClip)
        {
            //LogIfDebugBuild("JPOGStegosaurus: Playing audio clip through CreatureVoice");
            DoAnimationClientRpc("roar");
            creatureVoice.PlayOneShot(audioClip);
            WalkieTalkie.TransmitOneShotAudio(creatureVoice, audioClip);
        }

        private void PlayAudioSFX(AudioClip audioClip)
        {
            //LogIfDebugBuild("JPOGStegosaurus: Playing audio clip through CreatureSFX");
            creatureSFX.PlayOneShot(audioClip);
        }
        private void PlayTailSFX(AudioClip audioClip)
        {
            //LogIfDebugBuild("JPOGStegosaurus: Playing audio clip through TailSFX");
            tailSFX.PlayOneShot(audioClip);
        }
    }
}