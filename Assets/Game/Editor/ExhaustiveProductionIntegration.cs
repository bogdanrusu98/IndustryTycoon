using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IndustryTycoon.Editor
{
    /// <summary>Art-only corrections found by the independent exported-asset census.</summary>
    public static class ExhaustiveProductionIntegration
    {
        public static void Apply(Scene scene)
        {
            Transform[] all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            Transform Find(string name) => all.First(t => t.name == name);
            void Hide(string name) => Find(name).GetComponent<Renderer>().enabled = false;

            // Both cutters are governed by the original WoodProductionUpgrade/UnlockFeedback roots.
            // Keep those roots, their unlock animation and references; replace only their art.
            foreach (string name in new[] { "Saw One", "Saw Two (Unlock)" })
            {
                Transform cutter = Find(name);
                Vector3 floor = cutter.position; floor.y = 0;
                GameObject shell = M19ProductionSceneIntegrator.AttachModel(cutter,
                    "Machines/Processor/Processor", 1.45f, cutter.InverseTransformPoint(floor),
                    new Vector3(0,180,0), true);
                shell.name = name + " Station Production Visual";
                foreach (string old in new[] { "Stand", "Blade", "Hub" }) cutter.Find(old).GetComponent<Renderer>().enabled = false;
                Transform blade = cutter.Find("Blade");
                blade.localPosition = new Vector3(-.40f,.22f,-.02f);
                GameObject disc = M19ProductionSceneIntegrator.AttachModel(blade,
                    "Machines/SawBlade/SawBlade", .45f, Vector3.zero, new Vector3(90,0,0), false, true);
                disc.name = name + " Blade Production Visual";
            }
            Hide("Station Platform");

            Transform vein = Find("Iron Vein");
            GameObject ore = M19ProductionSceneIntegrator.AttachModel(vein,
                "Resources/IronOre/IronOre", 2.25f, new Vector3(0,.14f,.15f), Vector3.zero, true);
            ore.name = "Iron Vein Production Visual";
            Hide("Ore Deposit Rock"); Hide("Exposed Iron Ore");

            // The existing feedback owns this anchor's activation and fill scale. Its child inherits both.
            Transform stored = Find("Stored Ore");
            Vector3 fillCenter = stored.parent.TransformPoint(new Vector3(0,1.35f,.20f));
            GameObject fill = M19ProductionSceneIntegrator.AttachModel(stored,
                "Resources/IronOre/IronOre", 1.25f, stored.InverseTransformPoint(fillCenter), Vector3.zero, false, true);
            fill.name = "Stored Ore Production Visual";
            Hide("Stored Ore");

            // Obsolete teaser primitives overlap the real gated district and the purchased plant.
            // The authoritative industrial gate already supplies the correct locked-state model.
            foreach (string name in new[] { "Industrial Skyline", "Industrial Lock Left", "Industrial Lock Right", "Industrial Lock Barrier", "Industrial District Platform" }) Hide(name);

            // Mount the disc at the conveyor throat, outside the opaque canopy.
            Find("Working Blade").localPosition = new Vector3(-.92f,1.15f,-.05f);

            // Move only the parcel VisualRoot onto the exposed conveyor instead of inside its canopy.
            Transform parcel = Find("Packing Parcel Production Visual").parent.parent;
            parcel.localPosition += new Vector3(-1.80f,.15f,.10f);

            // The body is exported without wheels. Align the four independent wheels to its fenders.
            Transform body = Find("Courier Production Visual").parent.parent;
            body.localPosition += Vector3.up * .15f;
            Find("Carried Crate Anchor").localPosition = new Vector3(0,1.24f,-.52f);
            foreach (string name in new[] { "Front Left Wheel", "Front Right Wheel", "Rear Left Wheel", "Rear Right Wheel" })
            {
                Transform wheel = Find(name);
                wheel.localPosition = new Vector3(name.Contains("Left") ? -.54f : .54f, .44f,
                    name.StartsWith("Front") ? .58f : -.62f);
            }

            // These six shells stand on decorative pads with top Y=.175, not on the ground below them.
            foreach (string name in new[] { "Processor Production Visual", "ToolWorkshop Production Visual", "Warehouse Production Visual", "AssemblyPlant Production Visual", "Recovered Smelter Production Visual", "Recovered Packing Production Visual" })
                Find(name).parent.parent.position += Vector3.up * .175f;
        }
    }
}
