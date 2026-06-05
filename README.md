# REPOBot

An automation mod that plays **R.E.P.O.** for you: it perceives the level,
avoids monsters, collects valuables, and hauls them to extraction — with a
runtime-switchable **SafeCollect** (careful/professional) and **Speedrun**
(fastest-possible) profile, an on-screen run timer, and persistent
**beat-your-best** time tracking per level.

It's a normal [BepInEx](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/)
plugin built with the same stack the rest of the R.E.P.O. modding scene uses
(BepInEx 5 + HarmonyX, optionally REPOLib).

---

## ⚠️ Intended use — please read

R.E.P.O. is a **co-op PvE** game and the developers support modding, but an
"AI plays for me" mod still affects the people in your lobby.

- Use it **solo**, or in **private lobbies with friends who are fine with it**.
- **Don't** drop a bot-controlled player into **public lobbies** with strangers
  who didn't sign up to babysit it.
- This is a single-player automation/quality-of-life mod, **not** a tool for
  griefing, and it has no place in any competitive or ranked context.

You're responsible for how you use it.

---

## What it does

| Capability | Notes |
|---|---|
| Monster avoidance | Steers away from enemies inside `DangerRadius`; drops everything and flees inside `FleeRadius`. Hunting enemies are weighted more heavily. |
| Item collection | Finds every `ValuableObject`, scores them by distance/value/exposure, and grabs them. `CollectAll` or value-threshold policies. |
| Extraction | Hauls to the nearest active extraction point; configurable "extract once carrying $X" policy. |
| Two modes | `SafeCollect` (survival-first) and `Speedrun` (time-first), **switchable mid-run** with a hotkey. |
| Run timer | On-screen timer for the current level. |
| Beat-your-best | Best completion time saved **per level**; optional "push harder when behind PB pace" mode. |
| Self-diagnostics | Logs which game symbols it resolved on startup, so version drift is easy to fix. |

---

## How it's built (architecture)

The code is split so that the **decision-making is engine-agnostic** and the
**game-specific glue is isolated** in one folder:

```
src/REPOBot/
├─ Plugin.cs              BepInEx entry point; wires everything together
├─ Config/Settings.cs     every tunable, bound to BepInEx config
├─ Core/
│   ├─ Perception.cs      plain data types the brain consumes (no game refs)
│   ├─ BotMode.cs         BotMode + RunPhase enums
│   ├─ Navigator.cs       NavMesh pathing + stuck detection
│   └─ BotController.cs   the state machine + run/timer lifecycle
├─ Brain/                 PURE logic, unit-testable, no game/Unity-game refs
│   ├─ ThreatModel.cs     monsters -> danger level + repulsion vector
│   ├─ TargetSelector.cs  which valuable next / when to extract
│   └─ Steering.cs        seek + avoid -> desired move direction
├─ Game/                  ⚠️ the ONLY place that touches R.E.P.O. internals
│   ├─ Reflect.cs         reflection toolkit + symbol-resolution reporting
│   ├─ GameApi.cs         every game type/field/method name lives here
│   ├─ WorldScanner.cs    scene -> Perception snapshot
│   └─ InputDriver.cs     desired direction -> character input
├─ Timing/
│   ├─ RunTimer.cs        timer + PB pace math
│   └─ BestTimeStore.cs   per-level best times persisted to a .tsv
└─ UI/BotHud.cs           IMGUI status overlay
```

Why this shape: R.E.P.O. updates change internal class/field names. When that
happens, **only `Game/GameApi.cs` (and occasionally `InputDriver.cs`) needs
touching** — the brain never breaks.

---

## Building — the easy way (Steam, Windows)

**Just double-click `build.bat`.** It does everything:

