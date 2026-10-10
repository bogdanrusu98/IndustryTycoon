# M19 Production Asset Integration

## Integration rules

- Source staging remains untouched under `Meshy Import/` and is intentionally untracked.
- Production art lives under `Assets/Art/Production/` with one deterministic FBX export per asset.
- Gameplay roots, scripts, triggers, colliders, save IDs, buffers and route endpoints are unchanged.
- Building and machine art is attached below dedicated `* Production Visual` children. Resource meshes replace presentation geometry only inside the existing `ResourceVisual` variant roots.
- Imported visuals contain no Collider or Rigidbody. Existing primitive/CharacterController/trigger collision remains authoritative.
- Missing asset categories retain their accepted placeholder visuals.

## Integrated inventory

All source color/normal textures are 2048×2048. Unity limits building, machine and vehicle maps to 1024; carried resource maps are limited to 512. Each integrated model uses one URP/Lit material.

| Source FBX | Final Unity path | Mapped entity | Vertices | Triangles | Textures at runtime max | Collider strategy | Animated part / notes |
|---|---|---|---:|---:|---|---|---|
| `Meshy Import/Meshy_AI_assets_20261010_143203/01a11f90-292f-770b-8bf2-8314bebc5156/Meshy_AI_model.fbx` | `Assets/Art/Production/Machines/OreHopper/OreHopper.fbx` | Ore Storage | 2,819 | 1,957 | Base + Normal, 1024 | Existing storage trigger/primitive collider only | Static hopper visual |
| `Meshy Import/Meshy_AI_assets_20261010_143203/01a11f97-02b7-77b9-a003-67553e7a3d3a/Meshy_AI_model.fbx` | `Assets/Art/Production/Machines/DrillBit/DrillBit.fbx` | Automated Drill bit | 2,228 | 1,983 | Base + Normal, 1024 | No collider on visual | Rotates only in `Producing` state |
| `Meshy Import/Meshy_AI_assets_20261010_143203/01a11f9c-89d1-71f1-89c8-f436c516cd57/Meshy_AI_model.fbx` | `Assets/Art/Production/Buildings/ToolWorkshop/ToolWorkshop.fbx` | Tool Workshop | 5,773 | 4,620 | Base + Normal, 1024 | Existing input/output triggers only | Static building visual |
| `Meshy Import/Meshy_AI_assets_20261010_143203/01a11fa0-dae9-73b3-a128-aed7b02d9b11/Meshy_AI_model.fbx` | `Assets/Art/Production/Buildings/IndustrialGate/IndustrialGate.fbx` | Industrial Gate | 7,288 | 4,667 | Base + Normal, 1024 | Existing gate blocker only | Child of authoritative blocker; hides on unlock |
| `Meshy Import/Meshy_AI_assets_20261010_143203/01a11fb0-5f5d-729e-9200-ee0e7be954ad/Meshy_AI_model.fbx` | `Assets/Art/Production/Buildings/Warehouse/Warehouse.fbx` | Central Warehouse | 8,010 | 4,676 | Base + Normal, 1024 | Existing transfer triggers only | Static building visual |
| `Meshy Import/Meshy_AI_assets_20261010_143203/01a11fb9-684d-7538-a1c7-61cdac316330/Meshy_AI_model.fbx` | `Assets/Art/Production/Buildings/AssemblyPlant/AssemblyPlant.fbx` | Assembly Plant | 4,539 | 2,697 | Base + Normal, 1024 | Existing input/output triggers only | Static building visual |
| `Meshy Import/Meshy_AI_assets_20261010_143217/01a11159-74a2-73d6-9376-09791372ba1f/Meshy_AI_model.fbx` | `Assets/Art/Production/Vehicles/Courier/Courier.fbx` | Courier | 5,763 | 5,168 | Base + Normal, 1024 | No vehicle physics; existing transform route remains authoritative | Static body on courier root |
| `Meshy Import/Meshy_AI_assets_20261010_143217/01a1115d-3906-7746-8f0b-1bbb0ce44658/Meshy_AI_model.fbx` | `Assets/Art/Production/Vehicles/CourierWheel/CourierWheel.fbx` | Courier wheels ×4 | 1,234 | 936 | Base + Normal, 1024 | No wheel physics | All four rotate only while courier moves |
| `Meshy Import/Meshy_AI_assets_20261010_143217/01a11bd7-a654-7123-bd85-09ebd0943271/Meshy_AI_model.fbx` | `Assets/Art/Production/Buildings/MineBarrier/MineBarrier.fbx` | Mine barrier | 1,982 | 1,427 | Base + Normal, 1024 | No production collider | Locked-state visual only |
| `Meshy Import/Meshy_AI_assets_20261010_143217/a0cc80d2-d40d-4bf7-9a72-61668b0e70ba/Meshy_AI_model.fbx` | `Assets/Art/Production/Buildings/MineEntrance/MineEntrance.fbx` | Mine entrance | 11,046 | 5,660 | Base, 1024 | No production collider | Static landmark |
| `Meshy Import/Meshy_AI_assets_20261010_143203/01a11bdd-bc4a-7206-b3e2-728b72cdaef2/Meshy_AI_model.fbx` | `Assets/Art/Production/Environment/MineRockA/MineRockA.fbx` | Mine rear rock left | 1,331 | 1,155 | Base + Normal, 1024 | Existing environment root only | Static, grounded visual |
| `Meshy Import/Meshy_AI_assets_20261010_143203/01a11f79-b8d8-70ce-94a8-c42dacde2a16/Meshy_AI_model.fbx` | `Assets/Art/Production/Environment/MineRockB/MineRockB.fbx` | Mine rear rock right | 1,481 | 1,347 | Base + Normal, 1024 | Existing environment root only | Static, grounded visual |
| `Meshy Import/Meshy_AI_iron_ingot_mobile_pro_1010143038_texture_fbx/Meshy_AI_iron_ingot_mobile_pro_1010143038_texture_fbx/Meshy_AI_iron_ingot_mobile_pro_1010143038_texture.fbx` | `Assets/Art/Production/Resources/IronBar/IronBar.fbx` | Iron Bar | 1,029 | 568 | Base, 512 | Carry visual has no collider | Single-renderer pooled variant |
| `Meshy Import/Meshy_AI_Mobile_Lowpoly_Wooden_1010143009_texture_fbx/Meshy_AI_Mobile_Lowpoly_Wooden_1010143009_texture.fbx` | `Assets/Art/Production/Resources/WoodCrate/WoodCrate.fbx` | Crate | 2,131 | 1,150 | Base, 512 | Carry visual has no collider | Single-renderer pooled variant |
| `Meshy Import/Meshy_AI_Mobile_Stylized_Toolb_1010143046_texture_fbx/Meshy_AI_Mobile_Stylized_Toolb_1010143046_texture_fbx/Meshy_AI_Mobile_Stylized_Toolb_1010143046_texture.fbx` | `Assets/Art/Production/Resources/Tool/Tool.fbx` | Tool | 2,952 | 1,621 | Base, 512 | Carry visual has no collider | Single-renderer pooled variant |
| `Meshy Import/Meshy_AI_Optimized_Iron_Ore_1010143030_texture_fbx/Meshy_AI_Optimized_Iron_Ore_1010143030_texture_fbx/Meshy_AI_Optimized_Iron_Ore_1010143030_texture.fbx` | `Assets/Art/Production/Resources/IronOre/IronOre.fbx` | Iron Ore | 2,020 | 1,256 | Base, 512 | Carry visual has no collider | Single-renderer pooled variant |
| `Meshy Import/Meshy_AI_Saw_Station_Optimized_1010143052_texture_fbx/Meshy_AI_Saw_Station_Optimized_1010143052_texture_fbx/Meshy_AI_Saw_Station_Optimized_1010143052_texture.fbx` | `Assets/Art/Production/Machines/Processor/Processor.fbx` | Wood Processor | 11,495 | 6,835 | Base, 1024 | Existing processor zones only | Static station body |
| `Meshy Import/Meshy_AI__1010143101_texture_fbx/Meshy_AI__1010143101_texture_fbx/Meshy_AI__1010143101_texture.fbx` | `Assets/Art/Production/Machines/SawBlade/SawBlade.fbx` | Processor saw blade | 1,410 | 984 | Base + Normal, 1024 | No collider on visual | Rotates only while Processor is active |

