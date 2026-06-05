using REPOBot.Config;
using UnityEngine;

namespace REPOBot.Brain
{
    /// <summary>
    /// Blends "seek the goal" with "avoid the monsters" into a single desired
    /// world-space move direction (XZ plane). The navigation layer turns that
    /// direction into actual control input.
    /// </summary>
    public sealed class Steering
    {
        private readonly Settings _s;

        public Steering(Settings s) => _s = s;

        /// <summary>
        /// Combine a seek direction (toward the next navmesh corner) with the
        /// threat repulsion. When fleeing, the goal is ignored and we move purely
        /// away from danger.
        /// </summary>
        public Vector3 Compute(Vector3 seekDir, ThreatModel.Assessment threat, bool fleeing)
        {
            seekDir.y = 0f;
            if (seekDir.sqrMagnitude > 0.0001f)
                seekDir = seekDir.normalized;

            if (fleeing)
            {
                // Pure survival: run away. If we somehow have no repulsion vector
                // (shouldn't happen while fleeing), fall back to the seek dir.
                return threat.Repulsion.sqrMagnitude > 0.0001f ? threat.Repulsion : seekDir;
            }

            // Weighted sum; avoidance scales with danger so faint threats barely
            // nudge the path while close ones dominate it.
            Vector3 avoid = threat.Repulsion * (_s.AvoidWeight.Value * threat.Danger);
            Vector3 combined = seekDir + avoid;
            combined.y = 0f;

            return combined.sqrMagnitude > 0.0001f ? combined.normalized : Vector3.zero;
        }
    }
}
