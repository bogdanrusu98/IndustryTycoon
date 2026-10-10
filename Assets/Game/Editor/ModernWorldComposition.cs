using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IndustryTycoon.Editor
{
    public static class ModernWorldComposition
    {
        public static readonly Vector3 MineShift = new Vector3(6,0,6);
        public static readonly Vector3 LogisticsShift = new Vector3(-8,0,2);
        public static readonly Vector3 IndustryShift = new Vector3(-2,0,14);
        public static readonly Vector3 GroundPosition = new Vector3(0,-.15f,15);
        public static readonly Vector3 GroundSize = new Vector3(60,.3f,66);
        public static readonly Vector3 CameraOffset = new Vector3(0,21,-16);
        private static Scene scene;
        private static Transform decor;

        public static void Apply(Scene target)
        {
            scene=target;
            var before=scene.GetRootGameObjects().ToDictionary(g=>g.name,g=>g.transform.position);
            Move("Mine Teaser",MineShift);
            Move("Central Warehouse",LogisticsShift);
            foreach(string name in new[]{"Industrial District M14","Industrial District Tease","Assembly Plant","Assembly Plant Purchase"}) Move(name,IndustryShift);
            // Markers follow their actual destinations; gameplay roots stay independent.
            foreach(string name in new[]{"Mining Area","MineEntrance Guidance Marker","Smelter Guidance Marker"}) Move(name,MineShift);
            foreach(string name in new[]{"Central Logistics - Warehouse Area","Warehouse Guidance Marker"}) Move(name,LogisticsShift);
            foreach(string name in new[]{"Industrial District","IndustrialEntrance Guidance Marker","AssemblyPlant Guidance Marker","Market Guidance Marker"}) Move(name,IndustryShift);
            GameObject ground=Find("Ground"); ground.transform.position=GroundPosition; ground.transform.localScale=GroundSize;
            SetBoundary("West",new Vector3(-30.5f,1.5f,15),new Vector3(1,3,66));
            SetBoundary("East",new Vector3(30.5f,1.5f,15),new Vector3(1,3,66));
            SetBoundary("South",new Vector3(0,1.5f,-18.5f),new Vector3(60,3,1));
            SetBoundary("North",new Vector3(0,1.5f,48.5f),new Vector3(60,3,1));
            Material grass=Material("Meadow",new Color(.49f,.61f,.43f));
            Material sand=Material("Limestone",new Color(.75f,.72f,.60f));
            Material road=Material("Avenue",new Color(.64f,.66f,.58f));
            Material edge=Material("Edge",new Color(.82f,.82f,.70f));
            Material mine=Material("Slate",new Color(.49f,.55f,.56f));
            Material clay=Material("Clay",new Color(.64f,.58f,.47f));
            ground.GetComponent<Renderer>().sharedMaterial=grass;
            ReplaceMaterial("Area_Lumber",grass); ReplaceMaterial("Area_Mining",mine);
            ReplaceMaterial("Area_Logistics",sand); ReplaceMaterial("Area_Industrial",clay);
            ReplaceMaterial("Packing_Station",sand); ReplaceMaterial("Wood_Stockpile",sand);
            ReplaceMaterial("Processor",Material("Conveyor",new Color(.29f,.36f,.33f)));
            ReplaceMaterial("Purchase_Complete",Material("Completed",new Color(.20f,.49f,.38f)));
            ReplaceMaterial("Purchase_Pad",Material("Purchase",new Color(.86f,.63f,.25f)));
            ReplaceMaterial("Sale_Point",Material("Trading",new Color(.25f,.48f,.38f)));
            ReplaceMaterial("World_Label_Backdrop",Material("Label Ink",new Color(.08f,.15f,.17f)));
            decor=new GameObject("Modern District Composition").transform;
            SceneManager.MoveGameObjectToScene(decor.gameObject,scene);
            Cube("Distant meadow",new Vector3(0,-.4f,15),new Vector3(140,.2f,140),grass);
            // Broad avenues join the front of each district, leaving working yards free.
            Path(new Vector3(0,0,-10),new Vector3(0,0,20),3.2f,road,edge);
            Path(new Vector3(0,0,16),new Vector3(-20,0,16),3.2f,road,edge);
            Path(new Vector3(0,0,20),new Vector3(13.2f,0,20),3.2f,road,edge);
            Path(new Vector3(13.2f,0,20),new Vector3(13.2f,0,25),3.2f,road,edge);
            Path(new Vector3(0,0,20),new Vector3(-7.5f,0,20),3.2f,road,edge);
            Path(new Vector3(-7.5f,0,20),new Vector3(-7.5f,0,34),3.2f,road,edge);
            foreach(string name in new[]{"Wood Processor","Packing Station","Tool Workshop","Central Warehouse","Assembly Plant","Iron Smelter"})
            {
                Transform t=Find(name)?.transform; if(t==null) continue;
                Vector3 p=t.position; p.y=.13f;
                Cube(name+" footprint",p,new Vector3(name=="Central Warehouse"?7:5.8f,.09f,5.2f),sand);
                Cube(name+" entry stripe",p+new Vector3(0,.06f,-2.5f),new Vector3(3,.025f,.10f),edge);
            }
            // Retain gameplay state on path root; replace its old, now disconnected renderers.
            GameObject oldPath=Find("Mine Access Path");
            if(oldPath!=null) foreach(Renderer r in oldPath.GetComponentsInChildren<Renderer>(true)) r.enabled=false;
            RecoverVisuals();
            foreach(Transform t in Find("Auto Feeder Visual").GetComponentsInChildren<Transform>(true))
            {
                if(t.name.Contains("Rail")) t.localScale=new Vector3(t.localScale.x,.09f,t.localScale.z);
                if(t.name.StartsWith("Belt Slat")) t.localScale=new Vector3(.68f,.07f,.66f);
            }
            foreach(Transform t in Objects().Select(g=>g.transform))
                if(t.name.StartsWith("Upgrade Plus"))t.localScale*=.65f;
            CreateWorkerVisual(Find("Capsule Placeholder").transform, true);
            CreateWorkerVisual(Find("Worker Capsule").transform, false);
            Find("Facing Marker").GetComponent<Renderer>().enabled=false;
            Find("Worker Facing Marker").GetComponent<Renderer>().enabled=false;
            var follow=UnityEngine.Object.FindAnyObjectByType<IndustryTycoon.CameraSystem.SmoothFollowCamera>();
            var cameraSettings=new SerializedObject(follow);
            cameraSettings.FindProperty("offset").vector3Value=CameraOffset;
            cameraSettings.ApplyModifiedPropertiesWithoutUndo();follow.SnapToTarget();
            // Boundary rock silhouettes use the inexpensive reviewed sources, not high-density duplicates.
            for(int i=0;i<6;i++)
            {
                var anchor=new GameObject("Mining escarpment "+i); anchor.transform.SetParent(decor,false);
                anchor.transform.position=new Vector3(7+i*3.2f,0,36+(i%2)*1.2f);
                var model=M19ProductionSceneIntegrator.AttachModel(anchor.transform,"Environment/MineRockB/MineRockB",3.3f,Vector3.zero,new Vector3(0,i*41,0),true);
                model.name="Escarpment Model "+i;
            }
            Material leaves=Material("Pine",new Color(.22f,.39f,.27f));
            Material trunk=Material("Pine Trunk",new Color(.37f,.29f,.21f));
            // Established edge rhythm, never across the early player/worker loop.
            for(int i=0;i<14;i++)
            {
                Vector3 p=i<7?new Vector3(12+(i%2)*2,0,-7+i*4):new Vector3(-14-(i%2)*2,0,-8+(i-7)*4);
                Cube("Pine trunk "+i,p+Vector3.up*.55f,new Vector3(.3f,1.1f,.3f),trunk);
                var crown=GameObject.CreatePrimitive(PrimitiveType.Sphere); crown.name="Pine crown "+i; crown.transform.SetParent(decor,false);
                crown.transform.position=p+Vector3.up*1.8f;crown.transform.localScale=new Vector3(1.8f,2.5f,1.8f);crown.GetComponent<Renderer>().sharedMaterial=leaves;
                UnityEngine.Object.DestroyImmediate(crown.GetComponent<Collider>());crown.isStatic=true;
            }
            foreach(TextMesh t in Objects().SelectMany(g=>g.GetComponents<TextMesh>()))
            {
                t.fontSize=64;
                // Small labels are local context, never floating text across the whole district.
                t.characterSize=Mathf.Max(t.characterSize,.035f);
            }
            var labels=decor.gameObject.AddComponent<IndustryTycoon.UI.WorldLabelProximity>();
            labels.Configure(Find("Player").transform,Objects().SelectMany(g=>g.GetComponents<TextMesh>()).ToArray());
            Directory.CreateDirectory("Logs/ModernVisual");
            File.WriteAllLines("Logs/ModernVisual/layout-delta.txt",scene.GetRootGameObjects().Where(g=>before.ContainsKey(g.name)&&before[g.name]!=g.transform.position).Select(g=>$"{g.name}|{before[g.name]} -> {g.transform.position}|translation={Vector3.Distance(before[g.name],g.transform.position):F2}m|straight-line travel-change bound at 5.5m/s={Vector3.Distance(before[g.name],g.transform.position)/5.5f:F2}s"));
        }

        private static void CreateWorkerVisual(Transform anchor,bool player)
        {
            anchor.GetComponent<Renderer>().enabled=false;
            var root=new GameObject("Crafted Worker VisualRoot");root.transform.SetParent(anchor,false);
            root.transform.localScale=new Vector3(1/anchor.localScale.x,1/anchor.localScale.y,1/anchor.localScale.z);
            root.transform.position=anchor.parent.position;
            Material suit=Material(player?"Foreman Uniform":"Worker Uniform",player?new Color(.12f,.29f,.33f):new Color(.35f,.42f,.27f));
            Material skin=Material("Worker Skin",new Color(.76f,.48f,.29f));
            Material hat=Material("Safety Helmet",new Color(.98f,.70f,.22f));
            Material boots=Material("Work Boots",new Color(.16f,.20f,.21f));
            Part(root.transform,"Work jacket",PrimitiveType.Cube,new Vector3(0,.93f,0),new Vector3(.64f,.7f,.4f),suit);
            Part(root.transform,"Head",PrimitiveType.Sphere,new Vector3(0,1.48f,0),new Vector3(.46f,.5f,.43f),skin);
            Part(root.transform,"Hard hat",PrimitiveType.Sphere,new Vector3(0,1.70f,0),new Vector3(.56f,.32f,.55f),hat);
            Part(root.transform,"Hat brim",PrimitiveType.Cylinder,new Vector3(0,1.61f,.05f),new Vector3(.68f,.045f,.66f),hat);
            for(int i=-1;i<=1;i+=2) {
                Part(root.transform,"Boot "+i,PrimitiveType.Cube,new Vector3(i*.19f,.25f,.05f),new Vector3(.24f,.5f,.39f),boots);
                Part(root.transform,"Sleeve "+i,PrimitiveType.Capsule,new Vector3(i*.4f,.95f,0),new Vector3(.22f,.25f,.24f),suit);
                Part(root.transform,"Glove "+i,PrimitiveType.Sphere,new Vector3(i*.4f,.66f,0),new Vector3(.23f,.23f,.25f),hat);
                Part(root.transform,"Safety strap "+i,PrimitiveType.Cube,new Vector3(i*.16f,.97f,.21f),new Vector3(.07f,.52f,.015f),hat);
            }
        }
        private static void Part(Transform parent,string name,PrimitiveType type,Vector3 p,Vector3 scale,Material mat) {
            var g=GameObject.CreatePrimitive(type);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=p;g.transform.localScale=scale;
            UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());g.GetComponent<Renderer>().sharedMaterial=mat;
        }

        private static void RecoverVisuals()
        {
            // The audited Ironforge asset is a furnace building; it is a suitable smelter shell as well.
            Transform smelter=Find("Iron Smelter").transform;
            foreach(string name in new[]{"Smelter Platform","Furnace","Smelter Chimney"}) Find(name).GetComponent<Renderer>().enabled=false;
            var furnace=M19ProductionSceneIntegrator.AttachModel(smelter,"Buildings/ToolWorkshop/ToolWorkshop",3.7f,new Vector3(0,0,.35f),new Vector3(0,180,0),true);
            furnace.name="Recovered Smelter Production Visual";
            // Use the reviewed saw-table/conveyor shell for packing; its blade is a separate export.
            Transform packing=Find("Packing Workshop Visual").transform;
            foreach(Renderer r in packing.GetComponentsInChildren<Renderer>(true))
                if(r.GetComponent<TextMesh>()==null && !r.name.Contains("Output") && !r.name.Contains("Crate")) r.enabled=false;
            var table=M19ProductionSceneIntegrator.AttachModel(packing,"Machines/Processor/Processor",3.5f,new Vector3(0,0,.2f),new Vector3(0,180,0),true);
            table.name="Recovered Packing Production Visual";
            // Production crate geometry replaces the packing's decorative box shell.
            var parcel=M19ProductionSceneIntegrator.AttachModel(packing,"Resources/WoodCrate/WoodCrate",.7f,new Vector3(.7f,.9f,0),Vector3.zero,false);
            parcel.name="Packing Parcel Production Visual";
        }
        private static void Move(string name,Vector3 delta) { GameObject g=Find(name); if(g!=null)g.transform.position+=delta; }
        private static GameObject Find(string name)=>Objects().FirstOrDefault(g=>g.name==name);
        private static IEnumerable<GameObject> Objects()=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject);
        private static void SetBoundary(string name,Vector3 p,Vector3 size) { Transform t=Find("Walkable Bounds").transform.Find(name);t.position=p;t.GetComponent<BoxCollider>().size=size; }
        private static Material Material(string name,Color color) {
            const string folder="Assets/Game/Prefabs/Materials";
            string path=folder+"/Modern_"+name.Replace(" ","_")+".mat";
            Material m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
            m.color=color;m.SetFloat("_Smoothness",.12f);EditorUtility.SetDirty(m);return m;
        }
        private static void ReplaceMaterial(string name,Material replacement) {
            string originalPath="Assets/Game/Prefabs/Materials/"+name+".mat";
            foreach(Renderer r in Objects().SelectMany(g=>g.GetComponents<Renderer>()))
                if(r.sharedMaterial!=null && AssetDatabase.GetAssetPath(r.sharedMaterial)==originalPath)r.sharedMaterial=replacement;
            // State presenters reassign materials after loading; update their art references too.
            foreach(MonoBehaviour behaviour in Objects().SelectMany(g=>g.GetComponents<MonoBehaviour>())) {
                if(behaviour==null)continue;
                var serialized=new SerializedObject(behaviour);var property=serialized.GetIterator();
                while(property.Next(true))
                    if(property.propertyType==SerializedPropertyType.ObjectReference && property.objectReferenceValue is Material material && AssetDatabase.GetAssetPath(material)==originalPath)
                        property.objectReferenceValue=replacement;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        private static void Cube(string name,Vector3 p,Vector3 scale,Material mat) {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(decor,false);g.transform.position=p;g.transform.localScale=scale;
            g.GetComponent<Renderer>().sharedMaterial=mat;UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());g.isStatic=true;
        }
        private static void Path(Vector3 a,Vector3 b,float width,Material mat,Material edge) {
            Vector3 p=(a+b)*.5f;p.y=.085f;bool horizontal=Mathf.Abs(a.x-b.x)>Mathf.Abs(a.z-b.z);
            float length=Vector3.Distance(a,b)+width;
            Cube("Avenue border",p,new Vector3(horizontal?length:width+.24f,.06f,horizontal?width+.24f:length),edge);
            p.y+=.04f;Cube("Avenue",p,new Vector3(horizontal?length:width,.05f,horizontal?width:length),mat);
        }
    }
}
