using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace REPOBot.Game
{
    /// <summary>
    /// Converts a desired WORLD-space move direction into the game's character
    /// input and applies it, plus interact/grab presses.
    ///
    /// IMPORTANT: how a Unity game ingests movement varies a lot, and R.E.P.O.'s
    /// exact input path is the single most version-sensitive thing in this mod.
    /// This driver supports two strategies, tried in order:
    ///
    ///   1. INPUT-FIELD strategy (preferred, least invasive): write the desired
    ///      local move axes into the field the player controller reads each tick.
    ///      Set the field name in <see cref="MoveInputCandidates"/>.
    ///
    ///   2. RIGIDBODY/transform nudge (fallback): if no input field is found, push
    ///      the player's Rigidbody toward the goal. This is cruder and can fight
    ///      the controller, but it lets you see the bot working while you wire up
    ///      strategy 1 for your build.
    ///
    /// Verify the candidate names against your decompile (dnSpy / DLL Muster).
    /// </summary>
    public sealed class InputDriver
    {
        private static readonly string[] MoveInputCandidates =
            { "moveInput", "movementInput", "inputMovement", "moveDirection", "inputVector" };

        private static readonly string[] SprintCandidates =
            { "sprint", "isSprinting", "running", "sprintInput" };

        private readonly ManualLogSource _log;
        private readonly GameApi _api;
        private FieldInfo _moveField;
        private FieldInfo _sprintField;
        private bool _resolved;
        private bool _warnedFallback;

        public InputDriver(GameApi api, ManualLogSource log)
        {
            _api = api;
            _log = log;
        }

        private void ResolveOnce(Component player)
        {
            if (_resolved || player == null) return;
            _resolved = true;
            var t = player.GetType();
            _moveField = Reflect.Field(t, MoveInputCandidates);
            _sprintField = Reflect.Field(t, SprintCandidates);
            if (_moveField == null)
                _log.LogWarning("InputDriver: no movement-input field resolved on " + t.Name +
                                ". Falling back to Rigidbody nudge. Set MoveInputCandidates to wire strategy 1.");
            else
                _log.LogInfo($"InputDriver: using input field '{_moveField.Name}' ({_moveField.FieldType.Name}).");
        }

        /// <summary>
        /// Drive the player toward <paramref name="worldDir"/> (XZ, need not be
        /// normalised). <paramref name="intensity"/> scales 0..1.
        /// </summary>
        public void Drive(Component player, Vector3 worldDir, float intensity, bool sprint, Camera cam)
        {
            if (player == null) return;
            ResolveOnce(player);

            worldDir.y = 0f;
            if (worldDir.sqrMagnitude < 0.0001f)
            {
                Stop(player);
                return;
            }
            worldDir.Normalize();

            // Convert world direction to local move axes relative to the camera
            // (how a player would press W/A/S/D looking that way).
            Vector3 fwd = cam != null ? cam.transform.forward : player.transform.forward;
            Vector3 right = cam != null ? cam.transform.right : player.transform.right;
            fwd.y = 0f; right.y = 0f;
            fwd.Normalize(); right.Normalize();

            float ax = Vector3.Dot(worldDir, right);
            float az = Vector3.Dot(worldDir, fwd);
            var local = new Vector2(ax, az);
            if (local.sqrMagnitude > 1f) local.Normalize();
            local *= Mathf.Clamp01(intensity);

            if (_moveField != null)
            {
                WriteMove(player, local, sprint);
            }
            else
            {
                NudgeRigidbody(player, worldDir, intensity, sprint);
            }
        }

        public void Stop(Component player)
        {
            if (player == null) return;
            if (_moveField != null)
                WriteMove(player, Vector2.zero, false);
        }

        private void WriteMove(Component player, Vector2 local, bool sprint)
        {
            try
            {
                // Support either a Vector2 or Vector3 input field.
                if (_moveField.FieldType == typeof(Vector2))
                    _moveField.SetValue(player, local);
                else if (_moveField.FieldType == typeof(Vector3))
                    _moveField.SetValue(player, new Vector3(local.x, 0f, local.y));

                if (_sprintField != null && _sprintField.FieldType == typeof(bool))
                    _sprintField.SetValue(player, sprint);
            }
            catch (Exception ex)
            {
                if (!_warnedFallback)
                {
                    _warnedFallback = true;
                    _log.LogWarning("InputDriver write failed, switching to Rigidbody nudge: " + ex.Message);
                    _moveField = null;
                }
            }
        }

        private void NudgeRigidbody(Component player, Vector3 worldDir, float intensity, bool sprint)
        {
            var rb = player.GetComponent<Rigidbody>() ?? player.GetComponentInChildren<Rigidbody>();
            if (rb == null) return;
            float speed = (sprint ? 6f : 3.5f) * Mathf.Clamp01(intensity);
            Vector3 target = worldDir * speed;
            target.y = rb.velocity.y; // preserve gravity
            rb.velocity = Vector3.Lerp(rb.velocity, target, 0.5f);
        }

        // --- Interact / grab ---
        // R.E.P.O. grabbing is physics-based (PhysGrabber). The most reliable way
        // to trigger a grab is to simulate the interact input the same way as
        // movement above. Wire the field/method for your build here.
        private static readonly string[] InteractCandidates =
            { "interactInput", "grabInput", "interact", "useInput" };
        private FieldInfo _interactField;
        private bool _interactResolved;

        public void SetInteract(Component player, bool pressed)
        {
            if (player == null) return;
            if (!_interactResolved)
            {
                _interactResolved = true;
                _interactField = Reflect.Field(player.GetType(), InteractCandidates);
                if (_interactField == null)
                    _log.LogInfo("InputDriver: no interact-input field resolved; grab must be wired for your build.");
            }
            if (_interactField != null && _interactField.FieldType == typeof(bool))
            {
                try { _interactField.SetValue(player, pressed); } catch { /* ignore */ }
            }
        }
    }
}
