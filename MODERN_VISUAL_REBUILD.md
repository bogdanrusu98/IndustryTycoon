# Modern visual rebuild — implementation and verification

Branch: `feature/core-prototype`. Comparison baseline: `8b1fc144ad3a277ca0f0a33d69507e0cd978c47b`. Unity 6000.5.9f1. Save schema **v9**.

## Presentation

Research covered eight publisher screenshot galleries: Idle Miner Tycoon, Township, Hay Day, Whiteout Survival, Idle Lumber Empire, Egg Inc., Hotel Empire Tycoon and Idle Supermarket Tycoon. [Research, source links and design tokens](VISUAL_RESEARCH.md) distinguish actual screenshot observations from promotional-gallery limitations.

The rebuilt UGUI presentation uses warm ivory cards, ink navigation, evergreen actions, amber rewards, separate cash/carry/level chips, a compact objective, vector icons and a dock that closes gaps when destinations are locked. Production groups recipe, buffers, cycle progress, three upgrades and specialization choices; market uses aligned order cards; logistics separates stock/routes/goals; shop separates products from optional reward actions. Main, pause, settings, help and dialogs share the same visual language. Selected/disabled states remain driven by the existing presenters.

Settings now has large transparent slider hit surfaces, visible handles and toggle tracks. The final PlayMode capture verifies the slider's actual GraphicRaycaster hit. Market and production refresh on panel opening through `PanelShown`; this repairs stale labels after silent save restoration without changing progression or transaction rules. Existing screen/input ownership, Back behavior and service event subscriptions remain authoritative.

## World composition and movement

The walkable ground changes from **31 × 39 m to 60 × 66 m**, with four updated boundary colliders. A distant visual meadow prevents a hard ground edge in the camera; it does not extend navigation. Districts connect through broad avenues, neutral footprints and open transition space. Muted sage, slate, sand and clay distinguish their ground. Collider-free trees and rock silhouettes define the edges. Local labels are limited to two nearby, spatially distinct destinations.

| District | Intentional translation | Straight-line distance-change bound at unchanged 5.5 m/s |
|---|---|---:|
| Early Lumber | none | 0 s |
| Mining, including its machines, pads and markers | +6 X, +6 Z | 1.54 s |
| Central Warehouse and guidance | −8 X, +2 Z | 1.50 s |
| Industrial district, Assembly Plant/purchase, tease and guidance | −2 X, +14 Z | 2.57 s |

The bounds above concern displacement/straight-line travel only. The separate collider audit searches a 0.5 m cardinal grid and then **moves the real CharacterController along each found path**. From the early spawn, conservative grid lengths/time estimates are 13 m / 2.36 s to stockpile, 14.5 m / 2.64 s to processor input, 20.5 m / 3.73 s to packing, 44 m / 8.00 s to ore storage, 37 m / 6.73 s to smelter, 42 m / 7.64 s to warehouse, and 48.5 m / 8.82 s to assembly. Targets use the nearest capsule-clear approach point. These are fixture route checks, not measured human task-completion times. Existing automation still removes repeated inter-district hauling.

Camera was adjusted after reviewing the recomposed scene: offset `(0,21,-16)`, existing 43° FOV and `(0,1,0)` look offset. Normal screenshots use the actual follow camera. The complete-empire overview is explicitly a separate diagnostic camera.

## Asset recovery

[Full entity/source/runtime/placeholder matrix](VISUAL_COMPLETION_ASSET_COVERAGE.md). All **21 staging FBXs and 86 textures** were reinspected; 20 unique meshes, including two additional rock variants and one duplicate crate. There is no hidden props pack.

- Smelter reuses the audited Ironforge furnace shell; packing reuses the blade-free conveyor/table shell and production crate. These are newly recovered uses of existing geometry, not newly discovered unique FBXs.
- Import axis correction is preserved beneath `VisualRoot → Model Facing → Model`; scale compensation occurs before facing rotation to prevent shearing. Gate/rocks are grounded independently of elevated primitive anchors. Saw/drill/wheels use centred visual pivots, and all four wheel axes follow the existing animation correctly.
- Crate, ore, ingot and toolbox resource variants retain exactly one renderer and the existing bounded pools.
- Remaining authored fallbacks are characters, wood/plank/cash/machine parts, stockpile, continuous feeder route, drill chassis and market kiosk: no suitable geometry exists in the complete source inventory. Characters now use assembled uniforms, hardhats and boots instead of visible capsules. They are not new rigged character assets.

No gameplay root was rotated to repair art. Whole-district translations above are deliberate map changes; root identities, references, recipes, costs, progression, saves, offline settlement, analytics and monetization logic remain unchanged.

## Screenshot review and corrections

The references were compared for hierarchy, card grouping, world visibility, district spacing and silhouettes, without copying branded art or layouts.

1. [First review](Documentation/ModernVisual/iteration-1.jpg): level text clipped, old progression tabs reappeared, locked dock slots left gaps, camera too close, capsule characters and sheared/sideways rotating parts dominated. Fixed compact level copy, final progressive-visibility bindings, adaptive dock, camera distance, crafted character visuals, import wrappers and wheel pivots.
2. [Second review](Documentation/ModernVisual/iteration-2.jpg): overlapping local labels, bright/heavy feeder, stale locked market copy, settings handles/hit areas and weak status contrast. Fixed bounded labels, narrower neutral feeder presentation, explicit material asset-path matching (protecting production materials), event-driven refresh on open, slider hit targets/handles, toggle tracks and darker status text. Follow-up QA also corrected a colliding Help label name and framed isolated models using camera aspect.
3. [Final contact sheet](Documentation/ModernVisual/final-contact.jpg): 23 runtime screens covering all requested areas plus settings, pause, achievements, help, reset and locked districts. Three captures use 540 × 1200 with simulated top/bottom insets; others use 540 × 960. [Full screenshots](Documentation/ModernVisual/screenshots) retain the individual images. All active captured buttons meet 64 rendering pixels at the 540-pixel width (128 reference units). Actual DPI and physical-device reach remain device-dependent.

