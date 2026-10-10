# Modern visual rebuild — source recovery and coverage

This replaces the conclusions of the earlier visual-completion coverage report. All **21 FBX files (20 unique byte hashes) and 86 PNG files** in `Meshy Import` were enumerated and reinspected on 2026-10-10. No source staging files were modified.

## Source evidence

`ModernAssetRecoveryAudit.Run` imports every FBX independently, walks every transform/renderer including inactive children, records SHA-256, geometry, normals, pivot/bounds, import scale and axes, then renders it. Every source has one mesh/renderer, one material slot, a complete normal array, an active/enabled imported root, and the importer correction **X = −90°, scale 100, fileScale .01**. No hidden mesh child contains an undiscovered building or prop pack. Pivots are source-centred, so buildings require grounding after normalization; rotating machinery requires a centred pivot instead.

The PNG audit decodes all 86 images and records dimensions/mode/hash. Contact sheets cover diffuse, normal, metallic, roughness, emission and packed metallic/roughness maps. Black metallic/emission images are valid data, not missing images; packed pink/yellow maps must never be assigned as base color. Production retains the established mobile texture import limits and opaque URP materials.

Full local evidence: `Logs/ModernVisual/SourceAudit/{all-files,geometry,texture-inspection}.txt`, numbered source PNGs and texture sheets. Source IDs below refer to those numbered renders.

Portable manifests, texture sheets and the [source contact sheet](Documentation/ModernVisual/source-contact.jpg) are committed under [Documentation/ModernVisual](Documentation/ModernVisual). The [runtime detail sheet](Documentation/ModernVisual/production-details.jpg) covers the integrated geometry and recovered uses.

