using System;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace REPOBot.Game
{
    /// <summary>
    /// On-demand introspection: dumps the real members of the game types the bot
    /// needs to drive (PhysGrabber, ValuableObject, ExtractionPoint, the cart,
    /// the player/camera rig) straight to the BepInEx log. Triggered by a hotkey
    /// so we can wire grabbing/cart logic against the EXACT names for whatever
    /// game version is running, instead of guessing.
    /// </summary>
    public static class ApiDump
    {
        public static void DumpAll(GameApi api, ManualLogSource log)
        {
            log.LogInfo("==================== REPOBot API DUMP (start) ====================");

            // 1) List every type whose name hints at the systems we care about,
            //    so we can find the real class names (esp. the cart).
            DumpTypeNames(log, "Cart");
            DumpTypeNames(log, "Grab");
            DumpTypeNames(log, "Extract");
            DumpTypeNames(log, "Valuable");

            // 2) Full member dumps of the key types.
            DumpType(log, api.PhysGrabberType, "PhysGrabber");
            DumpType(log, api.ValuableType, "ValuableObject");
            DumpType(log, api.ExtractionType, "ExtractionPoint");
            DumpType(log, api.PlayerControllerType, "PlayerController");
            DumpType(log, api.PlayerAvatarType, "PlayerAvatar");
            DumpType(log, api.EnemyType, "Enemy");

            // 3) The local player + camera rig at runtime, so we can find the aim
            //    transform we'll need to point at a valuable before grabbing.
            DumpCameraRig(api, log);

            log.LogInfo("==================== REPOBot API DUMP (end) ======================");
            log.LogInfo("Copy everything between the (start)/(end) markers and send it back.");
        }

        private static void DumpTypeNames(ManualLogSource log, string keyword)
        {
            var asm = Reflect.GameAssembly;
            if (asm == null) { log.LogInfo($"[types ~ '{keyword}'] Assembly-CSharp not found"); return; }
            string[] names;
            try
            {
                names = asm.GetTypes()
                    .Where(t => t.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(t => t.Name).OrderBy(n => n).ToArray();
            }
            catch (ReflectionTypeLoadException ex)
            {
                names = ex.Types.Where(t => t != null && t.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(t => t.Name).OrderBy(n => n).ToArray();
            }
            log.LogInfo($"[types containing '{keyword}'] {string.Join(", ", names)}");
        }

        private static void DumpType(ManualLogSource log, Type t, string label)
        {
            if (t == null) { log.LogInfo($"---- {label}: TYPE NOT FOUND ----"); return; }

            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var sb = new StringBuilder();
            sb.AppendLine($"---- {label}  (actual type: {t.Name}) ----");

            foreach (var f in t.GetFields(F).OrderBy(x => x.Name))
                sb.AppendLine($"  F {Short(f.FieldType)} {f.Name}");

            foreach (var p in t.GetProperties(F).OrderBy(x => x.Name))
                sb.AppendLine($"  P {Short(p.PropertyType)} {p.Name}");

            foreach (var m in t.GetMethods(F).Where(x => !x.IsSpecialName).OrderBy(x => x.Name))
            {
                var ps = string.Join(", ", m.GetParameters().Select(p => Short(p.ParameterType)));
                sb.AppendLine($"  M {Short(m.ReturnType)} {m.Name}({ps})");
            }

            log.LogInfo(sb.ToString());
        }

        private static void DumpCameraRig(GameApi api, ManualLogSource log)
        {
            var player = api.LocalPlayer();
            var sb = new StringBuilder();
            sb.AppendLine("---- Camera / aim rig ----");
            var cam = Camera.main;
            sb.AppendLine($"  Camera.main: {(cam ? cam.name : "null")}");
            if (cam != null)
            {
                var path = cam.transform;
                int depth = 0;
                while (path != null && depth < 8)
                {
                    var comps = string.Join(", ", path.GetComponents<Component>().Select(c => c ? c.GetType().Name : "null"));
                    sb.AppendLine($"  cam parent[{depth}] '{path.name}' : {comps}");
                    path = path.parent;
                    depth++;
                }
            }
            if (player != null)
                sb.AppendLine($"  LocalPlayer GO: '{player.gameObject.name}' components: " +
                              string.Join(", ", player.GetComponents<Component>().Select(c => c ? c.GetType().Name : "null")));
            log.LogInfo(sb.ToString());
        }

        private static string Short(Type t) => t == null ? "?" : t.Name;
    }
}
