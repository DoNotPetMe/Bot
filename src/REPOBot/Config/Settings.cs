using BepInEx.Configuration;
using REPOBot.Core;
using UnityEngine;

namespace REPOBot.Config
{
    /// <summary>
    /// All user-tunable settings, bound to BepInEx config so they can be edited
    /// in the .cfg file or live via a config-manager mod. Every value the brain
    /// reads at runtime comes from here, so behaviour is fully configurable
    /// without recompiling.
    /// </summary>
    public sealed class Settings
    {
        // --- Master ---
        public readonly ConfigEntry<bool> MasterEnabled;
        public readonly ConfigEntry<BotMode> Mode;

        // --- Hotkeys ---
        public readonly ConfigEntry<KeyCode> KeyToggle;     // enable/disable the bot
        public readonly ConfigEntry<KeyCode> KeyCycleMode;  // SafeCollect <-> Speedrun
        public readonly ConfigEntry<KeyCode> KeyPanic;      // instant off + hands back to player
        public readonly ConfigEntry<KeyCode> KeyResetBest;  // clear best time for this level
        public readonly ConfigEntry<KeyCode> KeyDumpApi;    // dump game API to log (diagnostics)

        // --- Movement / pace ---
        public readonly ConfigEntry<float> MoveIntensity;       // 0..1, how hard to push the stick
        public readonly ConfigEntry<float> WalkSpeed;           // m/s target velocity when walking
        public readonly ConfigEntry<float> SprintSpeed;         // m/s target velocity when sprinting
        public readonly ConfigEntry<bool> AllowSprint;          // permit sprinting when safe
        public readonly ConfigEntry<float> ArriveRadius;        // how close counts as "reached"
        public readonly ConfigEntry<float> StuckSeconds;        // replan if no progress this long
        public readonly ConfigEntry<float> RepathInterval;      // seconds between path recomputes

        // --- Threat / monster avoidance ---
        public readonly ConfigEntry<float> DangerRadius;        // start steering away inside this
        public readonly ConfigEntry<float> FleeRadius;          // drop everything and flee inside this
        public readonly ConfigEntry<float> AvoidWeight;         // strength of monster repulsion
        public readonly ConfigEntry<float> AlertedMultiplier;   // extra caution for hunting monsters

        // --- Collection / extraction policy ---
        public readonly ConfigEntry<bool> CollectAll;           // try to grab every valuable...
        public readonly ConfigEntry<float> MinValueToDetour;    // ...or only those worth >= this
        public readonly ConfigEntry<float> ExtractWhenCarrying;  // haul once carrying this many $
        public readonly ConfigEntry<float> GrabReachSeconds;     // dwell at an item before giving up
        public readonly ConfigEntry<float> UnreachableSkipSeconds; // how long to ignore a skipped item
        public readonly ConfigEntry<float> DeliveredRadius;      // valuables within this of extraction = delivered
        public readonly ConfigEntry<float> GrabRange;            // only grab within this distance (LOS-gated)
        public readonly ConfigEntry<float> GrabRetrySeconds;     // min time between grab attempts
        public readonly ConfigEntry<int> GrabMaxAttempts;        // give up after this many tries
        public readonly ConfigEntry<float> FailedGrabSkipSeconds; // ignore an un-grabbable item this long
        public readonly ConfigEntry<bool> JumpWhenStuck;         // try jumping when stuck on something

        // --- Timing / records ---
        public readonly ConfigEntry<bool> ShowTimer;
        public readonly ConfigEntry<bool> TrackBestTimes;
        public readonly ConfigEntry<bool> BeatBestMode;         // push harder when behind PB pace
        public readonly ConfigEntry<float> BeatBestAggression;   // how much harder (0..1)

        // --- HUD / diagnostics ---
        public readonly ConfigEntry<bool> ShowHud;
        public readonly ConfigEntry<bool> VerboseLogging;
        public readonly ConfigEntry<bool> DiagnosticsOnLoad;    // log resolved game symbols on start

