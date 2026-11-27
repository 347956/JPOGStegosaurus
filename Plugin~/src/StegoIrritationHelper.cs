using System.Diagnostics;
using UnityEngine;

namespace JPOGStegosaurus
{
    //Keeps track of how irritated the Stegosaurus is. Only used on the owner (host).
    internal class StegoIrritationHelper
    {
        //The Stegosaurus keeps chasing until the irritation drops below this percentage of the max
        private const float StopChasePercent = 20f;

        private readonly JPOGStegosaurusAI stegoAI;

        public float Level { get; private set; }
        public float Max { get; private set; }
        public bool IsMax => Level >= Max;
        public float StopChaseThreshold => Max / 100f * StopChasePercent;

        private float incrementAmount;
        private float decrementAmount;
        private float incrementInterval;
        private float decrementInterval;
        private float lastIncrementTime;
        private float lastDecrementTime;

        public StegoIrritationHelper(JPOGStegosaurusAI stegoAI)
        {
            this.stegoAI = stegoAI;
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public void Configure(float max, float incrementAmount, float incrementInterval, float decrementAmount, float decrementInterval)
        {
            Max = max;
            this.incrementAmount = incrementAmount;
            this.incrementInterval = incrementInterval;
            this.decrementAmount = decrementAmount;
            this.decrementInterval = decrementInterval;
        }

        public void SetPercent(float percent)
        {
            Level = Max / 100f * percent;
        }

        public void SetMax()
        {
            Level = Max;
        }

        //Returns true when the irritation was actually increased (it is limited by the increment interval)
        public bool TryIncrease()
        {
            if (stegoAI.currentBehaviourStateIndex == (int)StegoState.ChasingTarget || Time.time - lastIncrementTime < incrementInterval)
            {
                return false;
            }
            Level = Mathf.Clamp(Level + incrementAmount, 0, Max);
            lastIncrementTime = Time.time;
            LogIfDebugBuild($"JPOGStegosaurus: IrritationLevel increased to = [{Level}]");
            return true;
        }

        public void TryDecrease()
        {
            if (Time.time - lastDecrementTime < decrementInterval)
            {
                return;
            }
            Level = Mathf.Clamp(Level - decrementAmount, 0, Max);
            lastDecrementTime = Time.time;
            LogIfDebugBuild($"JPOGStegosaurus: IrritationLevel decreased to = [{Level}]");
        }
    }
}
