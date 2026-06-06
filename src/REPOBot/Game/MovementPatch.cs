using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace REPOBot.Game
{
    /// <summary>Coordinate space the game's InputDirection expects.</summary>
    public enum InputSpace { World, CameraRelative }

    /// <summary>
    /// Drives the local player. Two modes:
    ///
    ///  - NativeInput (preferred): writes the bot's desired move direction into
    ///    PlayerController.InputDirection(+Raw) via a Harmony postfix on the
    ///    controller's Update, so the GAME'S OWN movement code runs - real wall
    ///    sliding, stairs, acceleration, stamina. This is the "proper" path.
    ///
    ///  - Velocity (fallback): overrides the Rigidbody velocity each physics tick.
    ///    Works everywhere but doesn't get the controller's native collision
    ///    handling, so the bot adds wall-avoidance/sliding itself.
    ///
    /// The bot sets the desired direction/velocity + Active each tick; when
    /// inactive the patch does nothing and the human keeps full control.
    /// </summary>
    public static class MovementPatch
    {
        public enum MoveMode { Velocity, NativeInput }

        public static volatile bool Active;
        public static MoveMode Mode = MoveMode.NativeInput;

        /// <summary>Velocity mode: world-space target velocity (XZ; Y preserved).</summary>
        public static Vector3 DesiredVelocity;
        /// <summary>Native mode: desired input direction (already in the field's space), magnitude 0..1.</summary>
        public static Vector3 DesiredInput;

        // Velocity-mode tunables.
        public static float Acceleration = 14f;
        public static bool AutoStep = false;
        public static float StepUpSpeed = 2.8f;

        private static ManualLogSource _log;
        private static Rigidbody _rb;
        private static bool _installed;
        private static bool _nativeReady;
        private static float _blockedTimer;
        private static float _stepCooldown;
        private static Vector3 _lastPos;
        private static bool _hasLastPos;

        private static FieldInfo _inputDir;
        private static FieldInfo _inputRaw;

        public static bool Installed => _installed;
        public static bool NativeAvailable => _nativeReady;

        public static void Install(Harmony harmony, GameApi api, ManualLogSource log)
        {
            _log = log;
            var pcType = api.PlayerControllerType;
            if (pcType == null)
            {
                log.LogWarning("MovementPatch: 'PlayerController' type not found - movement disabled.");
                return;
            }

            _inputDir = api.InputDirectionField;
            _inputRaw = api.InputDirectionRawField;
            _nativeReady = _inputDir != null;

            try
            {
                MethodInfo fixedUpdate = DeclaredMethod(pcType, "FixedUpdate") ?? DeclaredMethod(pcType, "Update");
                MethodInfo update = DeclaredMethod(pcType, "Update");

                // Velocity mode: override rigidbody velocity AFTER the controller ran.
                if (fixedUpdate != null)
                    harmony.Patch(fixedUpdate, postfix: new HarmonyMethod(typeof(MovementPatch)
                        .GetMethod(nameof(AfterFixed), BindingFlags.Static | BindingFlags.NonPublic)));

                // Native-input mode: set InputDirection both right BEFORE the physics
                // movement runs (FixedUpdate prefix) and after input is read each frame
                // (Update postfix), so the controller's own movement uses our value
                // whichever way it reads input.
                if (_nativeReady && fixedUpdate != null)
                    harmony.Patch(fixedUpdate, prefix: new HarmonyMethod(typeof(MovementPatch)
                        .GetMethod(nameof(BeforeFixed), BindingFlags.Static | BindingFlags.NonPublic)));
                if (_nativeReady && update != null)
                    harmony.Patch(update, postfix: new HarmonyMethod(typeof(MovementPatch)
                        .GetMethod(nameof(AfterUpdate), BindingFlags.Static | BindingFlags.NonPublic)));

                _installed = fixedUpdate != null;
                log.LogInfo($"MovementPatch: installed. Mode={Mode}. native-input {( _nativeReady ? "available" : "UNAVAILABLE - InputDirection not found")}.");
            }
            catch (Exception ex)
            {
                log.LogError("MovementPatch: failed to apply Harmony patch: " + ex.Message);
            }
        }

        private static MethodInfo DeclaredMethod(Type t, string name) =>
            t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        // --- Native input mode: set the controller's input direction ---
        private static void WriteInput(MonoBehaviour pc)
        {
            if (_inputDir == null) return;
            try
            {
                _inputDir.SetValue(pc, DesiredInput);
                _inputRaw?.SetValue(pc, DesiredInput);
            }
            catch { /* ignore */ }
        }
        private static void AfterUpdate(MonoBehaviour __instance)
        {
            if (Active && Mode == MoveMode.NativeInput) WriteInput(__instance);
        }
        private static void BeforeFixed(MonoBehaviour __instance)
        {
            if (Active && Mode == MoveMode.NativeInput) WriteInput(__instance);
        }

        // --- Velocity mode: override the Rigidbody velocity ---
        private static void AfterFixed(MonoBehaviour __instance)
        {
            if (!Active || Mode != MoveMode.Velocity) return;
            if (_rb == null) _rb = FindBody(__instance);
            if (_rb == null || _rb.isKinematic) return;

            float dt = Time.fixedDeltaTime;
            Vector3 v = _rb.velocity;

            Vector3 curH = new Vector3(v.x, 0f, v.z);
            Vector3 tgtH = new Vector3(DesiredVelocity.x, 0f, DesiredVelocity.z);
            Vector3 newH = Vector3.MoveTowards(curH, tgtH, Acceleration * dt);

            // Actual movement from position change (rb.velocity is braked by the
            // controller, so it's not a reliable "am I moving" signal).
            Vector3 pos = _rb.position;
            float horiz = 0f;
            if (_hasLastPos)
            {
                Vector3 d = pos - _lastPos; d.y = 0f;
                horiz = d.magnitude / Mathf.Max(dt, 0.0001f);
            }
            _lastPos = pos;
            _hasLastPos = true;

            float y = v.y;
            float want = tgtH.magnitude;
            bool grounded = Mathf.Abs(v.y) < 1.5f;
            if (want > 0.5f && horiz < 0.4f && grounded) _blockedTimer += dt;
            else _blockedTimer = 0f;
            if (_stepCooldown > 0f) _stepCooldown -= dt;

            bool hopped = false;
            if (AutoStep && _blockedTimer >= 0.35f && _stepCooldown <= 0f && grounded)
            {
                y = StepUpSpeed;
                _blockedTimer = 0f;
                _stepCooldown = 0.7f;
                hopped = true;
            }

            // Unless we deliberately auto-stepped, NEVER write an upward velocity.
            // This kills the bounce-hopping: ramming into floor seams/steps can give
            // the player upward velocity, which we used to preserve and re-launch.
            if (!hopped) y = Mathf.Min(y, 0f);

            _rb.velocity = new Vector3(newH.x, y, newH.z);
        }

        private static Rigidbody FindBody(Component c)
        {
            if (c == null) return null;
            var rb = c.GetComponent<Rigidbody>()
                   ?? c.GetComponentInChildren<Rigidbody>()
                   ?? c.GetComponentInParent<Rigidbody>();
            if (rb == null) _log?.LogWarning("MovementPatch: no Rigidbody found on the player.");
            return rb;
        }

        public static void SetVelocity(Vector3 worldVelocity)
        {
            DesiredVelocity = worldVelocity;
            Active = true;
        }

        public static void SetInput(Vector3 inputDir)
        {
            DesiredInput = inputDir;
            Active = true;
        }

        public static void Release()
        {
            Active = false;
            DesiredInput = Vector3.zero;
            _blockedTimer = 0f;
            _stepCooldown = 0f;
            _hasLastPos = false;
        }
    }
}