## Deferred and fallback assets

- Player and Lumber Worker: no character/rig export was present; accepted capsule visuals, controllers and colliders remain.
- Wood, Plank and Cash: no matching production export was present; readable portrait placeholders remain.
- Stockpile, Auto Feeder, Packing Station, Smelter and Market kiosk: no valid matching model was present. The visually inspected ambiguous exports were a saw blade and a courier wheel, so they were mapped correctly rather than forced onto unrelated entities.
- Two low-cost rock-cluster exports are now integrated at the mine. Two redundant high-cost exports (23–31 MB source FBX) remain deferred because they add no gameplay coverage and are materially heavier for Android.
- Industrial Props Pack remains deferred as allowed by the milestone.

## Material and import optimization

- URP/Lit opaque materials with explicit Base Map, optional Normal Map, scalar Metallic and Smoothness.
- No transparent surfaces, alpha clipping, embedded material import or duplicate exports.
- Model animation/cameras/lights disabled; Read/Write disabled; Medium mesh compression; mesh optimization enabled; imported normals and MikkTSpace tangents; no generated lightmap UVs.
- Texture Read/Write disabled; mipmaps and streaming enabled; Android ETC2 RGBA8 override; 1024 max for world assets and 512 for carried resources.
- Production resource variants retain exactly one renderer for the existing bounded pools.

