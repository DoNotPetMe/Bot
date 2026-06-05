using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using REPOBot.Config;
using REPOBot.Core;
using REPOBot.Game;
using REPOBot.Timing;
using REPOBot.UI;

namespace REPOBot
{
    /// <summary>
    /// BepInEx entry point. Wires up config, the game adapter, persistence, the
    /// controller and the HUD, then steps back - all per-tick work happens in
    /// <see cref="BotController"/>.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "donotpetme.repobot";
        public const string Name = "REPOBot";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;

            try
            {
                var settings = new Settings(Config);
                var api = new GameApi(Log);
                var bestTimes = new BestTimeStore(Paths.ConfigPath, Log);

                if (settings.DiagnosticsOnLoad.Value)
                    api.LogDiagnostics();

                if (!api.Usable)
                {
                    Log.LogError("Could not resolve R.E.P.O.'s PlayerAvatar type. The bot is loaded but " +
                                 "inactive. Verify symbol names in Game/GameApi.cs for your game version.");
                }

                // Host the controller + HUD on a persistent GameObject.
                var host = new GameObject("REPOBot");
                DontDestroyOnLoad(host);
                host.hideFlags = HideFlags.HideAndDontSave;

                var controller = host.AddComponent<BotController>();
                controller.Settings = settings;
                controller.Log = Log;
                controller.Api = api;
                controller.BestTimes = bestTimes;
                controller.Init();

                var hud = host.AddComponent<BotHud>();
                hud.Settings = settings;
                hud.Controller = controller;

                // Harmony: install the movement override (drives the player by
                // overriding PlayerController's velocity each physics tick).
                var harmony = new Harmony(Guid);
                MovementPatch.Install(harmony, api, Log);
                harmony.PatchAll();

                Log.LogInfo($"{Name} {Version} loaded. Toggle with {settings.KeyToggle.Value}, " +
                            $"cycle mode with {settings.KeyCycleMode.Value}, panic with {settings.KeyPanic.Value}.");
            }
            catch (Exception ex)
            {
                Log.LogError("REPOBot failed to initialise: " + ex);
            }
        }
    }
}
