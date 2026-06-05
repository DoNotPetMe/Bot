using UnityEngine;

namespace REPOBot.Timing
{
    /// <summary>
    /// A simple wall-clock run timer driven by Unity time. Tracks the elapsed
    /// time of the current run and exposes pace info relative to a target
    /// (personal best) so the controller can push harder when behind.
    /// </summary>
    public sealed class RunTimer
    {
        public bool Running { get; private set; }
        public float Elapsed { get; private set; }

        /// <summary>Best time we are chasing this run, if any (seconds).</summary>
        public float? Target { get; private set; }

        public void Start(float? target)
        {
            Running = true;
            Elapsed = 0f;
            Target = target;
        }

        public void Tick(float deltaTime)
        {
            if (Running)
                Elapsed += deltaTime;
        }

        public float Stop()
        {
            Running = false;
            return Elapsed;
        }

        public void Reset()
        {
            Running = false;
            Elapsed = 0f;
            Target = null;
        }

        /// <summary>
        /// How far ahead (positive) or behind (negative) the personal-best pace we
        /// are, in seconds, assuming linear pacing. Null if there is no target.
        /// This is intentionally a coarse heuristic - "beat best" mode just needs
        /// a sign and rough magnitude to decide how much extra risk to accept.
        /// </summary>
        public float? PaceDelta(float fractionComplete)
        {
            if (Target == null) return null;
            fractionComplete = Mathf.Clamp01(fractionComplete);
            float expectedByNow = Target.Value * fractionComplete;
            return expectedByNow - Elapsed; // >0 means ahead of PB pace
        }

        public static string Format(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            return $"{m:00}:{s:00.00}";
        }
    }
}
