using UnityEngine;
using UnityEngine.AI;
using REPOBot.Config;

namespace REPOBot.Core
{
    /// <summary>
    /// Thin wrapper over Unity's NavMesh (the same surface the game's monsters
    /// path on). Computes a path to a goal, hands back the next corner to steer
    /// toward, throttles re-planning, and detects when the bot is stuck so the
    /// controller can recover.
    /// </summary>
    public sealed class Navigator
    {
        private readonly Settings _s;
        private readonly NavMeshPath _path = new NavMeshPath();
        private int _corner;
        private float _lastRepath;
        private Vector3 _goal;
        private bool _hasPath;

        // Stuck detection
        private Vector3 _lastPos;
        private float _stuckTimer;

        public Navigator(Settings s) => _s = s;

        public bool HasPath => _hasPath && _path.status != NavMeshPathStatus.PathInvalid;

        /// <summary>(Re)plan toward a goal, throttled by RepathInterval.</summary>
        public void SetGoal(Vector3 from, Vector3 goal, bool force = false)
        {
            bool goalMoved = (goal - _goal).sqrMagnitude > 1f;
            if (!force && !goalMoved && Time.time - _lastRepath < _s.RepathInterval.Value)
                return;

            _goal = goal;
            _lastRepath = Time.time;

            Vector3 start = Snap(from);
            Vector3 end = Snap(goal);
            _hasPath = NavMesh.CalculatePath(start, end, NavMesh.AllAreas, _path);
            _corner = 1; // corner 0 is the start
        }

        /// <summary>
        /// World direction to steer this tick. Advances through path corners as
        /// they are reached. Falls back to a straight line if there is no path.
        /// </summary>
        public Vector3 SteerDirection(Vector3 currentPos)
        {
            if (!HasPath || _path.corners == null || _path.corners.Length == 0)
                return _goal - currentPos;

            var corners = _path.corners;
            // Advance past corners we have effectively reached.
            while (_corner < corners.Length)
            {
                Vector3 flat = corners[_corner] - currentPos; flat.y = 0f;
                if (flat.magnitude <= _s.ArriveRadius.Value && _corner < corners.Length - 1)
                    _corner++;
                else
                    break;
            }
            int idx = Mathf.Min(_corner, corners.Length - 1);
            Vector3 dir = corners[idx] - currentPos;
            dir.y = 0f;
            return dir;
        }

        public bool Arrived(Vector3 currentPos)
        {
            Vector3 flat = _goal - currentPos; flat.y = 0f;
            return flat.magnitude <= _s.ArriveRadius.Value;
        }

        /// <summary>
        /// Returns true once the bot has failed to make progress for StuckSeconds,
        /// then resets its own timer so the controller can react and retry.
        /// </summary>
        public bool UpdateStuck(Vector3 currentPos, float deltaTime)
        {
            if ((currentPos - _lastPos).sqrMagnitude < 0.02f)
            {
                _stuckTimer += deltaTime;
            }
            else
            {
                _stuckTimer = 0f;
                _lastPos = currentPos;
            }

            if (_stuckTimer >= _s.StuckSeconds.Value)
            {
                _stuckTimer = 0f;
                return true;
            }
            return false;
        }

        public void Clear()
        {
            _hasPath = false;
            _corner = 1;
            _stuckTimer = 0f;
        }

        private static Vector3 Snap(Vector3 p)
        {
            return NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas) ? hit.position : p;
        }
    }
}
