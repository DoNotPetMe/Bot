using REPOBot.Config;
using REPOBot.Core;
using UnityEngine;

namespace REPOBot.Brain
{
    /// <summary>
    /// Turns the set of nearby monsters into (a) a danger level 0..1 and (b) a
    /// world-space repulsion vector pointing away from threats. Pure function of
    /// the snapshot + settings, so it is trivial to reason about and tune.
    /// </summary>
    public sealed class ThreatModel
    {
        private readonly Settings _s;

        public ThreatModel(Settings s) => _s = s;

        public struct Assessment
        {
            /// <summary>0 = clear, 1 = a monster is on top of us.</summary>
            public float Danger;
            /// <summary>True once inside FleeRadius of any monster.</summary>
            public bool ShouldFlee;
            /// <summary>Normalised (XZ) direction to move away from threats; zero if none.</summary>
            public Vector3 Repulsion;
            /// <summary>Distance to the closest monster, or +inf if none.</summary>
            public float NearestDistance;
        }

        public Assessment Assess(WorldSnapshot world)
        {
            var a = new Assessment
            {
                Danger = 0f,
                ShouldFlee = false,
                Repulsion = Vector3.zero,
                NearestDistance = float.PositiveInfinity
            };

            float danger = _s.DangerRadius.Value;
            float flee = _s.FleeRadius.Value;
            Vector3 accum = Vector3.zero;

            foreach (var e in world.Enemies)
            {
                if (e.Distance < a.NearestDistance)
                    a.NearestDistance = e.Distance;

                if (e.Distance > danger)
                    continue;

                // Closer monsters push harder (inverse-ish falloff), hunting ones harder still.
                float proximity = Mathf.Clamp01(1f - (e.Distance / danger));
                float weight = proximity * proximity;
                if (e.Alerted)
                    weight *= _s.AlertedMultiplier.Value;

                Vector3 away = world.PlayerPos - e.Pos;
                away.y = 0f;
                if (away.sqrMagnitude > 0.0001f)
                    accum += away.normalized * weight;

                a.Danger = Mathf.Max(a.Danger, weight);

                if (e.Distance <= flee)
                    a.ShouldFlee = true;
            }

            a.Danger = Mathf.Clamp01(a.Danger);
            accum.y = 0f;
            if (accum.sqrMagnitude > 0.0001f)
                a.Repulsion = accum.normalized;

            return a;
        }
    }
}
