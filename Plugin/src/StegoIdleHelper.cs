using System.Collections;
using System.Diagnostics;
using UnityEngine;

namespace JPOGStegosaurus
{
    //Random idle animations. The idle state is currently not entered by the AI (work in progress), but it is fully wired up.
    internal class StegoIdleHelper
    {
        private readonly JPOGStegosaurusAI stegoAI;
        private Coroutine? idleCoroutine;

        public bool IsPlaying { get; private set; }
        public bool IsDone { get; private set; }

        public StegoIdleHelper(JPOGStegosaurusAI stegoAI)
        {
            this.stegoAI = stegoAI;
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public void PlayRandom()
        {
            (string animation, float duration) = stegoAI.enemyRandom.Next(0, 3) switch
            {
                0 => ("scratch", 4.5f),
                1 => ("stretch", 4.1f),
                _ => ("tailShow", 3.8f)
            };
            LogIfDebugBuild($"JPOGStegosaurus: idle animation to play = [{animation}]");
            IsPlaying = true;
            IsDone = false;
            idleCoroutine = stegoAI.StartCoroutine(IdleRoutine(animation, duration));
        }

        private IEnumerator IdleRoutine(string animation, float duration)
        {
            stegoAI.DoAnimationClientRpc(animation);
            yield return new WaitForSeconds(duration);
            IsPlaying = false;
            IsDone = true;
            idleCoroutine = null;
        }

        public void Reset()
        {
            if (idleCoroutine != null)
            {
                stegoAI.StopCoroutine(idleCoroutine);
                idleCoroutine = null;
            }
            IsPlaying = false;
            IsDone = false;
        }
    }
}
