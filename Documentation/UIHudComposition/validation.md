# Validation — HUD composition correction

- EditMode: **85/85 PASS**, zero failed/skipped. `Logs/composition-editmode.xml`.
- M18 onboarding: **PASS** fresh/resume/no-replay/M17 handoff/input/marker cleanup. `Logs/composition-m18.log`.
- M17 Empire: **PASS** fresh → Lumber → Mining → Tools → Logistics → Industrial → Machine Parts → Specialization → Market → Empire 25. `Logs/composition-m17.log`.
- M2 gameplay: **PASS** sale 12 + 12 + 1 wood; early sale exit stopped; pooled feedback settled; HUD converged; completion count 1; production delta 2 → 4 logs / 2.7s. `Logs/composition-gameplay.log`.
- Scene validation: **PASS** `LumberCampPrototypeBuilder.ValidateFromCommandLine`. `Logs/composition-scene-audit.log`.
- Camera, resource artwork/bindings, warnings/objective separation, Pause pointer raycast and existing Back handler: **PASS**, see `final/runtime-validation.txt`.
- Safe Area: **PASS** at 360x640, 1080x1920, 1440x3200 with inset safe rectangles; see `final/mobile-validation.txt`.
- Schema: **v9 unchanged**; no persistent gameplay/data schema files modified.
- Scene scope: 33 existing records changed, all UI/feedback or Main Camera; 145 presentation records added, 118 replaced. `scene-delta.json` ignores serialization whitespace.

## Additional legacy test limitation

The exploratory M11 vertical-slice run was **not PASS**: it stopped at its exact fresh Mining-state guard. A baseline rerun using the committed scene, committed M11 test and committed changed runtime presentation files also failed, at its fixed camera requirement `(0,14,-10.6)` while the committed scene uses `(0,21,-16)`. This is not counted as a successful regression test. No changes to that legacy test are retained. The current M17 Mining-to-Empire path and the focused current-camera contract above passed. Logs: `composition-m11.log`, `composition-m11-baseline.log`, `composition-camera-capture.log`.

## Android and device coverage

Android Release result and APK checksum are recorded below. `adb devices` reported no connected devices: no physical-device installation, hardware Back gesture or on-device screenshot is claimed. Back is verified through the existing navigation handler and no Android input implementation was changed.

Android Release: **PASS**; ARM64 / IL2CPP, assembleRelease, zero build warnings. APK: IndustryTycoon-HudComposition-Release.apk, 42474355 bytes. SHA256: 1D66EBC7C5A033BA0452AA08559355AE21DFDBF328491BF5410362432CE75523. The Unity BuildReport total includes intermediate/symbol outputs; this is the actual APK file size.
