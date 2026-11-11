# AGENTS.md — EasyEngine

Context for AI coding assistants (Codex, Claude Code, Cursor, Copilot, Gemini…). Read this before changing anything.
`README.md` (English) and `README.pt-BR.md` are the user-facing description; this file is the working knowledge behind it.

## The project in one paragraph

EasyEngine is "the easiest 2D game engine": a **Windows-only C# / WPF / .NET 8** desktop editor. The user brings the
art, builds scenes by drag-and-drop, describes in plain language what each thing does, and an **AI turns that into
real behaviors** (never free C# code — it answers JSON that the editor applies defensively). Games run in the engine's
own runtime (`PlayWindow`) and export as a self-contained `.exe`. Target audience: **people who don't like
programming**. The owner's yardstick game is a mid-size **RPG + metroidvania + puzzle + hack-and-slash**; features
that game needs come first (rotational physics/ragdolls/vehicles come last). Long-term goal: publish it free.

- Repo: `github.com/tarl3s/EasyEngine` — **public** (MIT, since v0.11.0), branch `main`. Everything you push is public:
  no secrets, no personal paths in docs meant for players. `README.md` (English) and `README.pt-BR.md` are the public
  face — keep both in sync when features change.
- Version: `0.17.0` (see *Versioning*). Commit history is detailed — `git log` is a good changelog.
- The owner speaks **Brazilian Portuguese**: reply to them in PT-BR. Code, comments, UI text, commit messages and
  this file are in **English** (the editor UI is 100% English by request; only `README.pt-BR.md` is Portuguese).

## Build, run, test

```bash
dotnet build                      # 0 errors expected; no NuGet dependencies — keep it that way (no AvalonDock etc.)
dotnet run                        # launcher (new/open/recent projects)
dotnet run -- --editor            # force the launcher even next to an exported game
dotnet run -- --play <proj.eeproj>  # play a project with no editor
```

**How the owner opens the engine:** the desktop shortcut `EasyEngine.lnk` runs `tools/Start-EasyEngine.ps1` (hidden
PowerShell). If any `.cs`/`.xaml`/`.csproj` is newer than `bin\Debug\net8.0-windows\EasyEngine.dll`, it rebuilds
first (small "Updating…" window, log in `%TEMP%\easyengine_build.log`), then starts the exe — so every change you
make shows up the next time they open it. While their engine is open, Windows locks `bin\Debug\…\EasyEngine.exe`:
build and test elsewhere (`dotnet build -p:OutDir=<scratch>/`), never kill their window (unsaved work), and tell
them to close and reopen it from the shortcut.

Every mode is dispatched in `App.OnStartup` (`App.xaml.cs`). Test suites run headless and log to `%TEMP%`:

| Flag | What | Log |
|---|---|---|
| `--fulltest` | the big battery (≈451 checks: every script action/event, behaviors, Scratch round-trip, editor, E2E) | `%TEMP%\easyengine_fulltest.log` |
| `--e2e` | plays the test scenarios step by step (Axe, Lighting, Boss, Infinite World; ≈41 steps) | `easyengine_e2e.log` |
| `--behtest` (49) / `--exprtest` (26) / `--blocktest` (18) / `--aijsontest` (20) | behaviors / expression evaluator / block-editor round-trip / AI-JSON parsing robustness | `easyengine_<name>.log` |
| `--selftest` | opens real windows (editor, play, every dialog), measures every visible text against what is drawn behind it (`ContrastAudit`, fails under 4.5:1), renders PNG snapshots; add `--theme light` for the light theme | `easyengine_selftest.log`, `easyengine_snap_*.png` (`*_light.png`) |
| `--review` | dev tool: plays Boss Demo + Infinite World in a real window, saves PNGs of key moments | `easyengine_snap_review_*.png` |
| `--pairtest [--only Patrol,Chase] [--combos] [--trace]` | dev tool (headless, ~1 min): first the behavior-combination battery (`FullTest.BehaviorCombos.cs`; `--combos` = only that), then every visible behavior alone, every pair (both orders for movers), "Only while"-resting movers, behaviors on the player and a few triples, each played ~5 s; flags exceptions, NaN, absurd speeds, falling through the ground, frozen / jittering / drifting objects, health out of range, console warnings, the player losing its keys. "By design" flags are listed apart; ends with a summary line. `--trace` logs the first ticks of each case | `easyengine_pairtest.log` |
| `--aitest [--model m \| --cloud [auto\|groq\|gemini\|openrouter\|"provider\|model"]] [--only id,id] [--delay ms]` | **graded** AI eval (`App.AiEval.cs`): ~125 real requests (moves, timers, enemies, bosses, HUD, whole levels, edits, Portuguese, conversations, the Boss/Cutscene cards' and the block editor's ✨ Ask the AI), each checked on the resulting project and by playing it headless. Needs a running AI; the cloud free tiers have daily quotas — rerun failures with `--only` | `easyengine_aitest.log` |

Last known green (v0.17.0): fulltest 911/911, e2e 127/127, pairtest 0 suspicious (2436 combinations), behtest 49, blocktest 18, exprtest 26, aijsontest 20, selftest PASS (dark and light); with FMOD 2.03.15 installed fulltest adds ~21 FMOD checks; --aitest local qwen2.5-coder 1.5b 121/123 and 3b 118/123 (v0.15.6) (cloud: re-measure, quotas).
**Run the relevant suites after every runtime change** — at minimum `--fulltest` (it includes E2E). The app exits by
itself; read the log, don't trust the exit code alone.

**Scripts that print only the summary** (prefer them — they save reading logs): `tools/Test-All.ps1` (build to scratch + every suite, one line each; `-Quick` = fulltest+e2e), `tools/AiEval.ps1` (graded AI eval on 1.5b/3b; `-Only`, `-Extra '--in pt'` for the cases in Portuguese or `--in es` Spanish), `tools/Strings.ps1` (`-Missing` lists untranslated texts, `-Add file.json` appends translations to every `Lang/*.json`). Claude Code skills `/test-all`, `/ai-eval`, `/translate`, `/release` wrap them.

Demo/project builders (write into `Desktop\Jogos\<name>`, replacing the old copy): `--makegame` (Platformer Demo),
`--mining`, `--topdown`, `--bossdemo`, `--infinite`, `--spritetest`, `--scenarios` (the *EasyEngine Tests* project,
one scene per mechanic). `--restore-recent` re-adds projects under `Desktop\Jogos` to the Recent list. `--strings` rewrites `Lang/en.json` (every English editor text) and `Lang/_context.tsv`.

## Architecture map

Flat folder, one namespace `EasyEngine`, big classes split into `partial` files by feature.

| Area | Files |
|---|---|
| Data model (Project → Scenes → Layers → GameObjectData → BehaviorData; layers have Locked/Opacity/Active and objects Active (off = not loaded when playing; `setactive` block turns them on/off at runtime); `BehaviorKind` enum, prefabs, theme, `SkySetup`) | `Model.cs` |
| Tilemap palette (`TileKind`: colour, solid, picture or animated frames; the tolerant "palette" JSON shared by editor and runtime) | `TilePalette.cs` (editor: `MainWindow.Tiles.cs`, runtime: `PlayWindow.Tiles.cs`) |
| Save/load `.eeproj` (JSON), recent list, legacy migration (`MigrateLegacy`, e.g. Melee → Use Item) | `ProjectIO.cs` |
| **BuilderEngine link** (open/save BuilderEngine folders, "Built on BuilderEngine" objects, live link, the bridge that runs BuilderEngine mechanisms in play) | `BuilderLink.cs` (helpers over BuilderEngine's `EasyLink`), `MainWindow.BuilderLink.cs` (inspector card, AI skip), `MainWindow.LiveLink.cs`, `PlayWindow.BuilderBridge.cs`, `lib/BuilderEngine/*.dll` (binary reference, `tools/Update-BuilderEngineLib.ps1`) — see *BuilderEngine link* below |
| **Behavior packages** (`.behavior` files: offered like built-ins, run in BuilderEngine's world) | `BehaviorPackages.cs` (finding, settings, keys, "show if", catalog view, copies), `MainWindow.Packages.cs` (＋ picker, card, pickers per type, export copy), `MainWindow.AiPackages.cs` (Build prompt, AI apply, Learn), the bridge in `PlayWindow.BuilderBridge.cs` (`SpawnWithPackages`, `PackageBack`) — see *Behavior packages* below |
| **Behavior catalog** — name, icon, description, every param with its default, picker kind and `ShowIf` | `BehaviorCatalog.cs` |
| How-to guides used by the AI Learn mode | `BehaviorGuides.cs` |
| Editor window (scene canvas, docking panels, inspector, AI panel, undo/redo, export) | `MainWindow.xaml(.cs)`, `MainWindow.Layers.cs` (layer rows and their ⋯ menu: order, rename, duplicate, opacity, lock, on/off), `SceneTools.cs` (duplicate scene/layer with new ids), `MainWindow.Params.cs` (param pickers), `.Learn.cs`, `.Minimap.cs`, `.Sequences.cs`, `.LockZ.cs` (🔒 Lock — editor-only, the AI skips locked objects; Z layer; arrow-key nudge; Keep proportions / Original size), `.Layers.cs` (⧉ duplicate scene; the layer row and its ⋯ menu: order = draw order, 🔒 locked layer, opacity, ▶ on/off in the game; objects' Active) — model-side helpers in `SceneTools.cs` |
| **Runtime** (game loop on `CompositionTarget.Rendering`, AABB physics, behaviors, Custom-rule interpreter, camera, HUD) | `PlayWindow.xaml(.cs)` + `PlayWindow.<Feature>.cs`: Lives (`PlayWindow.Lives.cs`: lives count + hearts HUD, back to checkpoint/start, a scene at zero; replaces Health's respawn), Movers (who steers an object when several behaviors want to: a hit's knockback for a moment, then the player's keys, then ONE automatic mover — Chase / Smart Chase with a target in range > Advanced Patrol > Patrol; a resting mover doesn't stop the others; chasers walk when they have gravity and never overshoot; patrols turn at walls; `Contact` = a touch on a solid thing is being against it; the inspector's warnings for pairs that can't work are in `BehaviorCombos.cs`), Layers (layer opacity, setactive), LightShapes (+ `LightShapes.cs`: Light `shape` circle/beam/half/area/bar, `effect` flicker/pulse/blink — mask and glow pictures cached per look, also used by LightGrid), Boss, Sequences (cutscene step runner), Terrain, Lighting (smooth darkness + glow), LightGrid (Day/Night "Blocks" lighting: per-block light map, block-by-block), Minimap, Tools (Use Item/Hotbar/Harvestable), Texts (Text/Yes-No/Colour variables, settext/toggle/setcolor/tint) and Lists (listadd/listremove/listclear/iflist, `count:name`), Sound (a Sound "Only heard nearby" fades out to its radius and "Left / right in the headphones" sets the balance by its position — the math in `SpatialSound.cs`, followed every frame per behavior in `PlayWindow.MultiBehaviors.cs`), Console, Checklist, Hitbox ("fit to the drawing": X/Y/W/H are the sprite's visible part, the picture is drawn at the full box via `PictureAt`, mirrored when flipped; bounds in `HitboxFit.cs`, editor rows in `MainWindow.Hitbox.cs`), PolyHitbox (`PhysicsData.Shape`: a polygon hitbox drawn in `HitboxShapeWindow.cs` as fractions of the box; geometry in `PolyHitbox.cs`; on a still solid it is the ground and walls — `ResolveX/Y` ask `PlayWindow.PolyHitbox.cs` (walk up slopes, stop at steep sides, snap down slopes) — on anything else it is what `Overlap`/`Touching` use; a moving object still moves as its box), ZOrder (`GameObjectData.Z`: `ZOrder.InDrawOrder` orders each layer in the editor and at load; spawned copies go on top, ordered by Z among themselves), Layers (layers/objects off in the game aren't loaded; layer opacity as an OpacityMask; the `setactive` rule), `Tests*` (test hooks); variable kinds and their conversions live in `VarScope.cs` |
| Custom behavior mini-language (`when → do` rules) + expressions | an object's rules run AFTER all its other behaviors each frame (so "always: speed 200" isn't undone by a Platformer listed later); empty `cond` = no condition; `Expr.cs` reads and/or/not, `=` and `<>`; events allgone/keyidle/becomes/keynear live in `PlayWindow.RuleEvents.cs`; runtime in `PlayWindow.xaml.cs` (`RunCustom`/`DoCustom`/`RuleValue`), `Expr.cs` (recursive evaluator, never throws) |
| Scratch-style editors | `BlockEditorWindow.cs` (Custom rules; a block can have several WHEN rows (OR), several DO rows (in order) and several conditions (all/any, joined into one `cond`) — stored as consecutive flat rules sharing `"block": n` (the 2nd+ WHEN rows have no "do"; a 1-WHEN/1-DO block has no `block` key), decided once per block in `RunCustom`; blocks with `BlockDef.Needs` show only for objects that have one of those behaviors (`PlayWindow.RuleBlocks.cs`: controls, ability, setjump, heal, behavior on/off, landed, hurt, interact); `.Palette.cs` = every event/action block and its fields — the one list the blocks, their AI prompt and `RuleDocs()` (the rule language for the Build prompt) come from; `.Ai.cs` = its "✨ Ask the AI": prompt built from the block palette, tolerant reply reader), `SequenceEditorWindow.cs` ("Advanced Scratch" for Boss AI / Cutscene). Blocks and AI must cover the same rule language: `FullTest.Parity.cs` plays every block while the game records each key it reads (`PlayWindow.RuleReads`) and fails, naming the gap, when a key has no block field (or `KeyAliases` entry) or a field is never read; it also round-trips AI-style rules through the blocks and compares how they play |
| Boss/Cutscene step palette, script model, tolerant JSON (shared by runtime, editor and AI) | `SequenceModel.cs` (`StepCatalog`, `SeqStep`, `BossScript`, `SequenceJson`), `SequenceAi.cs` |
| AI clients | `MainWindow.AiPrompt.cs` (the Build prompt), `MainWindow.AiApply.cs` (applies a reply: create/edit, behaviors, physics, intent guesses), `AiSpecFix.cs` (cleans up every model reply), `MainWindow.AiExamples.cs` (whole request→reply examples), `MainWindow.AiRules.cs` (rule merging, deletes, copied habits), `MainWindow.AiLevel.cs` (the "level" layout), `OllamaClient.cs` + `OllamaManager.cs` (local, `localhost:11434`), `CloudClient.cs` (OpenAI-compatible free tiers Groq/Gemini/OpenRouter with auto-fallback on 429, BYO paid keys incl. native Anthropic; `AiSettings`) |
| Shared helpers | `ListSearch.cs` (type to search in every open ComboBox/ContextMenu: hides non-matching items, never changes the selection until Enter; class handlers installed at startup), `NumberWheel.cs` (the mouse wheel steps the number in the focused TextBox and applies it at each notch — live; a burst is one undo step; the box gets the focus back after an inspector rebuild), `AssetBackup.cs` (hidden `.backup` copy of every project asset, restored on open; Settings → Backup), `StartMenu.cs` (a Start menu shortcut made once when the launcher opens — not for the source build; a deleted one stays deleted), `LooseJson.cs` (lenient JSON + number parsing), `Theme.cs` (dark/light palettes, `Init`), `Loc.cs` (`L.T` languages, `Lang/*.json`), `CacheCleaner.cs` (clear temporary files), `Dropdown.cs`, `ParamSources.cs` (lists for pickers), `Contrast.cs`, `DemoKit.cs`, `ToolArt.cs`, `LookPicker.cs`, `ColorPickerPopup.cs`, `SoundSynth.cs` (generated WAVs) |
| Other windows | `AudioEditorWindow.cs` + `AudioEdit.cs` (the sound editor behind "✂ Edit…": cut, speed, pitch, volume, fades, reverse, echo; WAV only; effect names stay English), `LauncherWindow`, `SettingsWindow.cs` (language, theme, size, updates, temp files), `AnimationWindow`, `SpriteEditorWindow.cs` (pixel art), `VariablesWindow`, `NewItemDialog.cs` (`ItemMaker`), `InputDialog`, `DebugConsoleView.cs` + `DebugLog.cs` (F1 console) |
| Demos / scenarios | `DemoGame.cs`, `MiningGame.cs`, `TopDownGame.cs`, `BossDemo.cs`, `InfiniteWorld.cs`, `SpriteTestGame.cs`, `TestScenarios.cs` (+ `GameAssets`) |
| Tests | `FullTest*.cs`, `E2ETests*.cs`, `App.SelfTest.*.cs`, `SelfTestContext.cs`, `App.Review.cs` (all `partial class App`) |

### Extension points (use these instead of growing the big files)

- **Runtime feature hooks**: `partial void` methods declared in `PlayWindow.xaml.cs` (search "feature hooks") — e.g.
  `SequencesBeforeLogic/AfterLogic`, `TerrainBeforeLogic`, `MinimapLogic`, `UpdateBoss`, `UpdateCutscene`,
  `*SceneReset`, `TerrainSceneLoaded`, `*EntityAdded/Removed`, `SequencesRender`, `CameraTargetOverride`,
  `CameraClampOverride`, `CameraShakeOffset`. Per-feature entity state goes in a `private partial class Entity` inside
  the feature file.
- **Collision** always asks through `SolidsNear(x, y, w, h)` (allocation-free struct enumerator over static solids
  *and* terrain cells). Don't iterate `_solids` directly.
- **Tests**: add a `partial void FeatureTests<Name>(FullTestKit t)` in a `FullTest.<Name>.cs` and call it where the
  others are called in `FullTest.cs`; E2E scenarios via `AddE2EScenarios*`; selftest via `SelfTestContext`
  (`Snapshot(window, name)` renders a window to PNG).
- **Demo builders** go through `App.RunDemoBuild(mode, folder, build)` and use `DemoKit`.
- Live and headless play share one `StepLogic`, so headless tests exercise the real game code.

### Adding or changing a behavior (checklist)

1. Add the `BehaviorKind` (projects save kinds **by name** — never rename one) and a `BehaviorInfo` in
   `BehaviorCatalog`.
2. **Every parameter is picked, not typed** (standing owner rule): give each `TextParam` a `PickKind` (Object, Item,
   Key, Color, Choice, Look, Variable, Sound…), give fixed numeric values `Options`, hide irrelevant fields with
   `ShowIf`. Typing is only for genuinely free text (labels, dialogue, scripts, a *new* name via "＋ New…"). Never
   expose internal string formats like `name:#color:look` or `a=1,b=2` — structured lists get a visual editor.
3. Implement it in the runtime (`UpdateBehaviors` switch or a feature file + hook). If it moves the object, give it
   its place in the mover rules (`PlayWindow.Movers.cs`) instead of writing VX/VY over the others; a pair that can
   never work together gets a warning in `BehaviorCombos.cs`. Run `--pairtest` to see it next to every other behavior.
   **Several on one object?** Set `BehaviorInfo.Repeatable = true` only when two of it on one object make sense on their
   own (today: Interact, Sound, Shooter, Spawner, Particles, Damager, Trigger Area, UI Button, Shop, Custom) — never for
   what owns the object (movers, Health/Lives, camera, HUD widgets, Boss/Cutscene, Day/Night, terrain, maps). A
   repeatable behavior keeps its runtime state per behavior (`St(en, bi)` in `PlayWindow.MultiBehaviors.cs`), never in
   an `Entity` field; one key press fires at most one Interact (and one Shop purchase) per object — the first in the
   list that is on, so two Interacts with opposite "Only while" make a one-key on/off toggle. The inspector offers a
   repeatable kind again and numbers its cards; the AI keeps each one a reply lists (`ApplyBehaviorsToObject`).
4. Teach the AI: the build prompt reads the catalog; add a how-to in `BehaviorGuides` for Learn mode; Boss/Cutscene
   steps live in `StepCatalog` (one palette shared by runtime, editor and AI prompt).
5. Old projects must keep loading: never rename persisted keys; retire a kind with `Hidden = true` + a migration in
   `ProjectIO`; missing keys get catalog defaults.
6. Tests in `--fulltest` (and an E2E scenario if it is a mechanic the owner will test by hand — see below).

## AI integration rules

- The model never writes C#. It gets the behavior catalog + the scene inventory and answers JSON
  (`{create, edit, layers, variables, theme, …}`), applied by `ApplyProjectSpec`/`TryApplyBehaviorSpec` in
  `MainWindow`. The parser must stay **tolerant** (comments, trailing commas, numbers in quotes, `"0,5"` decimals —
  use `LooseJson`). Small local models (qwen2.5-coder 1.5b) are the worst case the owner tests against on purpose;
  improvements belong in the shared pipeline so every model benefits.
- Platformer/TopDown read the *player's* keyboard; non-player objects get `controls = 0`. "When X happens, do Y" is
  always a Custom rule, never a static parameter.
- Everything the model answers goes through `AiSpecFix` before it is applied: event/action synonyms (one table, also
  used by the block editor's ✨ Ask the AI), params in the wrong bag (`"var"` in nums, `"axis":"y"`), Custom written as
  a behavior, a door "on death". Rules it can't use are reported under the change summary (⚠), never guessed.
  Custom rules from the AI are ADDED to an object's rules (`MergeRules`: same event+action retunes the old one;
  `"replaceCustom":true` rewrites them); `"delete":[name]` removes objects; locked objects are never touched.
- `"level":{platforms,coins,enemies,spikes,stairs,door,checkpoint,enemyKind,style:"topdown"}` lets the model say WHAT a
  level has; `MainWindow.AiLevel.cs` lays it out (platforms within the ~100 px jump, coins on them, enemies on the
  ground, a door at the end, a score label). Small models can't place many objects; this way they can still build one.
- **A request in another language is translated to English first** (`AiTranslate.cs`, one short JSON call through the same AI, for Build, the block editor's and the Boss/Cutscene's ✨ Ask the AI): hints, examples, fix-ups and small models all work in English. `LooksEnglish` decides (function words; non-Latin script = not English); `Clean` refuses a translation that lost a number, a quoted text or a name, and then the original is used. Measured (71 panel cases, 1.5b / 3b): Portuguese 55→62 / 61→65, Spanish 44→56 / 56→60; English (123 cases) 121 / 118. The remaining misses are the model's, mostly the same ones it makes in English. `--aitest --in pt|es` runs that panel; the translation is logged as ENGLISH under a failure.
- **The Build prompt is made per request** (`BuildProjectPrompt(request)`, ~3–5k chars, the same for cloud and local):
  a short core (reply shape, rules, every behavior/event/action *name*), then only what the request needs — up to 6
  `Hints` (tagged how-tos in `MainWindow.AiPrompt.cs`, picked by word overlap; `Words()` maps PT/ES stems to English),
  the 2 closest whole examples (`MainWindow.AiExamples.cs`, `ExamplesFor`), the details of the behaviors involved, the
  `PARAMS` of the rule ids they use (from the block palette's `RuleDocs()`), and the scene (rules budgeted). The block
  editor's ✨ Ask the AI builds its prompt the same way (`AiSystemPrompt(request)`, examples converted to rules).
  When an --aitest case keeps failing, first ask whether the runtime should accept what the model wrote (a value as an
  expression, a synonym), then whether a hint or an example is missing; only then special-case. Keep prompts small:
  every hint/example costs tokens only when a request matches it.
- Copied "habits" (a FollowCamera on a box, a Spawner key nobody asked for) are dropped (`StripCopiedHabits`).
  `AiSpecFix.Shape` accepts `create`/`edit` written as one object and a bare rule (or list) as `{"custom":[…]}`.
- Rule values: any numeric param may be an expression or a quoted number (`CustomRule.Num` evaluates it for that
  object); `time` is game time. `timer` first fires after its interval. Object x/y/width/height may be formulas too.
  Instant events (start, keypress, timer, after, touch, clicks, wheel) are decided for all rules before any rule runs
  (so a key buys once even if an earlier rule changes the condition); continuous rules still chain in order. `touch`
  counts edge contact (standing on something touches it). Any behavior can have an "Only while" formula (`cond`).
- Local models: `OllamaClient` asks for a context window that fits the prompt (Ollama's default 4096 tokens silently
  cut the build prompt) and caps the answer (`num_predict`). Cloud Auto: any failure moves to the next free provider, a retired model id is replaced from the
  provider's model list, a model used up for the day hands over to the same provider's next model (Groq/Gemini count
  free quota per model — `CloudClient.DailyFallbacks`), and when all are only busy for a minute it waits once. Free tiers
  are small (Groq 200k tokens/day/model and 8k tokens/minute, Gemini 20 requests/day/model): keep prompts lean — Learn
  sends the compact catalog on free providers, the scene inventory has a rules budget. Running `--aitest --cloud` uses
  the owner's daily quota: rerun only what changed (`--only`).
- The block editor has "📚 Recipes" (`BlockEditorWindow.Recipes.cs`, ready-made rules) and its own ✨ Ask the AI
  (`BlockEditorWindow.Ai.cs`, prompt built from the block palette, shares `AiSpecFix`'s synonym tables).
- Modes: 🔨 Build (edits), 💡 Brainstorm (chat only), 🎓 Learn (explains from catalog + guides, never edits).
  Boss/Cutscene cards have their own "✨ Ask the AI" (dedicated prompt, validated by the runtime's parser, one retry).
- API keys live in `%APPDATA%\EasyEngine\settings.json` (with panel layout and recent list). Never commit keys,
  never print them. Cloud model ids change over time — on a 404, list the provider's models.

## BuilderEngine link

EasyEngine and BuilderEngine (`github.com/tarl3s/BuilderEngine`, sibling folder `JogosBuilderEngine`) open each other's
projects and can edit the same one at once. **The contract is BuilderEngine's `docs/EASYENGINE-LINK.md`**; the one
converter is BuilderEngine's `BuilderEngine.Script.EasyLink` — EasyEngine never maps fields itself.

- **Binary reference**: `lib/BuilderEngine/BuilderEngine.Script.dll` + `BuilderEngine.Plugin.dll` are committed (the repo builds
  alone). After BuilderEngine's link or runtime changes: `tools/Update-BuilderEngineLib.ps1` (runs BuilderEngine's
  `tools/Build-LinkLib.ps1`), build, run the suites, commit the DLLs. Both libraries define `Project`, `Scene`, `World`,
  `TileKind`…: always `using BE = BuilderEngine.Script;`, never `using BuilderEngine.Script;` (and in PlayWindow `World` is
  also the canvas).
- **Unknown JSON is kept**: `Project`, `Scene`, `Layer`, `GameObjectData`, `PrefabDef` have `[JsonExtensionData] Extra`, so
  BuilderEngine's `"Builder"` blocks survive EasyEngine saves and every JSON copy (paste, duplicate, undo, prefab apply —
  a copied BuilderEngine object keeps its mechanisms). Don't add a property named like a key BuilderEngine writes.
- **Opening**: `ProjectIO.Load` takes a `.eeproj`, a BuilderEngine `project.json` or its folder (`EasyLink.ToEeprojFromFolder`);
  `Project.IsBuilderEngine` (FilePath is a project.json) makes `ProjectIO.Save` go back through `EasyLink.SaveFromEeproj`;
  such projects get no default prefabs. Saving a `.eeproj` is atomic (`EasyLink.WriteAtomic`). A project path as the
  first command-line argument opens straight in the editor with the live link on (BuilderEngine's "Open in EasyEngine").
  Recent shows a "BuilderEngine" badge.
- **Built on BuilderEngine** (`BuilderLink.IsBuilt`: `Builder.mechanisms` non-empty): the inspector shows only the common
  fields and one read-only card (+ "Open in BuilderEngine" when `BuilderLink.FindBuilderExe` finds it); the AI
  (`ApplyBehaviorsToObject`, `AddRules`) and the ＋ behavior picker skip it.
- **Live link** (`MainWindow.LiveLink.cs`, BuilderEngine's `LiveLink`): watches the project file; an outside change reloads
  in place (`CopyProjectFrom`, keeping scene, selection, zoom/scroll, active layer; Ctrl+Z restores), never during play;
  with the link on, edits autosave ~500 ms later. Every editor save goes through `SaveProjectFile()` so our own writes
  are echoes. `EasyEngine.exe <proj> --link-probe` logs to `%TEMP%asyengine_link.log` and nudges an object (smoke tests).
- **The bridge** (`PlayWindow.BuilderBridge.cs`): a scene with built objects gets a BuilderEngine `World` (`GameBoot.NewWorld`,
  then `ForNextScene` for later scenes); other entities are read-only mirrors (name, box, tags: Type, Group, Builder tags,
  "player" for the steered one, solid). Per frame after physics: positions → mirrors, keys, number globals ⇄ `_vars`,
  `World.Frame`, then BuilderEngine objects → their entities (box, visibility, picture, colour, flip, solid), spawns and
  destroys, `say`/`narrate`/`ask` → the dialogue box, `show text`/`show bar` → HUD, `notify` → a top message, `play` →
  sounds, `goto_scene` → scene change, prints/errors → F1. Commands needing BuilderEngine's screen (camera, lights, screen
  effects, minimap, hotbar, tiles, cursor, captions/labels/outline, screens, looping sounds) run but are logged once as
  not available. Tests: `FullTest.BuilderLink.cs`, `E2ETests.BuilderLink.cs`, scene "BuilderEngine Link" (`TestScenarios.BuilderLink.cs`).

## Behavior packages

A behavior package (`.behavior`, **the format is BuilderEngine's `docs/BEHAVIOR-FORMAT.md`**) is one JSON file both engines
understand: a manifest (name, icon, category, description, settings with Builder Script param types, `ai.keywords`/`ai.guide`)
plus the logic as one Builder Script mechanism. **EasyEngine never parses it**: `BE.BehaviorPackage` (in the link DLL) reads,
validates and finds them (`Parse`, `Validate`, `AllParams`, `Discover`). After BuilderEngine changes the format or the
runtime, run `tools/Update-BuilderEngineLib.ps1` and commit the DLLs.

- **Where** (`BehaviorPackages.cs`): `BE.BehaviorPackage.DiscoverAll(projectDir)` — `<project>/behaviors/` first, then the
  installed folder (`%APPDATA%\BuilderEngine\Behaviors`, shared with BuilderEngine), then the ones shipped inside the DLL
  (Spin, Projectile, Spawner, TriggerArea… — BuilderEngine's versions of EasyEngine's simple built-ins); first wins, each
  with its `Origin`. `BehaviorPackages.For(project)` is cached and read again when a file's size/time changes. Broken files
  are `PackageSet.Problems`: said once in the status bar (`ReportPackageProblems`) and once in the F1 console when playing —
  never a crash. Test modes set `BehaviorPackages.InstalledFolder` (= BuilderEngine's settable one) to an empty temp folder
  (the person's own packages never change a test).
- **Shipped packages that `replaces` a built-in** (and any package named like a `BehaviorKind`): EasyEngine keeps playing its
  own built-in. They are offered in the picker (tooltip "comes with BuilderEngine"), but the AI and Learn don't list them
  and a reply naming them gets the built-in (`ReplacesBuiltIn`).
- **Stored** as `BehaviorKind.Package` (appended to the enum): `Texts["package"]` = its name, numbers and on/off settings in
  `Nums` (on = 1), everything else in `Texts` (lists and paths as JSON text, keys as the key picker spells them: `Space`,
  `E`, `LeftShift`), each under the param's **name** — what BuilderEngine's EasyCompat reads for a `Package` behavior
  (`PackageParam.EasyEngineKey` is only for a package standing in for a built-in's own keys). The generic catalog entry is `Hidden` (packages are offered by their own names);
  `BehaviorPackages.InfoFor(pkg)` makes a catalog entry from a package for the AI, AiSpecFix and Learn. A package that isn't
  installed keeps its data; its card and the console say so. Adding one (picker or AI) copies the file into the project's
  `behaviors/` (`EnsureInProject` → `CopyToProject`); exporting also writes any installed or shipped one the project uses
  (`CopyGameFiles`).
- **Editor** (`MainWindow.Packages.cs`): "＋ Add behavior…" lists them after the built-ins under "📦 Behavior packages"
  (icon, name, description + category in the tooltip, found by name/category/keywords); repeatable ones can be added again.
  The card uses the existing pickers per type: number/seconds → spinner (− ＋, min/max/step, the wheel steps by `step`:
  `NumberWheel.SetRange`), bool → check, Choice → dropdown, Object/Scene/animation → dropdowns, Key → key list + ⌨ press,
  Color → colour picker, Sound → `SoundRow`, Image → `PicturePick`, text → text box, `List of T` → one row per item with the
  item type's picker, Path → X/Y rows. The author's labels/descriptions are never translated (`L.SetSkip`), `help` is the
  label's tooltip, `showIf` is BuilderEngine's `BehaviorPackage.Shows` over the behavior's settings (one it can't work out
  shows the field).
- **Playing** (the bridge, `PlayWindow.BuilderBridge.cs`): a scene whose objects (or prefabs) have packages gets a BuilderEngine
  world even without built objects. The object stays a normal EasyEngine object; its mirror also runs the packages'
  mechanisms (`GameBoot` already loaded the project's/installed/shipped packages into the world — `World.Package(name)`;
  otherwise the script is loaded once with `World.Load`; settings converted with `ParamTypes.Convert`, a repeatable
  package twice gets each behavior's own settings). **Who moves it**: EasyEngine's behaviors and physics run first; then the
  twin gets x, y, vx, vy and angle, BuilderEngine runs its frame, and what the package CHANGED is applied — a position change
  adds to EasyEngine's move (a Drift on a Platformer: walking + drift), a speed it sets replaces EasyEngine's from the next
  frame (a mover that sets the speed every frame, like Platformer, takes it back), angle → rotation, size/picture/colour/
  visibility/flip like a built object's, `destroy` removes it. "Only while" and the behavior on/off block don't apply to
  packages yet. No per-frame allocation in the bridge part.
- **AI** (`MainWindow.AiPackages.cs`): the Build prompt adds "Behavior packages (…): names" after the built-ins and a detail
  line (description, settings with defaults/choices, guide) for the packages a request names (name or `ai.keywords`); the
  scene list shows a package by its name. A reply adds one with `{"kind":"<name>"}` or `{"kind":"Package","texts":{"package":…}}`,
  cleaned by `AiSpecFix.Behavior` with the package's catalog entry (values kept within min/max, choices matched); `remove`
  takes it off; a built-in with the same name wins. 🎓 Learn lists them and explains the ones asked about (`DescribePackage`).
- **Tests**: `FullTest.BehaviorPackages.cs`, `E2ETests.Packages.cs`, scene "Behavior Packages" (`TestScenarios.Packages.cs`,
  packages written by `TestPackages`), selftest card snapshot (`App.SelfTest.Packages.cs`).

## Optional FMOD audio

FMOD is **optional and never in git** (own licence; `.gitignore` has `fmod*.dll`, `fmod*.lib`, `*.bank`). The wrapper is
BuilderEngine's (`BE.FmodLibrary` / `FmodCore` / `FmodStudio` / `FmodEventHost` in `lib/BuilderEngine/BuilderEngine.Script.dll`,
source in BuilderEngine's `src/BuilderEngine.Script/Fmod*.cs`) — report wrapper bugs there, don't work around them here.

- **`GameAudio.cs`**: the `audioBackend` setting (`AiSettings.AudioBackend`: auto / fmod / classic), ONE shared `BE.FmodCore`
  made the first time a game (or the sound editor's preview) wants it — null = classic, exactly the old MediaPlayer code —
  status lines, the pickers' event / parameter lists (`FmodStudio.ListEvents`, cached), the Sound's `fmodParams`
  (`name=value;…`, shown as one slider per parameter in `MainWindow.Fmod.cs`), export helpers.
- **`PlayWindow.Fmod.cs`**: `AudioStart` (Start) / `AudioFrame` (OnFrame: `Update`, listener) / `AudioStop`; sound files become
  FMOD channels in `PlaySoundFile` (`BehState.Chan`, volume/pan set per frame without allocating); one `BE.FmodEventHost`
  per game, made on the first event request. A Sound with `source` = `fmod` plays `event` (`BehState.Ev`, released by FMOD
  when it ends; 3D events placed at the object). Rule actions `fmodevent` (event, action play|stop, name, volume) and
  `fmodparam` (event or none = global, param, value — sent only when it changes); the cutscene/boss step `fmodevent`;
  BuilderEngine objects through `World.FmodControl`. Headless, nothing plays: every request is in `TFmod` and the log.
- Settings → Sound (backend + status) and → FMOD Studio (open FMOD Studio, reload banks, the open project's banks):
  `SettingsWindow.Sound.cs`. Export asks to include `fmod.dll` (+ `fmodstudio.dll`), default No; the project's `fmod`
  folder always goes with the game. Tests: `FullTest.Fmod.cs` (headless + a silent NOSOUND smoke test with the FMOD
  Engine's example banks when FMOD is installed, a skip line otherwise; `FMOD_DLL=none` forces the no-FMOD path).

## Conventions and hard rules

- **Versioning X.Y.Z**: X = big structural change, Y = a genuinely new behavior/editor/system, Z = fixes AND improvements to something that already exists, even when it gains new options (bump Y resets Z). When unsure, ask the owner before bumping Y. Bump **both**
  `<Version>` in `EasyEngine.csproj` and the fallback `Text="vX.Y.Z"` of `VersionText` in `LauncherWindow.xaml`.
  Each release commit is titled `vX.Y.Z: …` and gets an **annotated tag pushed to GitHub**
  (`git tag -a vX.Y.Z -m "…" && git push origin main --tags`). Tags must always match the real version.
- **Every version is also a GitHub release** — that's what people download and what the in-app updater installs:
  after pushing the tag, run `tools/Publish-Release.ps1 -NotesFile <notes.md>` (self-contained win-x64 build →
  `EasyEngine-win-x64.zip` with an `easyengine-files.txt` manifest → `gh release create`). Write the notes for players
  (what's new, in plain words). The README's download button points at `releases/latest/download/EasyEngine-win-x64.zip`,
  so never rename that asset.
- **Updater** (`Updater.cs`): the launcher checks `releases/latest`; if newer, it asks, downloads, and a script swaps
  the files after the engine closes — deleting only the files in the old version's manifest. A build run from the
  source (an `EasyEngine.csproj` above the exe, i.e. the owner's `bin\Debug`) never checks and never installs
  samples. `--update-now` does the whole update without asking (log `%TEMP%\easyengine_update_now.log`).
- **App icon**: `EasyEngine.ico`, drawn by `tools/Make-Icon.ps1` (the launcher's green hexagon).
- **Encoding**: sources are UTF-8 *without* BOM, mixed LF/CRLF, full of emoji (🎓 ✨ ＋ —). **Never** rewrite files
  with Windows PowerShell `Get-Content | Set-Content` / `-replace` — it double-encodes UTF-8 (this broke the launcher
  once: `ðŸ§ª`, `â€”`). Use a proper edit tool or `sed`/`perl` (handle `\r?\n`). After bulk edits, grep for mojibake:
  `â€|Ã©|ðŸ|ï¼`.
- **UI contrast**: text colour comes from the window (no app-wide TextBlock foreground). Popups, menus, tooltips and
  buttons use the styles in `Styles.xaml`; text on the green accent uses `AccentText`. Contrast ≥ 4.5:1 is tested.
- **Themes (dark / light)**: colours come from the palette brushes (`Bg0..Bg3`, `Hover`, `Border`, `Text`, `TextDim`,
  `Accent`, `AccentHover`, `Danger`, `AccentText`, `Viewport`, `ViewportCard`) — never hard-code a colour that assumes a
  dark background; use `Theme.Brush(key, …)` or `Theme.Pick(dark, light)`. The theme is set once at startup
  (`Theme.Init`, before `Styles.xaml` loads — WPF freezes app resources), so changing it needs a restart. Check both
  with `--selftest` and `--selftest --theme light` (snapshots). Games (`PlayWindow`) keep their own look.
- **Languages (9)**: every text the person can see goes through `L.T("English")` / `L.F("… {0} …", x)` (`Loc.cs`);
  elements also translate themselves when they load. Mark the person's own content (object/layer/scene/project names,
  chat, pickers of their names) with `L.SetSkip`/`L.Keep`. Never translate values the engine reads or compares (keep
  value and label apart — `Dropdown` items carry the value in `Tag`); default names the person can rename (a new
  boss attack "Attack 1", a phase) are theirs and are created in their language. Catalog texts stay English in the catalog (the AI
  reads them) and are translated where they are shown. After adding or changing texts: run `--strings` (rewrites
  `Lang/en.json`), then add the new entries to every `Lang/<code>.json` — the fulltest fails if a language falls below
  97% or breaks a `{0}`. `--lang <code>` forces a language in any mode (e.g. selftest snapshots). AI prompts stay in
  English; the chat and Learn mode are told to answer in the chosen language (`L.AiReplyRule`).
- Game-loop code must not allocate per frame (cached brushes are frozen, lists reused, no LINQ in hot paths).
- Prefer shared helpers (`LooseJson`, `Theme`, `Dropdown.Make`, `ParamSources`, `DemoKit`) over new copies. Sliders are always
  `SliderBar.Make` (click AND drag) — WPF's native `Slider` thumb can't be dragged in this app.
- Commit messages: imperative summary + a body explaining *what changed for the user* and the test totals.

## Testing the way the owner wants

- Every new or complained-about mechanic becomes **a scene in the "EasyEngine Tests" project** (`TestScenarios.cs`,
  built with `--scenarios`; the owner removed its launcher button): on-screen `Checklist` that ticks itself, console
  open on start, instructions, **R** restarts — plus a headless E2E in `E2ETests*.cs`. One project for all scenarios.
- **Sample projects that ship with the engine** are listed in `SampleProjects.All` (now Top-Down Demo, Boss Demo,
  Infinite World). The launcher builds any missing one once per computer into `Documents\EasyEngine Samples` and adds
  it to Recent (tracked in settings `samplesInstalled`, so a removed sample doesn't come back). **Only the owner decides
  what goes in that list** — add a project there only when they say so.
- The F1 debug console logs everything; when a game closes it writes `%TEMP%\easyengine_play.log`. **Read that file**
  when the owner reports a problem from a manual test.
- Visual review: use `--selftest`/`--review` PNGs (the engine renders its own windows). **Never capture the owner's
  screen** (it once grabbed their private chat).

## Files and folders you must respect

- `C:\Users\tarle\OneDrive\Área de Trabalho\Jogos\` is the owner's real workspace (their games, FNAF folders, music…).
  Don't touch anything there except regenerating demos **when asked**. Projects to keep: Platformer Demo, Sprite
  Test, Mining Demo, Top-Down Demo, Boss Demo, Infinite World, EasyEngine Tests, My Game. Regenerating a demo
  overwrites its folder — only if the owner hasn't edited it or asked for it.
- Delete the throwaway projects *you* create while testing: `%TEMP%\EE_*` (`EE_SelfTest_*`, `EE_BehTest_*`,
  `EE_Full*`, `EE_AiTest_*`…). The suites mostly clean up, but check.
- `C:\Users\tarle\learn` is an old, unrelated Unity project — ignore it completely.
- `bin/`, `obj/`, `publish/`, `*.exe` are git-ignored.

## Known gaps / deferred ideas

Export icon is the default one; lights are stylised (no occlusion); undo is per scene (scene add/delete and the
prefab library aren't undoable); no rotational physics/joints (would need Box2D/Aether); no scene thumbnails;
missing behavior keys are filled from the catalog only when the editor opens a project (not on `--play`);
deferred refactors: unify per-behavior AI keyword tables into the catalog, flag Boss-AI-incompatible behaviors at
authoring time, generic Esc handling, lazy `Entity.Locals`, compute terrain contract fields instead of storing them.
