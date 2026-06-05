using UnityEngine;
using REPOBot.Config;
using REPOBot.Core;
using REPOBot.Timing;

namespace REPOBot.UI
{
    /// <summary>
    /// Minimal IMGUI overlay showing the bot's live state: enabled/mode/phase,
    /// current target, the run timer and your best time (with a flash when a new
    /// record is set). Purely cosmetic - reads state, never drives the game.
    /// </summary>
    public sealed class BotHud : MonoBehaviour
    {
        public Settings Settings;
        public BotController Controller;

        private GUIStyle _box;
        private GUIStyle _label;
        private GUIStyle _record;

        private void EnsureStyles()
        {
            if (_box != null) return;
            _box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8) };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            _record = new GUIStyle(_label) { fontStyle = FontStyle.Bold };
        }

        private void OnGUI()
        {
            if (Settings == null || Controller == null) return;
            if (!Settings.ShowHud.Value) return;
            EnsureStyles();

            const float w = 260f;
            float h = 150f;
            var rect = new Rect(12, 12, w, h);
            GUI.Box(rect, "REPOBot", _box);

            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 26, w - 20, h - 30));

            string enabled = Settings.MasterEnabled.Value
                ? "<color=#7CFC7C>ON</color>" : "<color=#FF8080>OFF</color>";
            string mode = ColorMode(Settings.Mode.Value);
            GUILayout.Label($"State: {enabled}   Mode: {mode}", _label);
            GUILayout.Label($"Phase: {ColorPhase(Controller.Phase)}", _label);
            GUILayout.Label($"Target: {Controller.TargetLabel}", _label);

            if (Settings.ShowTimer.Value)
            {
                GUILayout.Label($"Time: <b>{RunTimer.Format(Controller.Timer.Elapsed)}</b>", _label);
                if (Settings.TrackBestTimes.Value)
                {
                    string best = Controller.LastBest.HasValue
                        ? RunTimer.Format(Controller.LastBest.Value) : "--:--.--";
                    GUILayout.Label($"Best: {best}", _label);
                }
            }

            if (Controller.NewRecordFlash)
                GUILayout.Label("<color=#FFD700>NEW RECORD!</color>", _record);

            GUILayout.EndArea();
        }

        private static string ColorMode(BotMode m)
        {
            switch (m)
            {
                case BotMode.SafeCollect: return "<color=#7CC4FF>SafeCollect</color>";
                case BotMode.Speedrun:    return "<color=#FFC04D>Speedrun</color>";
                default:                  return "<color=#AAAAAA>Off</color>";
            }
        }

        private static string ColorPhase(RunPhase p)
        {
            if (p == RunPhase.Flee) return "<color=#FF6060>FLEE</color>";
            if (p == RunPhase.Done) return "<color=#7CFC7C>Done</color>";
            return p.ToString();
        }
    }
}
