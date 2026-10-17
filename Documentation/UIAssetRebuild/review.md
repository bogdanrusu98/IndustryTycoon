# Industrial Playcraft — uploaded asset integration

Worktree: `D:/IndustryTycoon/IndustryTycoon-ui-pass1`, branch `feature/core-prototype`.
The primary checkout remains on `codex/animated-production-exports`; it is not the
checkout used for these screenshots or the release APK. `main` is unchanged.

## Source assets and integration

All eight PNGs supplied in the primary checkout's `PNGs` folder are preserved
byte-for-byte in `Assets/Game/UI/Art/Meshy`. `asset-provenance.json` records SHA256
checksums. Cash, Empire and Objective use the uploaded coin, star and flag. The
existing Goals, Awards and Factory controls use the clipboard, trophy and hardhat;
their navigation actions and progression gating are unchanged.

The uploaded cream and teal card sources have ornate inset frames, and the cream
source has background-removal residue. They remain imported source references.
Production backgrounds are clean code-generated, sliced cream/teal sprites with
rounded edges, subtle vertical shading, highlighted rims and contact shadows.
This avoids stretching ornamentation and dirty edges into the HUD.

The committed animated player/worker and lumber-resource integration (`f06620b`)
was brought into this branch as `6bf579d` so the UI and the latest existing world
assets are delivered together. The unrelated primary checkout's uncommitted
changes remain untouched. Resource icons are rendered from all seven actual
resource roots, including the new Wood and Plank models.

Barlow Semi Condensed SemiBold/Bold are bundled with their OFL license, from
https://github.com/google/fonts/tree/main/ofl/barlowsemicondensed. This approximates
the reference typography; it is not a claim to have identified its original font.
Legacy text shadows are removed. Both Editor and player reference the same fonts.

## Composition

At 1080×1920 the cream cards start 42 design pixels below Safe Area, are 112 pixels
high, and share one 984-pixel row with a separate 106-pixel pause button. Cash uses
a large coin and dominant amount; Carry shows its resource and capacity on two
lines; Empire has a large star, level and authoritative level-step progress.
Empty Carry remains hidden and full Carry adds a small gold badge.

The dedicated 984×112 Objective card starts at y=174 and keeps M17/M18 data and
bindings. Its category is secondary, its count is right-aligned, and there is no
nonfunctional chevron. Warnings use a separate centered 520×48 hint. Pickup feedback
uses a 180×78 resource-plus-delta chip, closer to the player, retaining the existing
event subscriptions and pop/hold/fade coroutine. Progress relief uses vertex
shading without extra overlay geometry or polling.

M20 navigation ownership, Android Back handling, SafeAreaFitter, economy, resource
rules and save schema v9 are retained. No camera or world placement edits are part
of this UI correction.

## Actual visual iterations

Each folder below contains seven unpainted 1080×1920 Unity Play Mode captures.
Fixtures use real runtime services and disable persistence writes.

1. `Documentation/UIHudComposition/meshy-iteration1`: imported illustrations,
   actual resource models, new typography and reusable surfaces. Comparison found
   the 128-pixel row / 130-pixel objective too tall and the category too prominent.
2. `meshy-iteration2`: first visual correction. Reduced cards/objective to 112 px,
   aligned pause, corrected icon/text padding and changed the heading to
   `Objective`. Checked full Carry, wrong resource and pickup states. Found the
   pickup too large/far below the player and warning width excessive.
3. `meshy-iteration3`: second visual correction. Smaller, repositioned pickup,
   narrower centered hint, stronger numeric weight and subtle progress relief.
   Objective keeps a numeric count only. Batch capture explicitly settles dynamic
   text geometry before readback. Final screenshots were inspected again.

`Tools/UIVisualCorrection/CompareAssets.ps1` produces **OLD | APPROVED REFERENCE |
NEW** at 270×480 per phone and an enlarged HUD comparison. OLD is the user's latest
rejected screenshot, not a substituted baseline. Both early-lumber and developed-
mining NEW comparisons are included, with different legitimate gameplay states.

Reproduce capture with Unity `-batchmode -executeMethod
IndustryTycoon.Editor.IndustrialPlaycraftCapture.RebuildAndRun -visualIteration
meshy-iteration3`. Run the regression/build sequence with
`Tools/UIVisualCorrection/ValidateAssets.ps1`.

## Visual limits

The top HUD and objective now follow the reference's proportions and illustrated
hierarchy. The uploaded art is glossier than the illustration; the exact original
font is unavailable. The early world remains sparse, the animated player differs
from the reference, and existing world labels, vegetation, joystick presentation
and lower navigation composition are not a whole-screen 1:1 reconstruction.
The uploaded card rasters are not used verbatim as production panels. No physical
Android-device visual acceptance is implied by the Editor captures.

## Technical references

- Unity sliced Image API: https://docs.unity3d.com/Packages/com.unity.ugui@2.0/api/UnityEngine.UI.Image.Type.html
- Rounded panel / Full Rect discussion: https://discussions.unity.com/t/making-a-panel-background-with-well-rounded-corners/796467
- Unity dynamic font lifecycle: https://docs.unity3d.com/ScriptReference/Font.RequestCharactersInTexture.html

Current-run test and build results are recorded in `validation.md`.
