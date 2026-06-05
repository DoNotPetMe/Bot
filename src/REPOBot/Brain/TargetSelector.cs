using System.Collections.Generic;
using REPOBot.Config;
using REPOBot.Core;
using UnityEngine;

namespace REPOBot.Brain
{
    /// <summary>
    /// Decides WHAT to pursue: which valuable to grab next, and when to switch to
    /// hauling/extraction. The scoring blends distance, value and how exposed each
    /// option is to monsters, weighted differently per BotMode.
    /// </summary>
    public sealed class TargetSelector
    {
        private readonly Settings _s;

        public TargetSelector(Settings s) => _s = s;

        /// <summary>Picks the best valuable to go for, or null if none qualifies.</summary>
        public ValuableView ChooseValuable(WorldSnapshot world, ThreatModel.Assessment threat, BotMode mode,
            HashSet<GameObject> skip = null)
        {
            ValuableView best = null;
            float bestScore = float.NegativeInfinity;

            bool speed = mode == BotMode.Speedrun;
            float minValue = _s.CollectAll.Value ? 0f : _s.MinValueToDetour.Value;

            foreach (var v in world.Valuables)
            {
                if (v.Value < minValue)
                    continue;
                if (skip != null && v.GameObject != null && skip.Contains(v.GameObject))
                    continue;

                // Base: prefer close items. Speedrun cares about distance most;
                // SafeCollect also weighs value so a costly detour is worthwhile.
                float distScore = -v.Distance;
                float valueScore = speed ? 0f : v.Value * 0.01f;

                // Penalise items sitting near a monster, more so when careful.
                float exposurePenalty = 0f;
                foreach (var e in world.Enemies)
                {
                    float d = Vector3.Distance(v.Pos, e.Pos);
                    if (d < _s.DangerRadius.Value)
                    {
                        float pen = (1f - d / _s.DangerRadius.Value);
                        exposurePenalty += pen * (e.Alerted ? _s.AlertedMultiplier.Value : 1f);
                    }
                }
                float caution = speed ? 1f : 3f;

                float score = distScore + valueScore - exposurePenalty * caution * 4f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = v;
                }
            }

            return best;
        }

        /// <summary>
        /// Decides whether the bot should be heading to extraction now rather than
        /// collecting more. True when nothing worthwhile is left, or the carry
        /// threshold is met, or there is no extraction-blocking reason to wait.
        /// </summary>
        public bool ShouldExtract(WorldSnapshot world, float carriedValue)
        {
            if (world.Extraction == null || !world.Extraction.Active)
                return false;

            // Carry-threshold policy: extract once holding "enough".
            float threshold = _s.ExtractWhenCarrying.Value;
            if (threshold > 0f && carriedValue >= threshold)
                return true;

            // Otherwise extract only when there is nothing left to collect.
            return world.ValuablesRemaining == 0;
        }
    }
}
