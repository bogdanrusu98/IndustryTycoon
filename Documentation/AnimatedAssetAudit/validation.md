# Animated production export validation — 2026-10-11

Unity 6000.5.9f1. Checks ran in `Logs/AnimatedAssetProject`, an isolated copy with a separate company/save namespace. The user's open Editor and save data were not used by the checks.

| Check | Result |
|---|---|
| Complete EditMode runner, including tests entering PlayMode | 76 passed, 0 failed, 0 skipped; `tests.xml` |
| Expanded AnimatedProductionTests | 3 passed as part of the complete suite; `tests.xml` |
| Scene validator | Exit 0 |
| M3 worker PlayMode smoke | Full stockpile pause/resume, invalid reservation recovery, real player preemption, 5 autonomous cycles passed |
| M4 production PlayMode smoke | Type isolation, $15 Plank/$5 Wood sales, full-output pause/resume, 19 recipes, 3 worker cycles passed |
| M5 auto-feeder PlayMode smoke | 23 exact transfers (20 consecutive), two safe cancellation paths, capacity/source pause/resume, player contention, manual worker/input/output/sale regressions passed |
| Windows standalone build | Build Finished, Result: Success; builder command exited 0 |
| Windows executable startup | Alive after 25 seconds, graphics initialized on Direct3D 12 / GTX 1660 Ti; no managed exceptions or missing shader/material messages. Test-owned process then stopped. The driver logged an unavailable D3D debug info-queue interface; device initialization succeeded. |
| Gameplay preservation | 572 root/component records matched the baseline; see `gameplay-safety.txt` |
| Source integrity | Every copied FBX/base-color hash matches `production-manifest.json` and the raw source |

The three targeted tests inspect all six imported Humanoid avatars, bone references, looping/root bake settings, serialized visual hierarchy and primitive suppression. Runtime checks sample Idle/Walk/Run on both actors, assert leg motion and zero gameplay-root drift, measure baked skin bounds/grounding, check forward toe alignment, compare renders with/without each character, and exercise real player movement/idle and worker pickup/carry/deposit.

Resource checks inspect both Wood/Plank mesh variants in every instantiated ResourceVisual pool slot, including inactive slots. They verify actual loose Wood, grounded world-space Wood and Plank presentation, six-item CarryStack reuse, settled sale flights of each type with unchanged pool size, manual processor input, two produced Planks, output transfer and worker delivery. Plank has no loose pickup spawn path in the existing prototype; its world-space probe uses only a temporary test presentation. Manual processor input stores a count and updates its label, rather than showing a static input mesh. Auto-feeder transport remains covered by M5 with the shared replacement visual prefab.

`Runtime/*.png` retains captures using the gameplay camera FOV and follow offsets, centered on the subject. `runtime.txt` contains measured character animation/ground results. Native walking/running poses include small floor excursions (approximately -0.10m to +0.15m); idle is planted to within roughly 5mm. Root motion is disabled; no new foot IK, carry clip, work clip or resource animation is synthesized.

The expanded render comparison caught complete occlusion of Plank output at the old presentation anchor inside the processor building. The integration now positions only that presentation anchor beside the existing output pickup zone. Its gameplay collider, resource buffer, ownership and transfer methods are unchanged. Differential captures now assert visible pixels for loose/world resources, sale flights and the settled processor output as well as both characters. The loose-ground fixture disables automatic collection before entering PlayMode, so random spawn overlap cannot turn its ground probe into a pickup-flight probe.

For the gameplay signature comparison, the baseline scene was exported from pre-change commit `ab70dbe41a918f70e6a20878485dbfd0dddaf7e2` into the isolated project's `Logs/AnimatedAssets/baseline.unity`. `AnimatedProductionIntegration.AuditGameplay` compares serialized MonoBehaviours, colliders, rigidbodies and scene-root transforms with semantic object references. Only new CharacterAnimationFeedback records and the intended cash pop target change from the old capsule anchor to VisualRoot are normalized. No movement, interaction, pooling, resource ownership or economy differences were excluded.

Reproduce with the Unity test runner (`-testPlatform EditMode`), `LumberCampPrototypeBuilder.ValidateFromCommandLine`, M3/M4/M5 `RunFromCommandLine`, and `LumberCampPrototypeBuilder.BuildWindowsPlayerFromCommandLine`. The normal scene builder also applies the production integration. Android/device performance remains unverified; existing Android texture limits and ETC2 import policy are retained.
