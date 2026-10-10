# Visual Completion Asset Coverage

Final audit target: Unity 6000.5.9f1, save schema v9. `Meshy Import/` remains untracked source staging; production copies are under `Assets/Art/Production/`.

| Gameplay entity | Current visual | Available source | Integrated visual | Final status / fallback reason |
|---|---|---|---|---|
| Player | Capsule character | None | Existing gameplay visual | Fallback — no rigged character export |
| Lumber Worker | Capsule character | None | Existing gameplay visual | Fallback — no rigged character export |
| Loose Wood | Low-poly log | None | Existing gameplay visual | Fallback — no matching Wood export |
| Plank | Readable plank | None | Existing gameplay visual | Fallback — no matching Plank export |
| Cash | Cash bundle | None | Existing gameplay visual | Fallback — no matching Cash export |
| Crate | Carry primitive | Mobile Lowpoly Wooden Crate | `Resources/WoodCrate` | Production |
| Iron Ore | Carry primitive | Optimized Iron Ore | `Resources/IronOre` | Production |
| Iron Bar | Carry primitive | Iron Ingot Mobile | `Resources/IronBar` | Production |
| Tool | Carry primitive | Mobile Stylized Toolbox | `Resources/Tool` | Production |
| Wood Processor | Primitive station | Saw Station Optimized | `Machines/Processor` | Production |
| Processor Saw | Primitive blade | Saw Blade | `Machines/SawBlade` | Production; active-only animation retained |
| Tool Workshop | Primitive workshop | Ironforge Tool Workshop | `Buildings/ToolWorkshop` | Production |
| Central Warehouse | Primitive warehouse | Central Warehouse Assembly | `Buildings/Warehouse` | Production |
| Assembly Plant | Primitive plant | Industrial Production Plant | `Buildings/AssemblyPlant` | Production |
| Industrial Gate | Primitive gate | Industrial District Lockdown | `Buildings/IndustrialGate` | Production |
| Mine Barrier | Primitive barrier | Industrial Lockdown Barrier | `Buildings/MineBarrier` | Production |
| Mine Entrance | Primitive entrance | Lowpoly Mine Entrance | `Buildings/MineEntrance` | Production |
| Mine Rock Left | Primitive rock | `01a11bdd-bc4a-7206-b3e2-728b72cdaef2` rock cluster | `Environment/MineRockA` | Production; 1,331 vertices / 1,155 triangles |
| Mine Rock Right | Primitive rock | `01a11f79-b8d8-70ce-94a8-c42dacde2a16` rugged rock | `Environment/MineRockB` | Production; 1,481 vertices / 1,347 triangles |
| Ore Storage | Primitive bin | Teal Industrial Hopper | `Machines/OreHopper` | Production |
| Automated Drill | Primitive bit | Titan Bore Drill Bit | `Machines/DrillBit` | Production; active-only animation retained |
| Courier | Primitive vehicle | Teal Cargo Cruiser | `Vehicles/Courier` | Production |
| Courier Wheels | Primitive wheels | Dedicated wheel export | `Vehicles/CourierWheel` ×4 | Production; route authority unchanged |
| Wood Stockpile | Curated primitive staging | None | Existing visual | Fallback — no matching Stockpile export |
| Auto Feeder | Curated primitive machine | None | Existing visual | Fallback — no matching Auto Feeder export |
| Packing Station | Curated primitive machine | None | Existing visual | Fallback — no matching Packing Station export |
| Smelter | Curated primitive furnace | Ambiguous saw-blade export | Existing visual | Fallback — source is not a Smelter |
| Market Kiosk | Curated primitive kiosk | Ambiguous courier-wheel export | Existing visual | Fallback — source is not a kiosk |

## Runtime visibility result

- 18 production models, 30 production textures and 18 URP/Lit production materials audited.
- 17 scene production renderers plus four carried-resource prefab variants verified with valid hierarchy, finite scale/position, visible layers, valid materials and Base Maps.
- MineRockA and MineRockB, previously deferred in M19, are now grounded and visible at their progression-gated mine anchors.
- Obsolete renderers are disabled under replaced production entities. Gameplay roots, colliders, triggers, IDs, scripts and interaction zones are unchanged.
- Zero missing scripts, pink/error materials or missing production Base Maps.

## Source assets intentionally not integrated

- The two remaining rock exports (`1f9936f8-76be-44f2-b9d7-3607ec08df42` and `29d69076-c3f4-41a0-ba2f-d92a3875a948`) duplicate the mine-rock role at substantially higher source cost (23–31 MB FBX plus large maps). The two integrated variants cover the role at 2,812 combined vertices and 2,502 triangles.
- Duplicate WoodCrate source path was not copied twice.
- Industrial Props Pack remains outside the gameplay-entity coverage and was not added as decorative draw-call cost.

## Evidence

- Machine-readable audit: `Logs/VisualCompletion/visual-audit.txt`.
- Portrait captures: `Logs/VisualCompletion/Screenshots/` (Lumber fresh/developed, Mining, Warehouse/logistics, Industrial, Market, building panel, goals/contracts, main menu, shop and settings).
