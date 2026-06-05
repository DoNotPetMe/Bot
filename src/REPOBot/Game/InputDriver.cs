using BepInEx.Logging;
using REPOBot.Config;
using UnityEngine;

namespace REPOBot.Game
{
    /// <summary>
    /// Turns a desired world-space move direction into actual character motion.
    ///
    /// Movement goes through <see cref="MovementPatch"/> (a Harmony velocity
    /// override on PlayerController) - the robust path that doesn't rely on the
    /// game's private input fields. If that patch couldn't be installed, we fall
    /// back to nudging the Rigidbody directly so the bot still moves, if crudely.
    ///
    /// Interact/grab is still best-effort (R.E.P.O. grabbing is aim-based via
    /// PhysGrabber); see the note on <see cref="SetInteract"/>.
    /// </summary>
    public sealed class InputDriver
    {
        private readonly Settings _s;
        private readonly ManualLogSource _log;
        private Rigidbody _fallbackBody;

        public InputDriver(GameApi api, Settings s, ManualLogSource log)
        {
            _s = s;
            _log = log;
        }

        /// <summary>Drive toward <paramref name="worldDir"/> (XZ) at <paramref name="speed"/> m/s.</summary>
        public void Drive(Component player, Vector3 worldDir, float speed)
        {
            worldDir.y = 0f;
            if (worldDir.sqrMagnitude < 0.0001f || speed <= 0.01f)
            {
                Stop(player);
                return;
            }
            worldDir.Normalize();

            bool useNative = _s.MovementMode.Value == MovementPatch.MoveMode.NativeInput
                             && MovementPatch.NativeAvailable;

            if (useNative)
            {
                MovementPatch.Mode = MovementPatch.MoveMode.NativeInput;
                Vector3 input = ToInputSpace(worldDir);
                // Scale magnitude so slower goals (carry/approach) move slower too.
                float mag = Mathf.Clamp01(speed / Mathf.Max(_s.WalkSpeed.Value, 0.1f));
                MovementPatch.SetInput(input * mag);
            }
            else
            {
                MovementPatch.Mode = MovementPatch.MoveMode.Velocity;
                MovementPatch.Acceleration = _s.Acceleration.Value;
                MovementPatch.AutoStep = _s.AutoStep.Value;
                MovementPatch.StepUpSpeed = _s.StepUpSpeed.Value;
                Vector3 vel = worldDir * speed;
                MovementPatch.SetVelocity(vel);
                if (!MovementPatch.Installed) NudgeFallback(player, vel);
            }
        }

        /// <summary>Convert a world move dir to the space the game's InputDirection wants.</summary>
        private Vector3 ToInputSpace(Vector3 worldDir)
        {
            if (_s.NativeInputSpace.Value == InputSpace.World) return worldDir;
            var cam = Camera.main;
            if (cam == null) return worldDir;
            Vector3 f = cam.transform.forward; f.y = 0f; f.Normalize();
            Vector3 r = cam.transform.right; r.y = 0f; r.Normalize();
            return new Vector3(Vector3.Dot(worldDir, r), 0f, Vector3.Dot(worldDir, f));
        }

        public void Stop(Component player)
        {
            MovementPatch.Release();
        }

        private void NudgeFallback(Component player, Vector3 vel)
        {
            if (_fallbackBody == null && player != null)
                _fallbackBody = player.GetComponent<Rigidbody>()
                              ?? player.GetComponentInChildren<Rigidbody>()
                              ?? player.GetComponentInParent<Rigidbody>();
            if (_fallbackBody == null || _fallbackBody.isKinematic) return;
            var cur = _fallbackBody.velocity;
            _fallbackBody.velocity = new Vector3(vel.x, cur.y, vel.z);
        }

        // --- Interact / grab ---
        // R.E.P.O. grabbing is aim-based (PhysGrabber grabs what the camera points
        // at). Driving a grab therefore needs camera aim + a grab input, which is
        // the next milestone. This hook is left in place but is a no-op until the
        // grab input is wired for your build.
        public void SetInteract(Component player, bool pressed)
        {
            // Intentionally no-op for now; see class comment.
        }
    }
}
