using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace REPOBot.Game
{
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
        public readonly Type EnemyType;
        public readonly Type ValuableType;
        public readonly Type ExtractionType;
        public readonly Type PhysGrabberType;
        public readonly Type RunManagerType;
        public readonly Type SemiFuncType;

        // Cached members (may be null; accessors handle that gracefully)
        private readonly MethodInfo _findObjectsOfType;     // UnityEngine.Object.FindObjectsOfType(Type)
        private readonly MemberInfoRef _playerIsLocal;      // PlayerAvatar.isLocal-ish
        private readonly MethodInfo _semiFuncLocalPlayer;   // SemiFunc.PlayerAvatarLocal()
        private readonly MemberInfoRef _playerGrabber;      // PlayerAvatar -> PhysGrabber
        private readonly MemberInfoRef _grabberHeldObject;  // PhysGrabber.grabbedObject
        private readonly MemberInfoRef _valuableValue;      // ValuableObject.dollarValue*
        private readonly MemberInfoRef _enemyOnHunt;        // Enemy hunting/alerted flag
        private readonly MemberInfoRef _extractionState;    // ExtractionPoint state/active flag
        private readonly MemberInfoRef _extractionComplete; // ExtractionPoint completed flag

        public bool Usable => PlayerAvatarType != null;

        public GameApi(ManualLogSource log)
        {
            _log = log;

            PlayerAvatarType = Report.Track("type PlayerAvatar",
                Reflect.FindType("PlayerAvatar", "PlayerController", "Player"));
            EnemyType = Report.Track("type Enemy",
                Reflect.FindType("EnemyParent", "Enemy", "EnemyMain"));
            ValuableType = Report.Track("type ValuableObject",
                Reflect.FindType("ValuableObject", "Valuable"));
            ExtractionType = Report.Track("type ExtractionPoint",
                Reflect.FindType("ExtractionPoint", "Extraction", "ExtractPoint"));
            PhysGrabberType = Report.Track("type PhysGrabber",
                Reflect.FindType("PhysGrabber", "PhysGrab"));
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

            _enemyOnHunt = MemberInfoRef.Resolve(Report, "Enemy.onHunt/alerted",
                EnemyType, "onHunt", "isHunting", "alerted", "hunting", "investigate");

            _extractionState = MemberInfoRef.Resolve(Report, "ExtractionPoint.active/state",
                ExtractionType, "isActive", "active", "currentState", "state", "extractionActive");

            _extractionComplete = MemberInfoRef.Resolve(Report, "ExtractionPoint.complete",
                ExtractionType, "isComplete", "complete", "extractionComplete", "haulComplete");
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
            if (player == null || _playerGrabber == null) return false;
            if (!_playerGrabber.TryGet(player, out var grabberObj) || grabberObj == null) return false;
            if (_grabberHeldObject == null) return false;
            if (!_grabberHeldObject.TryGet(grabberObj, out var held)) return false;
            // held may be a UnityEngine.Object (transform/component) or null.
            return held is UnityEngine.Object uo && uo != null;
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

        // --- Extraction ---

        public bool ExtractionActive(Component extraction)
        {
            if (extraction == null || _extractionState == null) return true; // assume usable if unknown
            if (!_extractionState.TryGet(extraction, out var v)) return true;
            if (v is bool b) return b;
            // If it is an enum/int state, treat non-zero "Idle/None" heuristically as active.
            return true;
        }

        public bool ExtractionComplete(Component extraction)
        {
            if (extraction == null || _extractionComplete == null) return false;
            return _extractionComplete.TryGet(extraction, out var v) && Reflect.TryGetBool(v, out var b) && b;
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
    }
}
