# IndustryTycoon UI Visual Rebuild — Pass 1 review

## Review setup

- Approved Industrial Playcraft reference reviewed beside the existing early-Lumber baseline and the rebuilt 9:16 runtime capture.
- Runtime states captured: early Lumber, active CarryStack, active Primary Objective, and later Empire progression.
- Validation resolutions: 360×640, 1080×1920, and 1440×3200 with simulated safe areas.

## First review findings and correction pass

The first capture established the intended cream/ivory, deep-teal, teal, and gold hierarchy, but the Objective and Empire progress fills were not receiving their serialized component binding. The same pass also exposed redundant progress text in the objective title.

Correction pass:

- moved `MobileProgressBar` to its own Unity script asset so serialized references resolve in Editor and player builds;
- regenerated the scene and repeated all four captures;
- removed duplicated trailing progress text from the concise objective title;
- verified meaningful Objective and Empire progress fills in the final captures.

## Final visual judgement

- Reference direction: PASS — tactile ivory chips/cards, deep-teal structure, teal interaction accents, and restrained gold progression are immediately recognizable.
- Two-second hierarchy: PASS — Cash, CarryStack, Empire, Pause, then the current objective.
- World dominance: PASS — the permanent HUD stays shallow and the compact objective card does not occupy the playfield.
- Component consistency: PASS — the same tokenized highlights, shadows, typography, icon wells, and state colors are reused.
- Prototype/default-Unity feel: PASS — stock flat-grey styling is absent from the rebuilt scope.

## Intentional deviations

- Gems are not shown because this gameplay state has no authoritative runtime relevance.
- Cash rate is not shown because there is no authoritative runtime rate source.
- The objective has no CTA/chevron because its current runtime presentation is informational, not interactive.
- Existing runtime/vector iconography and project fonts are retained; no unlicensed concept-image artwork or font was imported.
- Warehouse, Logistics, Market, Shop, Goals, Contracts, Achievements, and building sheets remain outside Pass 1.