| ID | Actual geometry | Triangles | Exact staging FBX | Decision |
|---|---|---:|---|---|
| 00 | Saw blade | 984 | `Meshy Import\Meshy_AI__1010143101_texture_fbx\Meshy_AI__1010143101_texture_fbx\Meshy_AI__1010143101_texture.fbx` | Production source |
| 01 | Rock cluster | 1155 | `Meshy Import\Meshy_AI_assets_20261010_143203\01a11bdd-bc4a-7206-b3e2-728b72cdaef2\Meshy_AI_model.fbx` | Production source |
| 02 | Rugged rock | 1347 | `Meshy Import\Meshy_AI_assets_20261010_143203\01a11f79-b8d8-70ce-94a8-c42dacde2a16\Meshy_AI_model.fbx` | Production source |
| 03 | Ore hopper | 1957 | `Meshy Import\Meshy_AI_assets_20261010_143203\01a11f90-292f-770b-8bf2-8314bebc5156\Meshy_AI_model.fbx` | Production source |
| 04 | Drill bit | 1983 | `Meshy Import\Meshy_AI_assets_20261010_143203\01a11f97-02b7-77b9-a003-67553e7a3d3a\Meshy_AI_model.fbx` | Production source |
| 05 | Ironforge furnace/workshop | 4620 | `Meshy Import\Meshy_AI_assets_20261010_143203\01a11f9c-89d1-71f1-89c8-f436c516cd57\Meshy_AI_model.fbx` | Production source |
| 06 | Industrial gate | 4667 | `Meshy Import\Meshy_AI_assets_20261010_143203\01a11fa0-dae9-73b3-a128-aed7b02d9b11\Meshy_AI_model.fbx` | Production source |
| 07 | Warehouse | 4676 | `Meshy Import\Meshy_AI_assets_20261010_143203\01a11fb0-5f5d-729e-9200-ee0e7be954ad\Meshy_AI_model.fbx` | Production source |
| 08 | Assembly plant | 2697 | `Meshy Import\Meshy_AI_assets_20261010_143203\01a11fb9-684d-7538-a1c7-61cdac316330\Meshy_AI_model.fbx` | Production source |
| 09 | Alternative rock cluster | 1154 | `Meshy Import\Meshy_AI_assets_20261010_143203\1f9936f8-76be-44f2-b9d7-3607ec08df42\Meshy_AI_model.fbx` | Valid 1,154-triangle alternative; redundant rock role, not rejected as high polygon. Existing two variants reuse materials. |
| 10 | High-density rock | 16618 | `Meshy Import\Meshy_AI_assets_20261010_143203\29d69076-c3f4-41a0-ba2f-d92a3875a948\Meshy_AI_model.fbx` | 16,618 triangles; redundant rock role and appreciably more geometry. |
| 11 | Courier body | 5168 | `Meshy Import\Meshy_AI_assets_20261010_143217\01a11159-74a2-73d6-9376-09791372ba1f\Meshy_AI_model.fbx` | Production source |
| 12 | Courier wheel | 936 | `Meshy Import\Meshy_AI_assets_20261010_143217\01a1115d-3906-7746-8f0b-1bbb0ce44658\Meshy_AI_model.fbx` | Production source |
| 13 | Mine barrier | 1427 | `Meshy Import\Meshy_AI_assets_20261010_143217\01a11bd7-a654-7123-bd85-09ebd0943271\Meshy_AI_model.fbx` | Production source |
| 14 | Mine entrance | 5660 | `Meshy Import\Meshy_AI_assets_20261010_143217\a0cc80d2-d40d-4bf7-9a72-61668b0e70ba\Meshy_AI_model.fbx` | Production source |
| 15 | Iron ingot | 568 | `Meshy Import\Meshy_AI_iron_ingot_mobile_pro_1010143038_texture_fbx\Meshy_AI_iron_ingot_mobile_pro_1010143038_texture_fbx\Meshy_AI_iron_ingot_mobile_pro_1010143038_texture.fbx` | Production source |
| 16 | Wood crate | 1150 | `Meshy Import\Meshy_AI_Mobile_Lowpoly_Wooden_1010143009_texture_fbx\Meshy_AI_Mobile_Lowpoly_Wooden_1010143009_texture.fbx` | Production source |
| 17 | Wood crate duplicate | 1150 | `Meshy Import\Meshy_AI_Mobile_Lowpoly_Wooden_1010143009_texture_fbx\Meshy_AI_Mobile_Lowpoly_Wooden_1010143009_texture_fbx\Meshy_AI_Mobile_Lowpoly_Wooden_1010143009_texture.fbx` | Byte-identical to 16; no second production copy. |
| 18 | Toolbox | 1621 | `Meshy Import\Meshy_AI_Mobile_Stylized_Toolb_1010143046_texture_fbx\Meshy_AI_Mobile_Stylized_Toolb_1010143046_texture_fbx\Meshy_AI_Mobile_Stylized_Toolb_1010143046_texture.fbx` | Production source |
| 19 | Iron ore | 1256 | `Meshy Import\Meshy_AI_Optimized_Iron_Ore_1010143030_texture_fbx\Meshy_AI_Optimized_Iron_Ore_1010143030_texture_fbx\Meshy_AI_Optimized_Iron_Ore_1010143030_texture.fbx` | Production source |
| 20 | Saw station/conveyor shell | 6835 | `Meshy Import\Meshy_AI_Saw_Station_Optimized_1010143052_texture_fbx\Meshy_AI_Saw_Station_Optimized_1010143052_texture_fbx\Meshy_AI_Saw_Station_Optimized_1010143052_texture.fbx` | Production source |

## Entity → source → runtime visual → placeholder → reason

Paths below are relative to `Assets/Art/Production`; complete staging paths are in the table above.