## Layout and presentation adjustments

- Production art is uniformly normalized from imported bounds and grounded at visual anchors.
- Non-uniform parent scales are compensated for the saw blade, drill bit and courier wheel anchors.
- The Industrial Gate is parented to the authoritative gate blocker so it disappears when progression opens the route.
- Processor, Tool Workshop and Warehouse labels were raised slightly to clear the production silhouettes.
- The Mine entrance and locked barrier were fitted behind existing interaction zones; no route, zone, camera or gameplay transform moved.

## M22 performance comparison

| Metric | M22 baseline | M19 integrated | Delta |
|---|---:|---:|---:|
| Scene GameObjects | 611 | 626 | +15 |
| Scene renderers | 282 | 297 | +15 |
| Enabled renderers | 282 | 267 | -15 (placeholder renderers disabled) |
| Total vertices | 12,169 | 79,458 | +67,289 |
| Total triangles | 12,144 | 56,562 | +44,418 |
| Active triangles at fresh start | 1,764 | 6,419 | +4,655 |
| Distinct scene materials | 34 | 46 | +12 |
| Referenced texture memory (Editor estimate) | 132,000 B | 30,911,232 B | +30,779,232 B |
| 600-frame Editor PlayMode average | 19.21 ms | 16.63 ms | -2.58 ms |
| 600-frame maximum | 29.93 ms | 29.29 ms | -0.64 ms |
| Peak GC allocated in one sampled frame | 39,530 B | 113,850 B | +74,320 B |
| Managed memory growth across sample | 0 B | 0 B | unchanged |

Final visual-completion measurement: 83,470 vertices, 59,664 triangles, 6,539 active triangles at fresh start, 26,655,024 B referenced texture memory, 16.34 ms average / 28.82 ms maximum over 600 Editor PlayMode frames, and 0 B managed growth. Compared with the M19 integrated checkpoint, the two production rocks add only 120 active triangles while referenced texture memory is 4,256,208 B lower after the final import audit.

The GC peak is a single Editor/TestRunner sample and not a sustained per-frame allocation; the production animation paths allocate no managed objects in `Update`. Physical-device profiling remains required for definitive Android GPU/texture-memory figures.

## Validation evidence

- Generated scene validator: PASS, including all legacy M1–M22 invariants.
- EditMode/embedded PlayMode suite: 70/70 PASS.
- M19 PlayMode smoke: production resources/materials and active-only Saw/Courier/Drill animation PASS.
- M17 full progression smoke: fresh → Lumber → Mining → Tools → Logistics → Industrial → Machine Parts → Specialization → Market → Empire 25 PASS.
- M18 onboarding smoke: fresh/resume/no-replay/M17 handoff/input/marker cleanup PASS.
- M22 performance soak: 600 frames, 16.63 ms average, 29.29 ms max, 0 B managed growth; 10,000-cycle market/offline/reward soak PASS.
- Portrait 540×960 validation renders: Lumber, Mining, Logistics and Industrial views generated under `Logs/M19-scene-validation/` and visually inspected.
- Save schema: v9 unchanged.