1. finds your Steam R.E.P.O. install automatically,
2. installs the .NET SDK for you if it's missing (one-time),
3. builds the mod, and
4. copies `REPOBot.dll` into `BepInEx\plugins\REPOBot\`.

Then launch the game and press **F8**. That's it.

> **One prerequisite:** BepInEx must already be in your game folder. If you don't
> have it, install **BepInExPack** first — either from
> [Thunderstore](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/) or via a
> mod manager like **r2modman / Thunderstore Mod Manager**. The script will tell
> you if it's missing and leave the built DLL ready to copy.

If auto-detection can't find the game, the script just asks you to paste the
folder path (in Steam: right-click R.E.P.O. → *Manage* → *Browse local files*).

### Manual build (advanced / non-Steam)

The bot reads game types via reflection, so you only need the UnityEngine DLLs
from your install — not `Assembly-CSharp.dll`.

```
cp Directory.Build.user.props.template Directory.Build.user.props   # set REPOGameDir
dotnet build -c Release
```
Output: `src/REPOBot/bin/Release/netstandard2.1/REPOBot.dll` → drop into
`...\REPO\BepInEx\plugins\REPOBot\`.

---

## Controls (default hotkeys)

| Key | Action |
|---|---|
| `F8` | Toggle the bot on/off |
| `F9` | Cycle mode (SafeCollect ⇄ Speedrun) |
| `F10` | **Panic** — instantly disable and return control to you |
| `F11` | Clear the saved best time for the current level |

All rebindable in the config.

---

## Settings

Everything is in the BepInEx config (`BepInEx/config/donotpetme.repobot.cfg`),
editable by hand or live with a config-manager mod. Highlights:

- **00 General** — `Enabled`, `Mode`
- **01 Hotkeys** — `Toggle`, `CycleMode`, `Panic`, `ResetBest`
- **02 Movement** — `MoveIntensity`, `AllowSprint`, `ArriveRadius`, `StuckSeconds`, `RepathInterval`
- **03 Threat** — `DangerRadius`, `FleeRadius`, `AvoidWeight`, `AlertedMultiplier`
- **04 Objectives** — `CollectAll`, `MinValueToDetour`, `ExtractWhenCarrying`
- **05 Timing** — `ShowTimer`, `TrackBestTimes`, `BeatBestMode`, `BeatBestAggression`
- **06 HUD** — `ShowHud`, `VerboseLogging`, `DiagnosticsOnLoad`

---

## Verifying symbols (important)

Because game internals change between versions, the adapter resolves each game
type/field by trying a list of **candidate names** and reports the result.

On startup (with `DiagnosticsOnLoad = true`) you'll see something like this in
the BepInEx console / `LogOutput.log`:

```
REPOBot symbol resolution (12/14 resolved):
  [OK ] type PlayerAvatar: ok
  [OK ] type Enemy: ok
  [!! ] ValuableObject.dollarValue: MISSING - verify name
  ...
```

For anything marked `!!`:

1. Decompile your `REPO_Data/Managed/Assembly-CSharp.dll` with **dnSpy**,
   **ILSpy**, or the REPO-specific **DLL Muster** tool.
2. Find the real member name on the listed type.
3. Add it to the **front** of the candidate list in `Game/GameApi.cs`, rebuild.

The two things most likely to need wiring for your build:

- **`ValuableObject` value field** (`GameApi._valuableValue`) — only affects
  value-based prioritisation; collection still works without it.
- **Movement** is driven by `MovementPatch` — a Harmony postfix on
  `PlayerController`'s per-tick update that overrides the player's Rigidbody
  velocity. This avoids depending on private input-field names. If the patch
  can't install, `InputDriver` falls back to nudging the Rigidbody directly.
  Tune `WalkSpeed`/`SprintSpeed` in config to match your game feel.

---

## Known limitations / honest caveats

- **Symbol names are best-effort.** They're drawn from the community decompile,
  not pinned to a specific patch. Expect to verify a few names for your build
  (the startup report tells you exactly which).
- **Grabbing is wired but experimental.** The bot grabs valuables via
  `PhysGrabber.ForceGrabPhysObject`, opens hinged containers (fridge/cupboard/
  drawer doors) that block a valuable, carries items to the active extraction
  point, and drops them. Extraction *completion detection* and the **cart** are
  still rough — completion is inferred from the extraction `currentState`, and
  the bot doesn't use the C.A.R.T. yet (it's designed for two players to push).
- **Carried-value isn't read precisely**, so the "extract once carrying $X"
  policy is conservative; the default behaviour (collect everything, then
  extract) doesn't depend on it.
- **No networking logic.** It controls only your local character; it does not
  coordinate with other players.
- Built and reviewed without a compiler in the authoring environment — see
  CONTRIBUTING/commit notes. Build it locally against your game DLLs.

---

## Roadmap ideas

- Door / valuable-cart handling and two-handed object logic.
- Smarter line-of-sight-aware threat model (raycast occlusion).
- Per-mode tuning presets and an in-game settings panel.
- Optional REPOLib integration for richer game-state access.

---

## License

For personal/solo and private-lobby use. Don't ship it as a public-lobby
griefing tool.
