using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace REPOBot.Game
{
    /// <summary>Result of a grab-reachability check.</summary>
    public enum GrabCheck { Ok, OutOfRange, Blocked, BlockedByDoor }

    /// <summary>
    /// THE place to verify symbols. Every reference to a R.E.P.O. internal type,
    /// field, property or method is declared here with a list of candidate names.
    /// If R.E.P.O. updates and something breaks, run the game once, read the
    /// "symbol resolution" report in the BepInEx log, and fix the candidate list
    /// for whatever shows "!!". Nothing else in the codebase touches game types.
    ///
    /// Names below are drawn from the community decompile of Assembly-CSharp
    /// (PlayerAvatar / EnemyDirector / PhysGrabber / ValuableObject /
    /// ExtractionPoint / RunManager / SemiFunc). Treat the first candidate as the
    /// best guess and the rest as fallbacks - VERIFY against your build with
    /// dnSpy / ILSpy / DLL Muster.
    /// </summary>
    public sealed class GameApi
    {
        private readonly ManualLogSource _log;
        public readonly SymbolReport Report = new SymbolReport();

        // Resolved game types
        public readonly Type PlayerAvatarType;
        public readonly Type PlayerControllerType;
        public readonly Type EnemyType;
        public readonly Type ValuableType;
        public readonly Type ExtractionType;
        public readonly Type PhysGrabberType;
        public readonly Type PhysGrabObjectType;
        public readonly Type PhysGrabHingeType;
        public readonly Type RunManagerType;
        public readonly Type SemiFuncType;

        // Cached members (may be null; accessors handle that gracefully)
        private readonly MethodInfo _findObjectsOfType;     // UnityEngine.Object.FindObjectsOfType(Type)
        private readonly MemberInfoRef _playerIsLocal;      // PlayerAvatar.isLocal-ish
        private readonly MethodInfo _semiFuncLocalPlayer;   // SemiFunc.PlayerAvatarLocal()
        private readonly MemberInfoRef _playerGrabber;      // PlayerAvatar -> PhysGrabber
        private readonly MemberInfoRef _grabberHeldObject;  // PhysGrabber.grabbedObject
        private readonly MemberInfoRef _valuableValue;      // ValuableObject.dollarValue*
        private readonly MemberInfoRef _enemyOnHunt;        // EnemyParent.playerVeryClose/playerClose
        private readonly MemberInfoRef _extractionState;    // ExtractionPoint.currentState (State enum)

        // Grabbing
        private readonly MethodInfo _forceGrab;             // PhysGrabber.ForceGrabPhysObject(PhysGrabObject)
        private readonly MethodInfo _overrideGrab;          // PhysGrabber.OverrideGrab(PhysGrabObject, float, bool)
        private readonly MethodInfo _releaseObject;         // PhysGrabber.ReleaseObject(int, float)
        private readonly MemberInfoRef _valuablePhysObject; // ValuableObject.physGrabObject (PhysGrabObject)
        private readonly MemberInfoRef _grabbedFlag;        // PhysGrabber.grabbed (bool)
        private readonly MemberInfoRef _grabbedPhysObj;     // PhysGrabber.grabbedPhysGrabObject
        private readonly MemberInfoRef _grabRangeField;     // PhysGrabber.grabRange (float)
        private readonly MemberInfoRef _pcInstance;         // PlayerController.instance (static)
        private readonly MemberInfoRef _jumpBuffer;         // PlayerController.JumpInputBuffer (float)

        public bool Usable => PlayerAvatarType != null;

        public GameApi(ManualLogSource log)
        {
            _log = log;

            PlayerAvatarType = Report.Track("type PlayerAvatar",
                Reflect.FindType("PlayerAvatar", "Player"));
            PlayerControllerType = Report.Track("type PlayerController",
                Reflect.FindType("PlayerController", "PlayerMovement", "PlayerControllerMovement"));
            EnemyType = Report.Track("type Enemy",
                Reflect.FindType("EnemyParent", "Enemy", "EnemyMain"));
            ValuableType = Report.Track("type ValuableObject",
                Reflect.FindType("ValuableObject", "Valuable"));
            ExtractionType = Report.Track("type ExtractionPoint",
                Reflect.FindType("ExtractionPoint", "Extraction", "ExtractPoint"));
            PhysGrabberType = Report.Track("type PhysGrabber",
                Reflect.FindType("PhysGrabber", "PhysGrab"));
            PhysGrabObjectType = Report.Track("type PhysGrabObject",
                Reflect.FindType("PhysGrabObject"));
            PhysGrabHingeType = Report.Track("type PhysGrabHinge",
                Reflect.FindType("PhysGrabHinge"));
            RunManagerType = Report.Track("type RunManager",
                Reflect.FindType("RunManager", "GameDirector", "RunDirector"));
            SemiFuncType = Report.Track("type SemiFunc",
                Reflect.FindType("SemiFunc"));

            // UnityEngine.Object.FindObjectsOfType(Type) - stable Unity API.
            _findObjectsOfType = typeof(UnityEngine.Object).GetMethod(
                "FindObjectsOfType", new[] { typeof(Type) });
            Report.Track("UnityEngine.Object.FindObjectsOfType", _findObjectsOfType);

            _semiFuncLocalPlayer = Report.Track("SemiFunc.PlayerAvatarLocal()",
                Reflect.Method(SemiFuncType, "PlayerAvatarLocal", "GetLocalPlayer", "PlayerAvatarLocalGet"));

            _playerIsLocal = MemberInfoRef.Resolve(Report, "PlayerAvatar.isLocal",
                PlayerAvatarType, "isLocal", "localPlayer", "isLocalPlayer", "IsLocal");

            _playerGrabber = MemberInfoRef.Resolve(Report, "PlayerAvatar.physGrabber",
                PlayerAvatarType, "physGrabber", "physGrab", "grabber", "PhysGrabber");

            _grabberHeldObject = MemberInfoRef.Resolve(Report, "PhysGrabber.grabbedObject",
                PhysGrabberType, "grabbedObject", "grabbedObjectTransform", "currentlyGrabbing", "heldObject");

            _valuableValue = MemberInfoRef.Resolve(Report, "ValuableObject.dollarValue",
                ValuableType, "dollarValueCurrent", "dollarValue", "dollarValueOriginal", "value");

            _enemyOnHunt = MemberInfoRef.Resolve(Report, "EnemyParent.playerVeryClose",
                EnemyType, "playerVeryClose", "playerClose", "onHunt", "isHunting", "alerted");

            _extractionState = MemberInfoRef.Resolve(Report, "ExtractionPoint.currentState",
                ExtractionType, "currentState", "state", "stateSetTo");

            _valuablePhysObject = MemberInfoRef.Resolve(Report, "ValuableObject.physGrabObject",
                ValuableType, "physGrabObject");

            _forceGrab = Report.Track("PhysGrabber.ForceGrabPhysObject",
                Reflect.Method(PhysGrabberType, "ForceGrabPhysObject"));
            _overrideGrab = Report.Track("PhysGrabber.OverrideGrab",
                Reflect.Method(PhysGrabberType, "OverrideGrab"));
            _releaseObject = Report.Track("PhysGrabber.ReleaseObject",
                Reflect.Method(PhysGrabberType, "ReleaseObject"));

            _grabbedFlag = MemberInfoRef.Resolve(Report, "PhysGrabber.grabbed",
                PhysGrabberType, "grabbed");
            _grabbedPhysObj = MemberInfoRef.Resolve(Report, "PhysGrabber.grabbedPhysGrabObject",
                PhysGrabberType, "grabbedPhysGrabObject");
            _grabRangeField = MemberInfoRef.Resolve(Report, "PhysGrabber.grabRange",
                PhysGrabberType, "grabRange");
            _pcInstance = MemberInfoRef.Resolve(Report, "PlayerController.instance",
                PlayerControllerType, "instance");
            _jumpBuffer = MemberInfoRef.Resolve(Report, "PlayerController.JumpInputBuffer",
                PlayerControllerType, "JumpInputBuffer", "JumpInputBufferTimer", "JumpGroundedBuffer");
        }

        public void LogDiagnostics()
        {
            _log.LogInfo(Report.Render());
            if (!Report.AllResolved)
                _log.LogWarning("Some R.E.P.O. symbols did not resolve. The bot will still run but " +
                                "the affected behaviour is degraded. See README 'Verifying symbols'.");
        }

        // --- Object enumeration ---

        /// <summary>FindObjectsOfType(type) -> array; empty if unavailable.</summary>
        public Component[] FindAll(Type type)
        {
            if (type == null || _findObjectsOfType == null) return Array.Empty<Component>();
            try
            {
                var arr = _findObjectsOfType.Invoke(null, new object[] { type }) as Array;
                if (arr == null) return Array.Empty<Component>();
                var result = new Component[arr.Length];
                for (int i = 0; i < arr.Length; i++) result[i] = arr.GetValue(i) as Component;
                return result;
            }
            catch (Exception ex)
            {
                _log.LogWarning($"FindAll({type?.Name}) failed: {ex.Message}");
                return Array.Empty<Component>();
            }
        }

        // --- Player ---

        /// <summary>Best-effort lookup of the local player's PlayerAvatar component.</summary>
        public Component LocalPlayer()
        {
            // 1) SemiFunc helper, if present.
            if (_semiFuncLocalPlayer != null)
            {
                try
                {
                    var p = _semiFuncLocalPlayer.Invoke(null, null) as Component;
                    if (p != null) return p;
                }
                catch { /* fall through */ }
            }

            // 2) Scan PlayerAvatars for the one flagged local.
            foreach (var pc in FindAll(PlayerAvatarType))
            {
                if (pc == null) continue;
                if (_playerIsLocal != null && _playerIsLocal.TryGet(pc, out var v) && v is bool b && b)
                    return pc;
            }

            // 3) Last resort: if there is exactly one, assume it is us (solo play).
            var all = FindAll(PlayerAvatarType);
            return all.Length == 1 ? all[0] : null;
        }

        public bool HoldingValuable(Component player)
        {
            var grabber = GetPhysGrabber(player);
            if (grabber == null) return false;
            // Holding if ANY of these say so (most reliable first): the grabbed
            // Rigidbody, the grabbed PhysGrabObject, or the 'grabbed' bool.
            if (_grabberHeldObject != null && _grabberHeldObject.TryGet(grabber, out var ho)
                && ho is UnityEngine.Object u1 && u1 != null) return true;
            if (_grabbedPhysObj != null && _grabbedPhysObj.TryGet(grabber, out var po)
                && po is UnityEngine.Object u2 && u2 != null) return true;
            if (_grabbedFlag != null && _grabbedFlag.TryGet(grabber, out var gv) && gv is bool gb && gb)
                return true;
            return false;
        }

        /// <summary>The grabber's configured grab range, or the fallback if unknown.</summary>
        public float GrabRange(Component grabber, float fallback)
        {
            if (grabber != null && _grabRangeField != null &&
                _grabRangeField.TryGet(grabber, out var v) && Reflect.TryGetFloat(v, out var f) && f > 0f)
                return f;
            return fallback;
        }

        /// <summary>
        /// True only if the valuable is within <paramref name="maxRange"/> of the
        /// camera AND there's clear line of sight to it (nothing solid in between).
        /// Prevents grabbing through walls/doors (which yanks and breaks items).
        /// </summary>
        /// <summary>
        /// Checks whether the valuable can be grabbed right now. If a hinged door
        /// (fridge/cupboard/drawer) is in the way, returns BlockedByDoor and the
        /// door's PhysGrabObject in <paramref name="doorToOpen"/> so the bot can
        /// open it first.
        /// </summary>
        public GrabCheck CheckGrab(Component player, Component valuable, Vector3 itemPos, float maxRange, out Component doorToOpen)
        {
            doorToOpen = null;
            if (player == null || valuable == null) return GrabCheck.Blocked;

            var cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : player.transform.position + Vector3.up * 1.4f;
            Vector3 to = itemPos - eye;
            float dist = to.magnitude;
            if (dist > maxRange) return GrabCheck.OutOfRange;
            if (dist < 0.05f) return GrabCheck.Ok;

            Transform vTr = valuable.transform;
            Transform physTr = GetValuablePhysObject(valuable) is Component pc ? pc.transform : null;
            Transform pRoot = player.transform.root;

            var hits = Physics.RaycastAll(eye, to / dist, dist + 0.25f, ~0, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return GrabCheck.Ok;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var h in hits)
            {
                var t = h.collider != null ? h.collider.transform : null;
                if (t == null) continue;
                if (pRoot != null && t.IsChildOf(pRoot)) continue;           // ignore the player's own colliders
                if (IsPartOf(t, vTr) || IsPartOf(t, physTr)) return GrabCheck.Ok; // first solid thing IS the item

                // Something blocks it. If it's a hinged door, report it as openable.
                doorToOpen = FindOpenableDoor(t);
                return doorToOpen != null ? GrabCheck.BlockedByDoor : GrabCheck.Blocked;
            }
            return GrabCheck.Ok;
        }

        /// <summary>If the blocking collider belongs to a hinged grabbable (a
        /// fridge/cupboard/drawer door), returns that door's PhysGrabObject.</summary>
        private Component FindOpenableDoor(Transform blocker)
        {
            if (blocker == null || PhysGrabObjectType == null) return null;
            // Must be hinged - that's what fridge/cupboard/drawer doors are.
            if (PhysGrabHingeType != null && blocker.GetComponentInParent(PhysGrabHingeType) == null)
                return null;
            return blocker.GetComponentInParent(PhysGrabObjectType);
        }

        private static bool IsPartOf(Transform t, Transform root)
        {
            if (t == null || root == null) return false;
            return t == root || t.IsChildOf(root) || root.IsChildOf(t);
        }

        /// <summary>Best-effort jump (buffers a jump input on the PlayerController).</summary>
        public void TryJump()
        {
            if (_pcInstance == null || _jumpBuffer == null) return;
            if (!_pcInstance.TryGet(null, out var inst) || inst == null) return;
            _jumpBuffer.TrySet(inst, 0.2f);
        }

        // --- Valuables ---

        public float ValuableValue(Component valuable)
        {
            if (valuable == null || _valuableValue == null) return 0f;
            return _valuableValue.TryGet(valuable, out var v) && Reflect.TryGetFloat(v, out var f) ? f : 0f;
        }

        // --- Enemies ---

        public bool EnemyAlerted(Component enemy)
        {
            if (enemy == null || _enemyOnHunt == null) return false;
            return _enemyOnHunt.TryGet(enemy, out var v) && Reflect.TryGetBool(v, out var b) && b;
        }

        // --- Grabbing ---

        /// <summary>The local player's PhysGrabber component, or null.</summary>
        public Component GetPhysGrabber(Component player)
        {
            if (player == null || _playerGrabber == null) return null;
            return _playerGrabber.TryGet(player, out var g) ? g as Component : null;
        }

        /// <summary>The PhysGrabObject of a ValuableObject (what you pass to a grab).</summary>
        public object GetValuablePhysObject(Component valuable)
        {
            if (valuable == null || _valuablePhysObject == null) return null;
            return _valuablePhysObject.TryGet(valuable, out var po) ? po : null;
        }

        /// <summary>Force-grab a specific PhysGrabObject. Returns false if no API.</summary>
        public bool TryGrab(Component grabber, object physObject)
        {
            if (grabber == null || !(physObject is UnityEngine.Object uo) || uo == null) return false;
            try
            {
                if (_forceGrab != null) { _forceGrab.Invoke(grabber, new[] { physObject }); return true; }
                if (_overrideGrab != null) { _overrideGrab.Invoke(grabber, new object[] { physObject, 1f, false }); return true; }
            }
            catch (Exception ex) { _log.LogWarning("TryGrab failed: " + ex.Message); }
            return false;
        }

        /// <summary>Release whatever the grabber is holding (best-effort).</summary>
        public void Release(Component grabber)
        {
            if (grabber == null || _releaseObject == null) return;
            try { _releaseObject.Invoke(grabber, new object[] { 0, 0f }); }
            catch (Exception ex) { _log.LogWarning("Release failed: " + ex.Message); }
        }

        // --- Extraction ---

        /// <summary>Reads ExtractionPoint.currentState and returns its enum name (or "").</summary>
        private string ExtractionStateName(Component extraction)
        {
            if (extraction == null || _extractionState == null) return "";
            return _extractionState.TryGet(extraction, out var v) && v != null ? v.ToString() : "";
        }

        public bool ExtractionComplete(Component extraction)
        {
            var s = ExtractionStateName(extraction);
            return s.IndexOf("Complete", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Active = not idle and not complete. Unknown state -> treat as usable.</summary>
        public bool ExtractionActive(Component extraction)
        {
            var s = ExtractionStateName(extraction);
            if (s.Length == 0) return true;
            if (ExtractionComplete(extraction)) return false;
            return s.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) < 0;
        }
    }

    /// <summary>A resolved field-or-property accessor that reads safely.</summary>
    public sealed class MemberInfoRef
    {
        private readonly FieldInfo _field;
        private readonly PropertyInfo _prop;

        private MemberInfoRef(FieldInfo f, PropertyInfo p) { _field = f; _prop = p; }

        public static MemberInfoRef Resolve(SymbolReport report, string label, Type t, params string[] candidates)
        {
            var f = Reflect.Field(t, candidates);
            var p = f == null ? Reflect.Property(t, candidates) : null;
            report.Track(label, (object)f ?? p);
            return (f == null && p == null) ? null : new MemberInfoRef(f, p);
        }

        public bool TryGet(object instance, out object value)
        {
            value = null;
            try
            {
                if (_field != null) { value = _field.GetValue(_field.IsStatic ? null : instance); return true; }
                if (_prop != null && _prop.CanRead) { value = _prop.GetValue(instance); return true; }
            }
            catch { /* ignore */ }
            return false;
        }

        public bool TrySet(object instance, object value)
        {
            try
            {
                if (_field != null) { _field.SetValue(_field.IsStatic ? null : instance, value); return true; }
                if (_prop != null && _prop.CanWrite) { _prop.SetValue(instance, value); return true; }
            }
            catch { /* ignore */ }
            return false;
        }
    }
}
