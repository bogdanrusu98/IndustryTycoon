# Industrial Playcraft HUD composition rebuild

The previous correction at `3d70ce6385afa1a7fc3cdc2825b061d64c7677bc` is the rejected baseline. Work stays on `feature/core-prototype`; `main` and the separate primary checkout are outside this change.

## Structure and bindings

`IndustrialHudBuilder` reconstructs the top row and dedicated objective. Existing WalletHud, WoodHud, EmpireLevelView, NextUnlockGuidance, Button and bound value Text objects survive. Three shared layered shells use a lower bevel, inset cream face, inner edge highlight and contact shadow. Coin/star/flag are purpose-built illustrated sprites. Resource icons render the seven actual carried resource models; Wood/Plank use a bundle. Values occupy the information zone; CASH/EMPIRE descriptors are removed. Pause is an independent 118-square button.

The objective has a 110 px flag, a secondary heading, a title with two-line capacity and right-aligned authoritative progress. M17/M18 determine its content. No invented income rate or nonfunctional chevron. Empire's bar represents completed level steps from 1 through 25, not fabricated XP within the current level. Empty Carry disappears; full Carry has a small badge. Context hints and M18 explanations are separate compact transient cards. Pickup uses the actual icon plus delta and a 160 ms pop followed by the existing hold/fade coroutine.

The camera's FOV remains 43. Only follow/look-at offsets change: from `(0,21,-16)` / `(0,1,0)` to `(-1,14.5,-11)` / `(-1,1,1.5)`. This brings the player and production buildings closer and places the player lower/right in the portrait. No movement, collisions, building placement, unlocks, resource rules, economic values or save fields change.

## Visual iterations

All captures come from Unity Play Mode at 1080x1920 using runtime service fixtures with persistence disabled. No painted or generated game screenshots. The approved source is the user's original attachment; the rejected inputs are the prior delivered captures. Cash, progress and objectives reflect different legitimate fixture states, not values copied from the reference.

1. `iteration1`: full structural rebuild, new artwork, resource bundles, larger text, 160 px top cards / 148 px objective, first closer camera. Inspection found excessive side-building cropping and insufficient objective height for the longest title.
2. `iteration2`: warmer cream, icon/text spacing, intermediate camera distance and forward framing. Inspection found too much empty space above buildings, the final objective losing its last word, and the old M18 explanation banner competing with the objective.
3. `iteration3`: final camera framing, two-line objective title, square pause, compact M18 explanation surface. Prior-fixture queued notifications are cleared only in the capture harness. Two visual correction iterations after the initial rebuild are complete.
4. `final`: identical reviewed composition, with an additional real pointer-raycast check for Pause. The layered button's root remains raycastable; decorative children do not intercept input.

`APPROVED-REJECTED-NEW-final-thumbnail.png` compares the early Carry state at 270x480 per phone. `APPROVED-REJECTED-NEW-final-world-thumbnail.png` compares the developed mining area, preserving each captured world rather than substituting the reference world. Enlarged HUD crops are also provided. At thumbnail size the new row is icon-led, rounded, dimensional, and visibly heavier than the rejected row; the objective remains a separate teal card.

## Remaining visual differences

This is a top-HUD/objective and camera correction, not a replacement of all environment art or bottom navigation. The existing early lumber field is still sparse; ground materials, world-space production labels, upgrade pads, vegetation density and lower navigation differ from the illustration. The developed mine and player are larger and readable. No claim of pixel-identical whole-screen reproduction or physical-device visual acceptance is made. Empty Carry intentionally leaves its slot transparent.

## Evidence and validation

Runtime and Safe Area evidence is in `final/runtime-validation.txt` and `final/mobile-validation.txt`. Reproduce with `Tools/UIVisualCorrection/ValidateComposition.ps1`; comparison tooling is `CompareComposition.ps1`. Validation results and scene scope audit are recorded separately in `validation.md` and `scene-delta.json`.

## Sources and artwork

The implementation uses Unity's sliced Image geometry for preserved rounded corners and serialized scene bindings for Editor/build parity. Research references: [Unity 9-slicing](https://learn.unity.com/tutorial/using-9-slicing-for-scalable-sprites?courseId=5dfbdefcedbc2a1fab6019e2), [Unity Camera API](https://docs.unity3d.com/cn/6000.0/ScriptReference/Camera.html), and [Unity rounded panel discussion](https://discussions.unity.com/t/making-a-panel-background-with-well-rounded-corners/796467). Built-in imagegen prompts and project paths are in `icon-provenance.md`.
