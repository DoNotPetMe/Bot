using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace REPOBot.Game
{
    /// <summary>
    /// Drives the local player by overriding its Rigidbody velocity every physics
    /// tick, via a Harmony postfix on PlayerController's update method. This is
    /// deliberately robust: it doesn't depend on the game's private input-field
    /// names (which change between versions) - it just overwrites whatever
    /// velocity the controller computed with the bot's desired velocity.
    ///
    /// The bot sets <see cref="DesiredVelocity"/> + <see cref="Active"/> each tick;
    /// when inactive the patch does nothing and the human keeps full control.
    /// </summary>
    public static class MovementPatch
    {
        public static volatile bool Active;
        /// <summary>World-space target velocity (XZ used; Y preserved for gravity).</summary>
        public static Vector3 DesiredVelocity;

        private static ManualLogSource _log;
        private static Rigidbody _rb;
        private static bool _installed;

        public static bool Installed => _installed;
        public static bool HasBody => _rb != null;

        public static void Install(Harmony harmony, GameApi api, ManualLogSource log)
        {
            _log = log;
            var pcType = api.PlayerControllerType;
            if (pcType == null)
            {
                log.LogWarning("MovementPatch: 'PlayerController' type not found - direct movement disabled.");
                return;
            }

            // Prefer FixedUpdate (physics), then Update / LateUpdate.
            MethodInfo target = DeclaredMethod(pcType, "FixedUpdate")
                             ?? DeclaredMethod(pcType, "Update")
                             ?? DeclaredMethod(pcType, "LateUpdate");
            if (target == null)
            {
                log.LogWarning("MovementPatch: PlayerController has no Update/FixedUpdate to hook - movement disabled.");
                return;
            }

            try
            {
                var postfix = new HarmonyMethod(typeof(MovementPatch)
                    .GetMethod(nameof(AfterTick), BindingFlags.Static | BindingFlags.NonPublic));
                harmony.Patch(target, postfix: postfix);
                _installed = true;
                log.LogInfo($"MovementPatch: hooked PlayerController.{target.Name} for movement control.");
            }
            catch (Exception ex)
            {
                log.LogError("MovementPatch: failed to apply Harmony patch: " + ex.Message);
            }
        }

        private static MethodInfo DeclaredMethod(Type t, string name) =>
            t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        // Harmony postfix - runs right after the controller's own movement code.
        private static void AfterTick(MonoBehaviour __instance)
        {
            if (!Active) return;
            if (_rb == null) _rb = FindBody(__instance);
            if (_rb == null || _rb.isKinematic) return;

            Vector3 v = _rb.velocity;
            _rb.velocity = new Vector3(DesiredVelocity.x, v.y, DesiredVelocity.z);
        }

        private static Rigidbody FindBody(Component c)
        {
            if (c == null) return null;
            var rb = c.GetComponent<Rigidbody>()
                   ?? c.GetComponentInChildren<Rigidbody>()
                   ?? c.GetComponentInParent<Rigidbody>();
            if (rb == null) _log?.LogWarning("MovementPatch: no Rigidbody found on the player - cannot drive movement.");
            return rb;
        }

        public static void SetVelocity(Vector3 worldVelocity)
        {
            DesiredVelocity = worldVelocity;
            Active = true;
        }

        public static void Release() => Active = false;
    }
}
