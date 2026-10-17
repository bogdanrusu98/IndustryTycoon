# Validation — uploaded Industrial Playcraft assets

Validated on Unity 6000.5.9f1, 2026-10-17, from `feature/core-prototype` in
`D:/IndustryTycoon/IndustryTycoon-ui-pass1`.

| Check | Result | Evidence |
| --- | --- | --- |
| EditMode suite including animated production rendering | PASS, 88/88, 0 skipped | `tests.xml`, `Logs/meshy-editmode.log` |
| M18 onboarding | PASS: fresh/resume/no replay/M17 handoff/input/marker cleanup | `regression-results.txt` |
| M17 progression | PASS through Empire 25 | `regression-results.txt` |
| M2 gameplay loop | PASS: sale 12+12+1, early exit, pooling, HUD convergence, production upgrade | `regression-results.txt` |
| Prototype scene validator | PASS | `Logs/meshy-scene-audit.log` |
| Resource HUD | PASS, all 7 resource icons/labels; empty/full states | `../UIHudComposition/meshy-iteration3/runtime-validation.txt` |
| Objective/warning independence | PASS, 3 contextual warnings | same runtime report |
| Empire restore/progress | PASS, Lv1/empty to Lv25/full | same runtime report |
| Pause / Back | PASS, pointer raycast, existing navigation action and Back handler | same runtime report |
| Safe Area | PASS, 360×640 / 1080×1920 / 1440×3200 with insets | `../UIHudComposition/meshy-iteration3/mobile-validation.txt` |
| Uploaded emblems and bundled fonts | PASS; no legacy text shadows | runtime report and `asset-provenance.json` |
| Save schema | v9 unchanged | runtime report; no persistence schema edits |
| Scene scope | PASS, changed/added/removed records belong to UI | `scene-delta.json` |
| Android Release | PASS, ARM64 IL2CPP APK | `android-release.json`, `Logs/meshy-android-release.log` |

The initial EditMode command used `-nographics`; the newly integrated animated
asset test needs pixel readback and correctly failed with zero changed pixels.
The runner was corrected to use a graphics device. The entire unmodified suite
then passed 88/88; the test was not disabled or weakened. Initial diagnostic logs
remain in `Logs/meshy-editmode-nographics.*`.

Android artifact: `Builds/Android/IndustryTycoon-Playcraft-Release.apk`, 47,673,533
bytes. SHA256 `924E9282B90B692480E5DE0628B5A0193016EC8D5579FBF7DDFD8F836C771254`.
`aapt dump badging` confirms `arm64-v8a`, minimum SDK 26, target SDK 36, and no
`application-debuggable` flag. Unity BuildReport reports zero build warnings;
the Editor log also contains Unity-package shader precision/deprecation warnings.
The larger BuildReport total includes intermediate and symbol outputs.

`adb devices` found no connected device. No physical-device installation,
hardware Back gesture, GPU performance measurement or on-device screenshot is
claimed. The final images are Editor Play Mode captures, not Android captures.

The scene audit is against `6bf579df1876c00cd47c0086bef93ec6dad5d1d9`, after bringing
in the previously committed animated assets. It reports 37 existing UI records
changed, 127 added, 156 replaced/removed. It rejects changes outside RectTransform
owners. Unity-generated IDE settings, graphics/build caches and whitespace-only
production importer rewrites were excluded from the UI commit.

All three screenshot iterations and their thumbnail comparisons were inspected.
This validates the top-HUD/objective correction, not pixel-identical whole-world
or lower-navigation reproduction. See `review.md` for remaining visual differences.
