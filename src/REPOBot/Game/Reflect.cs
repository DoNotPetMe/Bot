using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace REPOBot.Game
{
    /// <summary>
    /// Small reflection toolkit used by the game adapter. Everything is resolved
    /// by trying a list of candidate names and cached. Each resolver remembers
    /// whether it succeeded so GameApi can print a one-shot diagnostics report -
    /// that report is your checklist for verifying symbols against the R.E.P.O.
    /// version you actually run.
    /// </summary>
    public static class Reflect
    {
        public const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static Assembly _gameAsm;

        /// <summary>The game's Assembly-CSharp, located once.</summary>
        public static Assembly GameAssembly
        {
            get
            {
                if (_gameAsm != null) return _gameAsm;
                _gameAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                return _gameAsm;
            }
        }

        /// <summary>Find a game type by trying several candidate simple names.</summary>
        public static Type FindType(params string[] candidates)
        {
            var asm = GameAssembly;
            if (asm == null) return null;
            foreach (var name in candidates)
            {
                var t = asm.GetTypes().FirstOrDefault(x => x.Name == name);
                if (t != null) return t;
            }
            return null;
        }

        public static FieldInfo Field(Type t, params string[] candidates)
        {
            if (t == null) return null;
            foreach (var n in candidates)
            {
                var f = t.GetField(n, Any);
                if (f != null) return f;
            }
            return null;
        }

        public static PropertyInfo Property(Type t, params string[] candidates)
        {
            if (t == null) return null;
            foreach (var n in candidates)
            {
                var p = t.GetProperty(n, Any);
                if (p != null) return p;
            }
            return null;
        }

        public static MethodInfo Method(Type t, params string[] candidates)
        {
            if (t == null) return null;
            foreach (var n in candidates)
            {
                var m = t.GetMethod(n, Any);
                if (m != null) return m;
            }
            return null;
        }

        /// <summary>Read a field-or-property value by candidate names; null if unresolved.</summary>
        public static object GetMember(object instance, Type declaring, params string[] candidates)
        {
            var f = Field(declaring, candidates);
            if (f != null) return f.GetValue(f.IsStatic ? null : instance);
            var p = Property(declaring, candidates);
            if (p != null && p.CanRead) return p.GetValue(p.GetGetMethod(true).IsStatic ? null : instance);
            return null;
        }

        public static bool TryGetFloat(object value, out float result)
        {
            result = 0f;
            if (value == null) return false;
            try { result = Convert.ToSingle(value); return true; }
            catch { return false; }
        }

        public static bool TryGetBool(object value, out bool result)
        {
            result = false;
            if (value is bool b) { result = b; return true; }
            return false;
        }
    }

    /// <summary>Records the resolution status of one logical symbol for diagnostics.</summary>
    public sealed class SymbolReport
    {
        public readonly List<(string symbol, bool ok, string detail)> Entries =
            new List<(string, bool, string)>();

        public T Track<T>(string symbol, T resolved, string detail = null) where T : class
        {
            Entries.Add((symbol, resolved != null, resolved != null ? (detail ?? "ok") : "MISSING - verify name"));
            return resolved;
        }

        public string Render()
        {
            var ok = Entries.Count(e => e.ok);
            var lines = Entries.Select(e => $"  [{(e.ok ? "OK " : "!! ")}] {e.symbol}: {e.detail}");
            return $"REPOBot symbol resolution ({ok}/{Entries.Count} resolved):\n" + string.Join("\n", lines);
        }

        public bool AllResolved => Entries.All(e => e.ok);
    }
}
