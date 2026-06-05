using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
using REPOBot.Brain;
using REPOBot.Config;
using REPOBot.Game;
using REPOBot.Timing;

namespace REPOBot.Core
{
    /// <summary>
    /// The brain stem. Each physics tick it perceives the world, decides a phase,
    /// picks a goal, paths to it while avoiding monsters, and drives the player.
    /// Also owns the run timer and best-time bookkeeping. All policy is in the
    /// Brain/ classes; this class is orchestration + the run lifecycle.
    /// </summary>
    public sealed class BotController : MonoBehaviour
    {
        // Injected by Plugin
        public Settings Settings;
        public ManualLogSource Log;
        public GameApi Api;
        public BestTimeStore BestTimes;

        // Subsystems
        private WorldScanner _scanner;
        private InputDriver _input;
        private ThreatModel _threat;
        private TargetSelector _selector;
        private Steering _steering;
        private Navigator _nav;
        public readonly RunTimer Timer = new RunTimer();

        // Live state (exposed for the HUD)
        public RunPhase Phase { get; private set; } = RunPhase.Idle;
        public string LevelKey { get; private set; } = "";
        public string TargetLabel { get; private set; } = "-";
        public float? LastBest { get; private set; }
        public bool NewRecordFlash { get; private set; }

        private bool _runActive;
        private int _initialValuableCount;
        private float _recordFlashUntil;

        // Valuables the bot has parked on but can't collect (grab not wired yet)
        // are skipped for a while so it tours the map instead of fixating on one.
        private readonly Dictionary<GameObject, float> _skipUntil = new Dictionary<GameObject, float>();
        private readonly HashSet<GameObject> _skipSet = new HashSet<GameObject>();
        private GameObject _dwellTarget;   // valuable we're currently parked at
        private float _dwellStart;         // when we arrived at it
        private float _lastGrabAttempt;    // throttle for grab calls

