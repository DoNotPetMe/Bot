using UnityEngine;
using REPOBot.Core;

namespace REPOBot.Game
{
    /// <summary>
    /// Builds a clean <see cref="WorldSnapshot"/> from the live scene each tick
    /// using GameApi. This is the only bridge from game objects to the brain's
    /// plain data types, so the decision code never touches a game class.
    /// </summary>
    public sealed class WorldScanner
    {
        private readonly GameApi _api;
        private readonly WorldSnapshot _snap = new WorldSnapshot();

        public WorldScanner(GameApi api) => _api = api;

        public WorldSnapshot Scan()
        {
            _snap.Reset();

            var player = _api.LocalPlayer();
            if (player == null)
                return _snap; // Valid stays false

            var pt = player.transform;
            _snap.Valid = true;
            _snap.PlayerPos = pt.position;
            _snap.PlayerForward = pt.forward;
            _snap.PlayerHoldingValuable = _api.HoldingValuable(player);

            // Enemies
            foreach (var e in _api.FindAll(_api.EnemyType))
            {
                if (e == null) continue;
                var pos = e.transform.position;
                _snap.Enemies.Add(new EnemyView
                {
                    Transform = e.transform,
                    Pos = pos,
                    Distance = Vector3.Distance(pos, _snap.PlayerPos),
                    Alerted = _api.EnemyAlerted(e),
                    Name = e.gameObject.name
                });
            }

            // Valuables (uncollected ones still present in the scene)
            foreach (var v in _api.FindAll(_api.ValuableType))
            {
                if (v == null || !v.gameObject.activeInHierarchy) continue;
                var pos = v.transform.position;
                _snap.Valuables.Add(new ValuableView
                {
                    GameObject = v.gameObject,
                    Pos = pos,
                    Distance = Vector3.Distance(pos, _snap.PlayerPos),
                    Value = _api.ValuableValue(v)
                });
            }

            // Extraction point: choose the nearest active, not-yet-complete one.
            ExtractionView bestExtraction = null;
            float bestDist = float.PositiveInfinity;
            foreach (var x in _api.FindAll(_api.ExtractionType))
            {
                if (x == null) continue;
                if (_api.ExtractionComplete(x)) continue;
                if (!_api.ExtractionActive(x)) continue;
                var pos = x.transform.position;
                float d = Vector3.Distance(pos, _snap.PlayerPos);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestExtraction = new ExtractionView
                    {
                        GameObject = x.gameObject,
                        Pos = pos,
                        Distance = d,
                        Active = true,
                        Complete = false
                    };
                }
            }
            _snap.Extraction = bestExtraction;

            return _snap;
        }
    }
}
