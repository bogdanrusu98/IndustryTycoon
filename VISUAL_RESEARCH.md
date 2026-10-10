# Modern visual rebuild — research and direction

Research date: 2026-10-10. Branch: `feature/core-prototype`. Unity 6000.5.9f1; M20 presenters and schema v9 remain authoritative.

## Reference study (before implementation)

Publisher-controlled Google Play screenshot galleries were opened and visually inspected. Store images can be promotional composites, not evidence of the complete live UI flow. Whiteout's current gallery includes a different minigame: do not infer its city HUD from that image. Support pages supplement the flow study; no claim of hands-on sessions or current market ranking is made.

| Reference / screenshot gallery | Observation / useful pattern | IndustryTycoon adaptation | Avoid |
|---|---|---|---|
| [Idle Miner Tycoon](https://play.google.com/store/apps/details?id=com.fluffyfairygames.idleminertycoon) | Large currency badge, explicit production tiers, silhouettes separated by floors | Cash chip; visually separate production, storage and upgrades | Promotional arrows, offers competing with gameplay |
| [Township](https://play.google.com/store/apps/details?id=com.playrix.township) | Clear roof shapes, grouped crops, sandy routes separating green parcels; compact corner currency | District palettes and connected paths, warehouse as a landmark | Dense decoration and unrelated minigame styling |
| [Hay Day](https://play.google.com/store/apps/details?id=com.supercell.hayday), [official gameplay flows](https://support.supercell.com/hay-day/en/game/index.html) | Product icons express input/output; broad open ground around production; level and currency occupy separate corners | Resource silhouettes, short recipes, modest progression badge | Permanent labels over every machine; copying its rustic brand |
| [Whiteout Survival](https://www.centurygames.com/games/a/), [gallery](https://play.google.com/store/apps/details?id=com.gof.global) | Strong environmental theme and focal structures; current promotional minigame uses fenced work areas and simple currency | A distinct mining terrain and industrial entrance, bounded work footprints | Assuming marketing screenshots represent every city screen; crowded event rails |
| [Idle Lumber Empire](https://play.google.com/store/apps/details?id=com.lumber.inc) | Portrait factory, road hierarchy, light ground, wood silhouettes, compact top resources | Neutral roads with restrained safety markings, generous station pads | Exaggerated piles, tutorial hands, constant upgrade spam |
| [Egg, Inc.](https://play.google.com/store/apps/details?id=com.auxbrain.egginc) | Upgrade rows: icon, short benefit, progress, right-side cost; bottom circular navigation; visible world below sheet | Reusable upgrade cards and compact bottom navigation; clear selected state | Ultra-condensed type and blur cost |
| [Hotel Empire Tycoon](https://play.google.com/store/apps/details?id=com.codigames.hotel.empire.tycoon.idle.game) | Building masses separated by promenades; coherent campus, readable room zones | Industrial campus with a northward destination and lateral logistics/mining branches | Uniform packing of every available square |
| [Idle Supermarket Tycoon](https://codigames.com/game/idle-supermarket-tycoon/), [gallery](https://play.google.com/store/apps/details?id=com.codigames.market.idle.tycoon) | Repeated work bays, circulation corridors, simple infrastructure silhouettes | Consistent building footprints and route connections | Many equally saturated interactive surfaces |

## Concrete design system

- Direction: **industrial resort**. Warm light surfaces, deep ink navigation, evergreen primary actions, amber rewards; muted sage lumber, slate mining, sand logistics and clay industrial ground. Production art supplies the visual detail.
- Typography at the existing 1080-wide reference canvas: title 48–64, section 36–42, body/action 30–34, secondary 26–28. Use sentence/title case for new headings and limited uppercase for short categories. Never depend on unsupported Unicode pictograms: draw a small consistent vector icon set in UGUI geometry.
- Spacing: 8 / 16 / 24 / 32 / 48; outer gutter 32; card padding 24; one section per surface. Rounded sliced sprites, 24-reference-unit corner treatment; restrained solid shadow instead of live blur.
- Resource chips: separate cash, carried inventory and empire level. Emphasize value, make unit secondary. One objective card below them. No permanent district-state paragraph.
- Buttons: primary evergreen, rewards amber, secondary quiet slate; dark text on bright controls, light text on dark controls. Minimum target 128 reference units (approximately 48 dp on a 405-dp-wide phone); label must fit independently of icon.
- Contextual production: bottom sheet, short header and recipe, buffer card, progress row, three equal upgrade cards, two specialization choices. Preserve visible world above it. No giant blank middle.
- Market: three order cards with strong action alignment. Warehouse: stock and routes grouped, goals below. Shop: product choices distinct from optional reward actions; provider state visible. No monetization logic changes.
- Navigation: muted background and separate icons/labels; selected button visibly highlighted. Retain M20 destinations and progressive unlock availability.
- Locked states: retain authoritative progression gates; quiet disabled controls plus readable reason. Selected states use accent and weight, not borders everywhere. Toasts are short and nonblocking; confirmation dialogs retain explicit cancel.
- Main menu: strong title, world visible above a compact action area. Settings use consistent vertical rows, clear controls and Android Back behavior.

## World composition

Keep the early lumber loop compact for pacing. Expand the mine northeast, warehouse northwest and industrial destination north, moving each complete gameplay hierarchy together only for intentional layout changes. Do not rotate gameplay roots to fix models. Give districts connecting avenues and substantial unbuilt transition ground. Keep colliders off decoration. Update bounds, landmarks and all route anchors together; measure travel deltas at the unchanged movement speed.

Use `GameplayRoot → VisualRoot → Model` for imported geometry, preserving the imported model's axis correction. Inspect every staging FBX, including duplicates and unnamed exports, with geometry and hierarchy records plus rendered previews. Decide coverage from that evidence, not filenames or the old matrix.

Re-evaluate the camera only on the recomposed scene. Start with the existing portrait framing, then compare actual 9:16 runtime captures before choosing any offset/FOV change. Keep resource and character scale readable.

## Implementation support

- [Unity ModelImporter axis conversion](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-bakeAxisConversion.html): model axis correction can reside on the import root; never overwrite it blindly.
- [Unity Screen.safeArea](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Screen-safeArea.html): anchor UI within the reported safe rectangle, accounting for portrait cutouts.
- [Unity UI optimization guidance](https://create.unity3d.com/Unity-UI-optimization-tips): event-driven presenters, no decorative raycast targets, avoid repeated layout rebuilds and excessive transparent overlap.
- [Unity community performance discussion](https://discussions.unity.com/t/unity-ui-performance-tips-sharing-my-findings/697057): useful corroboration of canvas rebuild concerns; optimization decisions still require measurements.

## Acceptance review

Capture early/developed lumber, mining, logistics, industrial, empire, production/upgrades, market, goals, main menu, shop and settings at 540×960; repeat critical screens at 540×1200 with simulated insets. Two documented visual review/fix passes are required. Compare world coverage, 2-second hierarchy, silhouette, orientation, whitespace, text and targets against these references. Compilation alone is not visual acceptance. Report editor measurements separately from unmeasured device performance.
