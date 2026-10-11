Run from the repository root with Python 3 + Pillow. Sources are read-only.

1. In Unity batch mode, execute IndustryTycoon.Editor.ExhaustiveAssetUsageAudit.InspectSources (-quit).
2. Run inspect_textures.py, then inspect_fbx.py. This decodes every external and embedded image, writes hashes/individual decisions/contact sheets under Logs/AssetUsageAudit/Sources, and retains original source files.
3. Execute IndustryTycoon.Editor.ExhaustiveAssetUsageAudit.Run (omit -quit: it enters/exits Play Mode and exits on completion). It checks 36 normal-camera views and authoritative gating.
4. Run classify_placeholders.py after the static and runtime renderer censuses exist.
5. For serialized gameplay comparison, write the desired baseline scene from Git to Logs/AssetUsageAudit/baseline.unity, then execute ExhaustiveAssetUsageAudit.FinalizeStaticAudit (-quit).

The source/image tools deliberately keep decoded full-size images in ignored Logs only. Committed contact sheets and manifests carry the portable evidence. New sources require fresh inspection and a new manifest decision; unknown source models must never be automatically labeled unsuitable.

The Play Mode audit uses temporary in-memory restoration fixtures and disables LocalPersistenceService before entering Play Mode. Back up user saves separately before running the broader project regression suite, which includes persistence tests.