| Entity | Source | Runtime visual | Placeholder? | Reason / recovery |
|---|---|---|---|---|
| Wood processor | 20 | `Machines/Processor` below VisualRoot | No | Upright station shell, imported axis correction preserved |
| Processor blade | 00 | `Machines/SawBlade` at Working Blade | No | Centred rotating art; nonuniform anchor scale cancelled before facing |
| Tool workshop | 05 | `Buildings/ToolWorkshop` | No | Furnace/workshop silhouette |
| **Smelter** | **05 reused** | **Recovered Smelter Production Visual** | **No for building shell** | Existing Ironforge geometry is a usable furnace; previous primitive furnace/chimney hidden |
| **Packing station** | **20 + 16 reused** | **Recovered Packing Production Visual + Packing Parcel Production Visual** | **No for station shell/parcel** | Conveyor/table export has no attached blade; suitable packaging surface, production crate added |
| Central warehouse | 07 | `Buildings/Warehouse` | No | Grounded building |
| Assembly plant | 08 | `Buildings/AssemblyPlant` | No | Upright building, art remains child of original gameplay root |
| Industrial gate | 06 | `Buildings/IndustrialGate` | No | Visual grounded at world floor despite elevated blocker anchor; original gate lifecycle retained |
| Mine barrier | 13 | `Buildings/MineBarrier` | No | Locked-state art |
| Mine entrance | 14 | `Buildings/MineEntrance` | No | Northeast district landmark |
| Mine rocks / escarpment | 01 + 02 | `Environment/MineRockA/B` | No | Grounded; repeated low-cost variant uses existing mesh/material |
| Ore storage | 03 | `Machines/OreHopper` | No for bin | Production hopper |
| Automated drill | 04 | `Machines/DrillBit` | Partial | Bit recovered and correctly centred; no mast/chassis export exists among all 20 unique sources, so existing authored mast remains |
| Courier | 11 + 12 | `Vehicles/Courier`, four `CourierWheel` instances | No | Imported orientation preserved; wheel axes aligned with existing X-axis animation |
| Crate | 16 (17 duplicate) | `Resources/WoodCrate` carry/pool variants | No | Single-mesh production variant, axis correction restored |
| Iron ore | 19 | `Resources/IronOre` carry/pool variants | No | Same bounded pools, upright resource |
| Iron bar | 15 | `Resources/IronBar` carry/pool variants | No | Ingot rests along its long axis |
| Tool | 18 | `Resources/Tool` carry/pool variants | No | Toolbox is upright, handle above body |
| Player / lumber worker | None in all 20 unique sources | Crafted Worker VisualRoot: uniform, hardhat, boots, gloves | Authored fallback | No human/rig/character mesh; old visible capsules replaced with assembled character art, controllers retained |
| Wood / plank / cash | None in all 20 unique sources | Existing readable resource meshes | Yes | No log/plank/currency geometry or child in staging; toolbox/crate cannot substitute their resource identity |
| Machine part | None in all 20 unique sources | Existing part resource mesh | Yes | No machine-part export; drill bit is already a full-sized machine attachment, not this recipe output |
| Stockpile | None in all 20 unique sources | Authored storage pad and resource stacks | Partial | No stack/rack source; uses existing wood presentation |
| Auto feeder | No continuous route model | Authored slim rails/slats/rollers | Partial | Saw-station shell is a discrete workstation, cannot cover the existing bent transport path without obstructing it; route and moving resource feedback retained |
| Market kiosk | None in all 20 unique sources | Authored kiosk | Yes | No booth/kiosk export; neither vehicle wheel nor industrial gate is suitable |
| Purchase/transfer zones, progress bars, guidance markers | Code-native visual primitives | Existing authoritative presentation bindings, rethemed materials | Intentional UI geometry | These communicate gameplay regions, not missing production buildings |
| Trees, paths, footprints | Code-native geometry | Modern District Composition | Authored scenery | No vegetation/road exports; no colliders added |

## Integration corrections

Hierarchy is `GameplayRoot → legacy presentation anchor → VisualRoot (scale compensation) → Model Facing → imported Model (native axis correction)`. Compensation happens **before** facing rotation, avoiding shearing beneath the old thin cylinder/cube anchors. Bounds are measured after transformation; grounded buildings and centred rotating parts use separate placement policies. Resource variants retain one mesh renderer, preserving pool assumptions.

No authoritative root was rotated to repair art. Intentional whole-district translations are documented separately in `MODERN_VISUAL_REBUILD.md`. Disabled legacy renderers remain available as deterministic fallback objects; they are not visible placeholders.

The earlier claim that an “Industrial Props Pack” existed was incorrect. The full inventory contains only the listed files. File byte size alone was also insufficient evidence of polygon cost for source 09; this report corrects that conclusion.
