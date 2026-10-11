> REJECTED by the user. Preserved as the previous comparison input; superseded by Documentation/UIHudComposition/review.md. Prior visual conclusions below are historical, not current acceptance.

# Industrial Playcraft HUD — Pass 1 correction

Supersedes the visually rejected Pass 1 at `eae10c5`. Work is isolated to `feature/core-prototype` in the existing UI worktree. The other checkout and `main` are untouched.

## Structural changes

The old icon wells, small glyphs and duplicated card children are replaced by a 984-unit top row: large layered coin with dominant balance; carried-resource artwork with name, count and capacity; large gold Empire star with a full-width progression track; and a matching 108×120 pause control. Cream surfaces have rounded corners, a warm vertical shade, edge highlights and lower shadows. Original Cash/Wood/Empire Text objects and presenter owners remain bound.

Seven transparent resource images are baked from the actual `WoodCarryVisual` resource roots. They are serialized sprite references, with no runtime asset lookup or Editor API. Coin/star/flag artwork uses layered UI geometry. Empty Carry is invisible and does not intercept input, while its bound count stays current. A full stack adds a small badge.

The dedicated deep-teal objective card sits below the row, with a gold flag, OBJECTIVE caption, M18 instruction or M17 objective, and progress. Context hints have their own compact surface and cannot replace the objective. Pickup feedback now presents a resource image and +amount, using the existing transaction coroutine and FeedbackTween for its fade. Pause retains its original action and M20 navigation owner.

## Visual evidence and iterations

- `old/`: rejected Pass 1 runtime captures, preserved before edits.
- `approved-reference.png`: exact user-supplied reference.
- `initial/`: initial rebuild, including the first rendering defects.
- `correction1/`: first correction fixes sprite import mode and missing CanvasRenderer. Re-capture revealed black resource mattes, over-rounded pause strokes and unsettled wallet values.
- `correction2/`: second correction recovers sprite alpha from dual-matte geometry renders, adds warm surface shading, refines pause strokes and captures at 1080×1920 after the wallet animation settles.
- `final/`: same visual composition, re-captured after regression fixes and audited runtime contracts.
- `OLD-REFERENCE-NEW-thumbnail.png`: complete phones at 270×480 each.
- `OLD-REFERENCE-NEW-hud.png`: enlarged comparison of the requested HUD/objective region.

Both correction iterations were performed after inspecting the previous actual Unity capture. The final comparison shows icon-first chips, separate dark objective, subordinate hints, and the reference's cream/teal/gold top composition. The permanent HUD ends at 15% of screen height in 9:16, leaving the world visible.

## Runtime and stability

`IndustrialHudBuilder` rebuilds only the HUD presentation while retaining runtime owners. The normalized scene audit lists 28 changed existing records, all in the affected HUD/toast objects; gameplay records do not change. The new children replace the old decoration. M20 panel navigation, SafeAreaFitter, progression rules, economy and save schema v9 remain in place.

Regression checks exposed a hidden Carry label retaining its previous count; the label now remains synchronized even when invisible. A scene-teardown UI callback also exposed destroyed production objects being accepted through an interface. `IndustrialDistrictService.FindBuilding` now uses Unity's destroyed-object check before the interface match. An EditMode regression test covers that case. Temporary context hints are cleared on restore so they cannot leak into a new/resumed session.

## Intentional differences and limits

- Resource artwork matches this branch's actual carried resources; the reference's illustrated plank bundle is not substituted for Wood.
- Empire's bar retains the existing authoritative overall level range (1–25), not invented XP or an invented next-level percentage. Level 1 is empty and level 25 is full.
- No cash-rate label: no authoritative rate source exists. No objective chevron: there is no navigation action on this informational card.
- Existing world art, bottom navigation and other screens are outside this correction.
- Safe-area and Back behavior are exercised in Editor runtime fixtures; physical Android device/cutout testing is not performed here.

## Reproduction

Run `Tools/UIVisualCorrection/Validate.ps1`, optionally passing `-UnityPath`, to capture the final scene, run EditMode tests, M18 and M17 smoke tests, the M2 full gameplay loop, scene audit and Android ARM64 IL2CPP Release build. Run `Tools/UIVisualCorrection/Compare.ps1` to regenerate the comparison artifacts. Rebuild the HUD with `IndustryTycoon.Editor.IndustrialPlaycraftCapture.ApplyCoreHudFromCommandLine`. `RebuildAndRun` also refreshes the baked HUD art and captures an iteration chosen by `-visualIteration`.

Validation results are recorded in `validation-results.txt`, `final/runtime-validation.txt` and `final/mobile-validation.txt`.

## Implementation references

- [Unity Learn: 9-slicing scalable sprites](https://learn.unity.com/tutorial/using-9-slicing-for-scalable-sprites?courseId=5dfbdefcedbc2a1fab6019e2): stable corner borders and compatible Canvas scaling.
- [Unity Canvas API](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/canvas): existing Canvas rendering and scale ownership retained.
- [Unity community: rounded panel backgrounds](https://discussions.unity.com/t/making-a-panel-background-with-well-rounded-corners/796467): reviewed common import/slicing pitfalls; the project's own rendering and tests determine the implementation.
