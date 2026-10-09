# M22 release configuration and device checklist

## Android release build

`LumberCampPrototypeBuilder.BuildAndroidPlayerFromCommandLine` applies and validates
the production profile before building:

- non-development build; script debugging, profiler connection, and deep profiling off;
- IL2CPP, Optimize Speed, ARM64, incremental GC;
- managed stripping Medium and engine-code stripping on;
- Android minimum API 26 and target API 36;
- portrait only;
- Player log disabled;
- public native symbols emitted as a sibling zip;
- test ad/store adapters and QA context-menu shortcuts excluded from non-development
  compilation.

Example:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath 'D:\IndustryTycoon\IndustryTycoon' `
  -executeMethod IndustryTycoon.Editor.LumberCampPrototypeBuilder.BuildAndroidPlayerFromCommandLine `
  -customBuildPath 'D:\IndustryTycoon\IndustryTycoon\Builds\IndustryTycoon-M22.apk' `
  -logFile 'D:\IndustryTycoon\IndustryTycoon\Logs\M22-android-release.log'
```

For development, use an explicit **Development Build** in Build Profiles and enable
script debugging/profiler only for the diagnostic run. `DEVELOPMENT_BUILD` deliberately
enables deterministic ad/store test adapters. Never upload that artifact. Return to the
release command above for the candidate APK; it clears development/debug/profiler flags.

## External release inputs still required

- production rewarded-ad SDK/ad-unit IDs and reviewed consent mapping;
- production store adapter/product IDs, Play Console products, sandbox accounts, and
  server-side receipt/fulfilment persistence;
- analytics provider configuration and reviewed retention/deletion policy;
- reviewed Privacy Policy URL and provider disclosures;
- release keystore/alias credentials and final version/version-code ownership;
- Play Console AAB/signing/upload validation (the milestone produces a local APK smoke
  artifact, not a store submission).

## Physical-device matrix

- Low: Android 8/API 26, 2-3 GB RAM, ARM64; verify stable 30 FPS, memory pressure,
  thermal throttling, battery saver, full warehouse and blocked routes.
- Mid: current mainstream Android, 4-6 GB RAM; verify 60 FPS target, 30-minute soak,
  repeated market/order/UI use, pause/resume, interruption, and poor network.
- High: recent Android flagship/high-refresh display; verify frame pacing, safe-area,
  portrait lock, and no refresh-rate-dependent timers.
- Notch/cutout/gesture navigation: narrow and tall screens, Android Back through every
  modal, keyboard focus loss, status/navigation insets, long localized labels, and
  maximum-value Cash/resources.
- Install lifecycle: fresh install, upgrade over the previous candidate with an existing
  schema-v9 save, corrupted-primary backup recovery, force-stop, OS process eviction,
  reboot, and uninstall/reinstall entitlement restore.
- Connectivity: offline launch, airplane mode, high latency, disconnect during rewarded
  completion and purchase pending/confirmation, then resume without duplicate grants.
- Background: Home, lock screen, incoming call, permission/provider activity, ad overlay,
  repeated background/foreground, and force-stop immediately after a meaningful action.
- Monetization sandbox: cancel/fail/complete rewards, max legitimate reward cadence,
  pending/deferred purchase, each Gem pack, Starter Pack purchase/restore, duplicate
  callbacks, refund/revoke policy, and unavailable-provider gameplay.
- Power/thermal: battery saver, thermal throttling, low battery, 30+ minute production and
  logistics soak, and background restriction.