        public void Init()
        {
            _scanner = new WorldScanner(Api);
            _input = new InputDriver(Api, Settings, Log);
            _threat = new ThreatModel(Settings);
            _selector = new TargetSelector(Settings);
            _steering = new Steering(Settings);
            _nav = new Navigator(Settings);
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDestroy() => SceneManager.activeSceneChanged -= OnSceneChanged;

        private void OnSceneChanged(Scene from, Scene to)
        {
            // New level -> reset the run lifecycle.
            EndRun(completed: false);
            Phase = RunPhase.Idle;
            LevelKey = to.name;
            _nav.Clear();
            _skipUntil.Clear();
            _skipSet.Clear();
            _dwellTarget = null;
        }

        private void Update()
        {
            HandleHotkeys();
            NewRecordFlash = Time.time < _recordFlashUntil;
        }

        private void FixedUpdate()
        {
            Timer.Tick(Time.fixedDeltaTime);

            if (!Settings.MasterEnabled.Value || Settings.Mode.Value == BotMode.Off)
            {
                if (_runActive) StopDriving();
                return;
            }

            var world = _scanner.Scan();
            if (!world.Valid)
            {
                Phase = RunPhase.Idle;
                return;
            }

            UpdateRunLifecycle(world);

            var assessment = _threat.Assess(world);
            DecideAndAct(world, assessment);
        }

        // --- Run lifecycle / timer ---

        private void UpdateRunLifecycle(WorldSnapshot world)
        {
            bool hasObjectives = world.ValuablesRemaining > 0 || world.Extraction != null;

            if (!_runActive && hasObjectives)
            {
                _runActive = true;
                _initialValuableCount = world.ValuablesRemaining;
                if (string.IsNullOrEmpty(LevelKey)) LevelKey = SceneManager.GetActiveScene().name;
                LastBest = Settings.TrackBestTimes.Value ? BestTimes.Get(LevelKey) : null;
                Timer.Start(LastBest);
                Log.LogInfo($"Run started on '{LevelKey}'. Valuables: {_initialValuableCount}. " +
                            (LastBest.HasValue ? $"PB {RunTimer.Format(LastBest.Value)}" : "no PB yet"));
            }
        }

        private void CompleteRun()
        {
            float secs = Timer.Stop();
            Log.LogInfo($"Run complete on '{LevelKey}' in {RunTimer.Format(secs)}.");
            if (Settings.TrackBestTimes.Value && BestTimes.Submit(LevelKey, secs))
            {
                LastBest = secs;
                _recordFlashUntil = Time.time + 6f;
                Log.LogInfo($"NEW BEST TIME for '{LevelKey}': {RunTimer.Format(secs)}!");
            }
            _runActive = false;
            Phase = RunPhase.Done;
        }

        private void EndRun(bool completed)
        {
            if (completed) CompleteRun();
            else { Timer.Reset(); _runActive = false; }
        }

        // --- Decision + actuation ---

        private void DecideAndAct(WorldSnapshot world, ThreatModel.Assessment threat)
        {
            // 1) Survival overrides everything.
            if (threat.ShouldFlee)
            {
                Phase = RunPhase.Flee;
                TargetLabel = "FLEE";
                Vector3 fleeDir = _steering.Compute(Vector3.zero, threat, fleeing: true);
                DriveTowardDirection(world, fleeDir, allowSprint: true);
                return;
            }

            // 2) Are we done?
            if (_runActive && world.ValuablesRemaining == 0 && _initialValuableCount > 0 &&
                world.Extraction != null && world.Extraction.Distance <= Settings.ArriveRadius.Value)
            {
                EndRun(completed: true);
                StopDriving();
                return;
            }

            // 3) Holding something -> haul it to extraction and drop it there.
            //    Otherwise pick the next valuable to grab.
            Vector3 goal;
            ValuableView valuableTarget = null;

            bool holding = world.PlayerHoldingValuable;
            Vector3? extractionPos = world.Extraction?.Pos;

            if (holding && world.Extraction == null)
            {
                // Carrying something but nowhere active to drop it: hold position.
                Phase = RunPhase.Haul;
                TargetLabel = "Holding (no active extraction)";
                StopDriving();
                return;
            }

            if (holding)
            {
                Phase = RunPhase.Haul;
                TargetLabel = "Haul -> Extraction";
                goal = world.Extraction.Pos;
            }
            else
            {
                var target = _selector.ChooseValuable(world, threat, Settings.Mode.Value,
                    BuildSkipSet(), extractionPos, Settings.DeliveredRadius.Value);
                if (target == null)
                {
                    // Nothing left to collect. Sit at extraction if one exists.
                    Phase = world.Extraction != null ? RunPhase.Extract : RunPhase.Idle;
                    if (world.Extraction != null) { TargetLabel = "Extraction"; goal = world.Extraction.Pos; }
                    else { StopDriving(); return; }
                }
                else
                {
                    Phase = RunPhase.Collect;
                    TargetLabel = target.Value > 0 ? $"${target.Value:0} item" : "valuable";
                    goal = target.Pos;
                    valuableTarget = target;
                }
            }

            DriveTowardGoal(world, goal, threat, valuableTarget);
        }

        private void DriveTowardGoal(WorldSnapshot world, Vector3 goal, ThreatModel.Assessment threat, ValuableView valuableTarget)
        {
            _nav.SetGoal(world.PlayerPos, goal);

            // Arrived: stop pushing so we don't vibrate on top of the goal.
            if (_nav.Arrived(world.PlayerPos))
            {
                StopDriving();
                HandleArrival(valuableTarget);
                return;
            }

            // Not at the goal but making no progress -> replan.
            if (_nav.UpdateStuck(world.PlayerPos, Time.fixedDeltaTime))
            {
                _nav.SetGoal(world.PlayerPos, goal, force: true);
                // If we keep failing to reach a valuable, skip it and move on.
                if (valuableTarget != null)
                    Skip(valuableTarget.GameObject, "unreachable");
                if (Settings.VerboseLogging.Value) Log.LogInfo("Stuck - replanning path.");
            }

            // Left the dwell target's vicinity: clear the dwell timer.
            if (_dwellTarget != null && (valuableTarget == null || _dwellTarget != valuableTarget.GameObject))
                _dwellTarget = null;

            Vector3 seek = _nav.SteerDirection(world.PlayerPos);
            Vector3 move = _steering.Compute(seek, threat, fleeing: false);

            bool sprint = Settings.AllowSprint.Value && ShouldSprint(threat);
            DriveTowardDirection(world, move, sprint);
        }

        /// <summary>
        /// Reached a goal. If it's a valuable, try to grab it; if we're hauling and
        /// reached extraction, drop what we're carrying. Failed grabs are skipped
        /// briefly so the bot moves on instead of getting stuck.
        /// </summary>
        private void HandleArrival(ValuableView valuableTarget)
        {
            var player = LocalPlayerComponent();

            // Arrived at extraction while carrying -> release the item there.
            if (valuableTarget == null)
            {
                if ((Phase == RunPhase.Haul || Phase == RunPhase.Extract) && Api.HoldingValuable(player))
                {
                    var g = Api.GetPhysGrabber(player);
                    if (g != null) { Api.Release(g); Log.LogInfo("Dropped a valuable at extraction."); }
                }
                return;
            }

            // Arrived at a valuable -> attempt to grab it.
            if (Api.HoldingValuable(player))
                return; // already carrying something; will haul next tick.

            if (_dwellTarget != valuableTarget.GameObject)
            {
                _dwellTarget = valuableTarget.GameObject;
                _dwellStart = Time.time;
            }

            var grabber = Api.GetPhysGrabber(player);
            var physObj = Api.GetValuablePhysObject(valuableTarget.Component);

            // Attempt the grab, throttled so we don't re-fire every physics tick.
            if (grabber != null && physObj != null && Time.time - _lastGrabAttempt >= 0.25f)
            {
                _lastGrabAttempt = Time.time;
                if (Api.TryGrab(grabber, physObj) && Api.HoldingValuable(player))
                {
                    Log.LogInfo($"Grabbed {(valuableTarget.Value > 0 ? "$" + valuableTarget.Value.ToString("0") : "a")} valuable.");
                    _dwellTarget = null;
                    return;
                }
            }

            // Couldn't grab within the dwell window -> skip it for a while.
            if (Time.time - _dwellStart >= Settings.GrabReachSeconds.Value)
            {
                Skip(valuableTarget.GameObject, grabber == null || physObj == null ? "no grab API" : "grab didn't take");
                _dwellTarget = null;
            }
        }

        private void Skip(GameObject go, string why)
        {
            if (go == null) return;
            _skipUntil[go] = Time.time + Settings.UnreachableSkipSeconds.Value;
            if (Settings.VerboseLogging.Value) Log.LogInfo($"Skipping valuable ({why}).");
        }

        /// <summary>Current set of valuables to ignore, with expired entries purged.</summary>
        private HashSet<GameObject> BuildSkipSet()
        {
            _skipSet.Clear();
            float now = Time.time;
            foreach (var kv in _skipUntil)
                if (kv.Value > now && kv.Key != null)
                    _skipSet.Add(kv.Key);
            return _skipSet;
        }

        private void DriveTowardDirection(WorldSnapshot world, Vector3 worldDir, bool allowSprint)
        {
            float intensity = Settings.MoveIntensity.Value;

            // "Beat best" mode: when behind PB pace, push intensity/aggression up.
            if (Settings.BeatBestMode.Value && Timer.Target.HasValue)
            {
                float frac = _initialValuableCount > 0
                    ? 1f - (float)world.ValuablesRemaining / _initialValuableCount
                    : 0f;
                float? pace = Timer.PaceDelta(frac);
                if (pace.HasValue && pace.Value < 0f) // behind pace
                    intensity = Mathf.Clamp01(intensity + Settings.BeatBestAggression.Value);
            }

            _input.Drive(LocalPlayerComponent(), worldDir, intensity, allowSprint);
        }

        private bool ShouldSprint(ThreatModel.Assessment threat)
        {
            if (Settings.Mode.Value == BotMode.Speedrun) return true;
            // In safe mode, sprint only when no threat is near.
            return threat.Danger < 0.15f;
        }

        private void StopDriving()
        {
            var p = LocalPlayerComponent();
            _input.Stop(p);
            _input.SetInteract(p, false);
        }

        private Component _cachedPlayer;
        private float _cachedAt;
        private Component LocalPlayerComponent()
        {
            // Cache the player lookup briefly to avoid scanning every sub-call.
            if (_cachedPlayer == null || Time.time - _cachedAt > 0.5f)
            {
                _cachedPlayer = Api.LocalPlayer();
                _cachedAt = Time.time;
            }
            return _cachedPlayer;
        }

        // --- Hotkeys ---

        private void HandleHotkeys()
        {
            if (Input.GetKeyDown(Settings.KeyToggle.Value))
            {
                Settings.MasterEnabled.Value = !Settings.MasterEnabled.Value;
                Log.LogInfo($"REPOBot {(Settings.MasterEnabled.Value ? "ENABLED" : "disabled")}.");
                if (!Settings.MasterEnabled.Value) StopDriving();
            }

            if (Input.GetKeyDown(Settings.KeyCycleMode.Value))
            {
                Settings.Mode.Value = Settings.Mode.Value == BotMode.SafeCollect
                    ? BotMode.Speedrun : BotMode.SafeCollect;
                Log.LogInfo($"REPOBot mode -> {Settings.Mode.Value}.");
            }

            if (Input.GetKeyDown(Settings.KeyPanic.Value))
            {
                Settings.MasterEnabled.Value = false;
                StopDriving();
                Log.LogInfo("REPOBot PANIC - control returned to player.");
            }

            if (Input.GetKeyDown(Settings.KeyResetBest.Value) && !string.IsNullOrEmpty(LevelKey))
            {
                BestTimes.Clear(LevelKey);
                LastBest = null;
                Log.LogInfo($"Cleared best time for '{LevelKey}'.");
            }

            if (Input.GetKeyDown(Settings.KeyDumpApi.Value))
            {
                Log.LogInfo("Dumping game API (this may be long)...");
                ApiDump.DumpAll(Api, Log);
            }
        }
    }
}
