using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Logging;

namespace REPOBot.Timing
{
    /// <summary>
    /// Persists the best completion time per level to a small text file next to
    /// the BepInEx config. Format is one "levelKey\tseconds" record per line, so
    /// there is no dependency on a JSON library and the file is human-readable.
    /// </summary>
    public sealed class BestTimeStore
    {
        private readonly string _path;
        private readonly ManualLogSource _log;
        private readonly Dictionary<string, float> _best = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public BestTimeStore(string directory, ManualLogSource log)
        {
            _log = log;
            _path = Path.Combine(directory, "REPOBot.besttimes.tsv");
            Load();
        }

        /// <summary>Best seconds for a level, or null if no record yet.</summary>
        public float? Get(string levelKey)
        {
            if (string.IsNullOrEmpty(levelKey)) return null;
            return _best.TryGetValue(levelKey, out var v) ? v : (float?)null;
        }

        /// <summary>
        /// Records a finish. Returns true if it is a new best (and saves to disk).
        /// </summary>
        public bool Submit(string levelKey, float seconds)
        {
            if (string.IsNullOrEmpty(levelKey) || seconds <= 0f)
                return false;

            if (_best.TryGetValue(levelKey, out var prev) && prev <= seconds)
                return false;

            _best[levelKey] = seconds;
            Save();
            return true;
        }

        public void Clear(string levelKey)
        {
            if (!string.IsNullOrEmpty(levelKey) && _best.Remove(levelKey))
                Save();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                foreach (var raw in File.ReadAllLines(_path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int tab = line.LastIndexOf('\t');
                    if (tab <= 0) continue;
                    var key = line.Substring(0, tab);
                    if (float.TryParse(line.Substring(tab + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var secs))
                        _best[key] = secs;
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"Could not load best-times file: {ex.Message}");
            }
        }

        private void Save()
        {
            try
            {
                using (var w = new StreamWriter(_path, false))
                {
                    w.WriteLine("# REPOBot best times  (levelKey<TAB>seconds)");
                    foreach (var kv in _best)
                        w.WriteLine(kv.Key + "\t" + kv.Value.ToString("0.###", CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"Could not save best-times file: {ex.Message}");
            }
        }
    }
}
