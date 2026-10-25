using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using GameNetcodeStuff;
using UnityEngine;

namespace JPOGStegosaurus
{
    internal enum StegoAttackType
    {
        Tail,
        Stomp,
        SpecialTail
    }

    //Runs the tail, stomp and special tail attacks. The attack itself is driven by the owner (host) which
    //asks every client to check its own local player. Entities are hit by the owner only.
    internal class StegoAttackHelper
    {
        private const float HitCheckInterval = 0.05f;
        private const int EntityDamage = 5;
        private const float SpecialAttackEntityRange = 5f;
        private const float SpecialAttackMaxPlayerDistance = 10f;

        private readonly JPOGStegosaurusAI stegoAI;
        private readonly HashSet<EnemyAI> hitEntities = new HashSet<EnemyAI>();
        private Coroutine? attackCoroutine;

        //Used on every client to hit the local player only once per attack
        private int attackId;
        private int lastCheckedAttackId = -1;
        private bool localPlayerHit;

        public bool IsAttacking { get; private set; }
        public bool SpecialAttackStarted { get; private set; }
        public bool SpecialAttackFinished { get; private set; }

        public StegoAttackHelper(JPOGStegosaurusAI stegoAI)
        {
            this.stegoAI = stegoAI;
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public void StartTailAttack() => TryStartAttack(StegoAttackType.Tail);

        public void StartStompAttack(EnemyAI? entityTarget = null) => TryStartAttack(StegoAttackType.Stomp, entityTarget);

        public void StartSpecialTailAttack()
        {
            if (TryStartAttack(StegoAttackType.SpecialTail))
            {
                SpecialAttackStarted = true;
                SpecialAttackFinished = false;
            }
        }

        public void ResetSpecialAttack()
        {
            SpecialAttackStarted = false;
            SpecialAttackFinished = false;
        }

        //The special attack is only used when the target is close enough to be hit by the tail
        public bool IsTargetInSpecialAttackRange()
        {
            return stegoAI.targetPlayer != null
                && Vector3.Distance(stegoAI.transform.position, stegoAI.targetPlayer.transform.position) <= SpecialAttackMaxPlayerDistance;
        }

        public void StopAll()
        {
            if (attackCoroutine != null)
            {
                stegoAI.StopCoroutine(attackCoroutine);
                attackCoroutine = null;
            }
            IsAttacking = false;
        }

        private bool TryStartAttack(StegoAttackType type, EnemyAI? entityTarget = null)
        {
            if (IsAttacking || stegoAI.isEnemyDead)
            {
                return false;
            }
            IsAttacking = true;
            attackCoroutine = stegoAI.StartCoroutine(AttackRoutine(type, entityTarget));
            return true;
        }

        private IEnumerator AttackRoutine(StegoAttackType type, EnemyAI? entityTarget)
        {
            // The animation names, the time before the hitbox becomes active and the total animation length
            (string animation, float hitDelay, float duration) = type switch
            {
                StegoAttackType.Tail => ("tailAttack", 0.3f, 2.8f),
                StegoAttackType.Stomp => ("stompAttack", 2f, 2.2f),
                _ => ("tailFrontalAttack", 0f, 5.7f)
            };

            LogIfDebugBuild($"JPOGStegosaurus: Beginning {type} attack");
            attackId++;
            hitEntities.Clear();
            stegoAI.DoAnimationClientRpc(animation);

            float startTime = Time.time;
            yield return new WaitForSeconds(hitDelay);
            while (Time.time - startTime < duration && !stegoAI.isEnemyDead)
            {
                if (type == StegoAttackType.Stomp)
                {
                    stegoAI.StompHitCheckClientRpc(attackId);
                }
                else
                {
                    stegoAI.TailHitCheckClientRpc(attackId);
                }
                if (type == StegoAttackType.SpecialTail)
                {
                    HitEntitiesNearTail();
                }
                yield return new WaitForSeconds(HitCheckInterval);
            }
            float remaining = duration - (Time.time - startTime);
            if (remaining > 0f)
            {
                yield return new WaitForSeconds(remaining);
            }

            if (type == StegoAttackType.Stomp && entityTarget != null && !entityTarget.isEnemyDead)
            {
                DamageEntity(entityTarget);
            }
            if (type == StegoAttackType.SpecialTail)
            {
                // The special attack animation leaves the Stegosaurus turned around
                stegoAI.transform.rotation = Quaternion.Euler(0, 0, 0);
                SpecialAttackFinished = true;
            }
            IsAttacking = false;
            attackCoroutine = null;
        }

        private void HitEntitiesNearTail()
        {
            foreach (EnemyAI entity in RoundManager.Instance.SpawnedEnemies)
            {
                if (entity == null || entity.enemyType == stegoAI.enemyType || entity.isEnemyDead || !entity.enemyType.canDie || hitEntities.Contains(entity))
                {
                    continue;
                }
                float tailDistanceToEntity = Vector3.Distance(entity.transform.position, stegoAI.tailSpike1.position);
                if (tailDistanceToEntity < SpecialAttackEntityRange)
                {
                    LogIfDebugBuild($"JPOGStegosaurus: entity enemy type = [{entity.enemyType.name}] will be hit. distance = [{tailDistanceToEntity}]");
                    hitEntities.Add(entity);
                    DamageEntity(entity);
                }
            }
        }

        //HitEnemyOnLocalClient syncs the hit to every client
        private void DamageEntity(EnemyAI entity)
        {
            entity.HitEnemyOnLocalClient(EntityDamage);
        }

        //Called on every client. Each client only checks (and kills) its own local player.
        public void CheckLocalPlayerHit(StegoAttackType type, int id)
        {
            if (id != lastCheckedAttackId)
            {
                lastCheckedAttackId = id;
                localPlayerHit = false;
            }
            if (localPlayerHit || stegoAI.isEnemyDead)
            {
                return;
            }

            Transform hitBox = type == StegoAttackType.Stomp ? stegoAI.stompHitbox : stegoAI.tailHitBox;
            // The tail hitbox follows a bone, so it is kept axis aligned
            Quaternion rotation = type == StegoAttackType.Stomp ? hitBox.rotation : Quaternion.identity;
            Collider[] hitColliders = Physics.OverlapBox(hitBox.position, hitBox.localScale, rotation, 1 << 3);
            foreach (Collider collider in hitColliders)
            {
                PlayerControllerB? player = stegoAI.MeetsStandardPlayerCollisionConditions(collider);
                if (player != null && stegoAI.CanAttackPlayer(player))
                {
                    LogIfDebugBuild($"JPOGStegosaurus: Player[{player.actualClientId}] was hit by {type} attack");
                    localPlayerHit = true;
                    stegoAI.Bodies.KillLocalPlayer(type == StegoAttackType.Stomp ? CauseOfDeath.Crushing : CauseOfDeath.Stabbing);
                    return;
                }
            }
        }
    }
}