        public Settings(ConfigFile cfg)
        {
            MasterEnabled = cfg.Bind("00 General", "Enabled", false,
                "Master switch. When false the bot never takes control. Toggle in-game with the hotkey.");
            Mode = cfg.Bind("00 General", "Mode", BotMode.SafeCollect,
                "Active behaviour profile. SafeCollect = careful/professional. Speedrun = fastest possible. Off = observe only.");

            KeyToggle = cfg.Bind("01 Hotkeys", "Toggle", KeyCode.F8,
                "Enable/disable the bot taking control.");
            KeyCycleMode = cfg.Bind("01 Hotkeys", "CycleMode", KeyCode.F9,
                "Cycle between SafeCollect and Speedrun.");
            KeyPanic = cfg.Bind("01 Hotkeys", "Panic", KeyCode.F10,
                "Immediately disable the bot and return control to you.");
            KeyResetBest = cfg.Bind("01 Hotkeys", "ResetBest", KeyCode.F11,
                "Clear the saved best time for the current level.");
            KeyDumpApi = cfg.Bind("01 Hotkeys", "DumpApi", KeyCode.F7,
                "Dump the game's real API (PhysGrabber, cart, extraction, camera rig) to the log for diagnostics.");

            MoveIntensity = cfg.Bind("02 Movement", "MoveIntensity", 1f,
                new ConfigDescription("Overall speed multiplier (0..1).", new AcceptableValueRange<float>(0f, 1f)));
            WalkSpeed = cfg.Bind("02 Movement", "WalkSpeed", 4f,
                "Target velocity (m/s) the bot drives at when walking. Tune to match your game feel.");
            SprintSpeed = cfg.Bind("02 Movement", "SprintSpeed", 7f,
                "Target velocity (m/s) the bot drives at when sprinting.");
            AllowSprint = cfg.Bind("02 Movement", "AllowSprint", true,
                "Let the bot sprint when it judges the path safe.");
            ArriveRadius = cfg.Bind("02 Movement", "ArriveRadius", 1.4f,
                "Distance (m) at which a target counts as reached.");
            StuckSeconds = cfg.Bind("02 Movement", "StuckSeconds", 1.5f,
                "If the bot makes no forward progress for this long, it replans.");
            RepathInterval = cfg.Bind("02 Movement", "RepathInterval", 0.4f,
                "Seconds between navmesh path recomputes.");

            DangerRadius = cfg.Bind("03 Threat", "DangerRadius", 9f,
                "Begin steering away from a monster once it is within this many metres.");
            FleeRadius = cfg.Bind("03 Threat", "FleeRadius", 4.5f,
                "Abandon the current goal and flee when a monster is within this many metres.");
            AvoidWeight = cfg.Bind("03 Threat", "AvoidWeight", 2.2f,
                "Strength of the steer-away force relative to the steer-toward force.");
            AlertedMultiplier = cfg.Bind("03 Threat", "AlertedMultiplier", 1.8f,
                "Extra avoidance applied to monsters that are actively hunting.");

            CollectAll = cfg.Bind("04 Objectives", "CollectAll", true,
                "Try to collect every valuable on the level before extracting.");
            MinValueToDetour = cfg.Bind("04 Objectives", "MinValueToDetour", 0f,
                "When CollectAll is false, only detour for valuables worth at least this much.");
            ExtractWhenCarrying = cfg.Bind("04 Objectives", "ExtractWhenCarrying", 0f,
                "Head to extraction once carrying at least this much value (0 = only when nothing left to grab).");
            GrabReachSeconds = cfg.Bind("04 Objectives", "GrabReachSeconds", 1.5f,
                "How long to pause at a valuable before giving up on it (grabbing isn't wired yet, so it then moves on).");
            UnreachableSkipSeconds = cfg.Bind("04 Objectives", "UnreachableSkipSeconds", 25f,
                "How long to ignore a valuable the bot couldn't collect/reach before trying it again.");
            DeliveredRadius = cfg.Bind("04 Objectives", "DeliveredRadius", 3.5f,
                "Valuables within this distance of the extraction point are treated as already delivered.");
            GrabRange = cfg.Bind("04 Objectives", "GrabRange", 2.5f,
                "Only grab a valuable when it is within this distance AND in clear line of sight (prevents grabbing through walls/doors).");
            GrabRetrySeconds = cfg.Bind("04 Objectives", "GrabRetrySeconds", 0.75f,
                "Minimum time between grab attempts (stops the constant grab-sound spam).");
            GrabMaxAttempts = cfg.Bind("04 Objectives", "GrabMaxAttempts", 3,
                "Give up on a valuable after this many failed grab attempts, then skip it for a while.");
            FailedGrabSkipSeconds = cfg.Bind("04 Objectives", "FailedGrabSkipSeconds", 120f,
                "How long to ignore a valuable the bot couldn't grab (out of sight / can't reach), so it doesn't keep returning and re-spamming.");
            JumpWhenStuck = cfg.Bind("02 Movement", "JumpWhenStuck", true,
                "Try to jump when the bot stops making progress (helps over small props/ledges).");

            ShowTimer = cfg.Bind("05 Timing", "ShowTimer", true,
                "Show the run timer on the HUD.");
            TrackBestTimes = cfg.Bind("05 Timing", "TrackBestTimes", true,
                "Persist a best completion time per level and show it on the HUD.");
            BeatBestMode = cfg.Bind("05 Timing", "BeatBestMode", false,
                "When ahead of / behind your personal-best pace, push the bot harder to try to beat it.");
            BeatBestAggression = cfg.Bind("05 Timing", "BeatBestAggression", 0.5f,
                new ConfigDescription("How much extra risk to accept in BeatBest mode (0..1).", new AcceptableValueRange<float>(0f, 1f)));

            ShowHud = cfg.Bind("06 HUD", "ShowHud", true,
                "Show the bot status overlay (mode, phase, timer, target).");
            VerboseLogging = cfg.Bind("06 HUD", "VerboseLogging", false,
                "Log detailed per-decision diagnostics to the BepInEx console.");
            DiagnosticsOnLoad = cfg.Bind("06 HUD", "DiagnosticsOnLoad", true,
                "On startup, log which game types/members the adapter resolved. Use this to verify symbols for your game version.");
        }
    }
}