[Production details](Documentation/ModernVisual/production-details.jpg) show all 18 production source geometries plus recovered uses/resource variants, taken from the running scene with isolated render layers. Locked/inactive ancestors are activated only for these disposable diagnostic shots. [Source contact](Documentation/ModernVisual/source-contact.jpg) independently covers every staging FBX. Texture sheets and machine-readable manifests are in the same documentation directory. Capture fixtures never persist to the user's save; the original save and backup are restored around tests.

## Performance and validation

Same-process authored-scene comparison against the baseline commit, same Editor/platform:

| Metric | Before | After | Delta |
|---|---:|---:|---:|
| Authored GameObjects | 678 | 867 | +189 |
| All renderers, including inactive | 349 | 435 | +86 |
| Active authored renderers | 72 | 139 | +67 |
| Active authored triangles | 6,539 | 29,849 | +23,310 |
| Distinct referenced materials | 49 | 56 | +7 |
| Distinct referenced textures | 27 | 28 | +1 |
| Referenced Editor texture memory | 183,052,424 B | 185,435,192 B | +2,382,768 B |

The additional geometry is mostly scenery, character parts and reused production models. Static counts include objects later hidden by runtime label/progression logic and exclude pooled runtime instances. Texture figures are **Editor allocations, not Android resident memory**; they must not be compared with earlier reports from different import/platform states. Production import caps remain 1024 for world art, 512 for resources, ETC2 on Android; there are no new source texture files. No blur or per-frame layout rebuilding was introduced. Dock layout changes only when invalidated by progression, labels update at 4 Hz, existing production progress at 10 Hz.

- Generated scene compile and legacy scene validators: PASS.
- Existing EditMode/embedded PlayMode suite: **70/70 PASS**.
- M19 visual/active-only animation smoke: PASS.
- M17 fresh-to-Empire-25 progression smoke: PASS.
- 600-frame Editor soak: **16.52 ms average, 26.06 ms max, 0 B managed growth**, peak single-frame GC sample 102,960 B. This is an Editor smoke bound, not a device performance claim or controlled before/after FPS benchmark.
- Runtime audit: zero missing scripts or null/error-shader materials; all source geometry/normals/materials inspected; all seven controller routes PASS; market state refresh and slider hit surfaces PASS.
- Android release: **PASS, 0 build warnings**, non-development ARM64 IL2CPP, minimum API 26 / target 36, portrait. Final APK: `Builds/IndustryTycoon-ModernVisual.apk`, **41,911,315 bytes** (39.97 MiB). `aapt` confirms `arm64-v8a`, API levels and absence of the application-debuggable flag. SHA-256: `07c1813bf3675100baac7711a5b9ff289638c417f0b068d0bcf3bb6760d99e6f`. Unity’s larger build-report total also includes symbol artifacts; it is not the APK size.

Physical Android handset playthrough, notch/DPI variations beyond the simulated captures and device GPU/battery profiling remain unverified. No store publication was performed.

## Reproduction and ownership

Primary changed files: `ModernPresentationBuilder`, `ModernWorldComposition`, `M19ProductionSceneIntegrator`, generated scene/carry prefab and modern materials; runtime helpers `TycoonIcon`, `AdaptiveNavigationLayout`, `WorldLabelProximity`, `PanelShown`; small presentation updates in existing views. `LumberCampM19Tests` now asserts descendant lifecycle ownership plus wrapper hierarchy/grounding instead of requiring the imported model to be the direct blocker child.

Commands use Unity's `-batchmode -projectPath <repo> -logFile <log>`:

- `-quit -executeMethod IndustryTycoon.Editor.LumberCampPrototypeBuilder.BuildFromCommandLine`
- `-executeMethod IndustryTycoon.Editor.ModernVisualCapture.Run -visualIteration final` (self-exits)
- `-quit -executeMethod IndustryTycoon.Editor.ModernAssetRecoveryAudit.Run`
- `-quit -executeMethod IndustryTycoon.Editor.ModernVisualVerification.RunMetrics` after exporting the baseline scene to `Logs/ModernVisual/baseline.unity`
- `-runTests -testPlatform EditMode -testResults Logs/modern-tests.xml`
- `-executeMethod IndustryTycoon.Editor.LumberCampM19PlayModeSmokeTest.RunFromCommandLine`
- `-executeMethod IndustryTycoon.Editor.LumberCampM17PlayModeSmokeTest.RunFromCommandLine`
- `-nographics -quit -executeMethod IndustryTycoon.Editor.LumberCampPrototypeBuilder.BuildAndroidPlayerFromCommandLine -customBuildPath Builds/IndustryTycoon-ModernVisual.apk`

Full local logs remain under `Logs/modern-*` and `Logs/ModernVisual`. Staging remains untracked. Documentation evidence is outside Assets and is not shipped in the APK.
