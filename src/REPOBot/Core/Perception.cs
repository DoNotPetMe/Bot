using System.Collections.Generic;
using UnityEngine;

namespace REPOBot.Core
{
    /// <summary>
    /// Engine-agnostic view of the world for one tick. The Game/ adapter fills
    /// this in from the live scene; the Brain/ classes consume it. Keeping this
    /// a plain data object means the decision logic has no game-version coupling.
    /// </summary>
    public sealed class WorldSnapshot
    {
        /// <summary>False if we could not locate the local player this tick.</summary>
        public bool Valid;

        public Vector3 PlayerPos;
        public Vector3 PlayerForward;

        /// <summary>True while the player is grabbing/holding a valuable.</summary>
        public bool PlayerHoldingValuable;

        public readonly List<EnemyView> Enemies = new List<EnemyView>();
        public readonly List<ValuableView> Valuables = new List<ValuableView>();

        /// <summary>Active extraction point, or null if none is currently live.</summary>
        public ExtractionView Extraction;

        /// <summary>Valuables that still need to be hauled (not yet extracted).</summary>
        public int ValuablesRemaining => Valuables.Count;

        public void Reset()
        {
            Valid = false;
            PlayerHoldingValuable = false;
            Enemies.Clear();
            Valuables.Clear();
            Extraction = null;
        }
    }

    public sealed class EnemyView
    {
        public Transform Transform;
        public Vector3 Pos;
        public float Distance;
        /// <summary>True if the enemy is hunting / has noticed a player.</summary>
        public bool Alerted;
        /// <summary>Optional display name for the HUD/log.</summary>
        public string Name;
    }

    public sealed class ValuableView
    {
        public GameObject GameObject;
        public Vector3 Pos;
        public float Distance;
        /// <summary>Estimated dollar value; 0 if unknown. Used for prioritisation.</summary>
        public float Value;
    }

    public sealed class ExtractionView
    {
        public GameObject GameObject;
        public Vector3 Pos;
        public float Distance;
        public bool Active;
        public bool Complete;
    }
}
