using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using IndustryTycoon.CameraSystem;
using IndustryTycoon.Persistence;
using IndustryTycoon.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IndustryTycoon.Editor
{
    /// <summary>Real PlayMode presenters with synthetic save fixtures; persistence is disabled before entry.</summary>
    [InitializeOnLoad]
    public static class ModernVisualCapture
    {
        private const string Key="IndustryTycoon.ModernCapture";
        private static int frames, shot;
        private static Camera camera;
        private static Canvas canvas;
        private static RenderTexture target;
        private static Texture2D pixels;
        private static string folder;
        private static readonly string[] Names={"01-early-lumber","02-developed-lumber","03-mining","04-logistics","05-industrial","06-empire","07-building","08-upgrades","09-market","10-goals","11-main-menu","12-shop","13-settings","14-tall-hud","15-tall-production","16-tall-shop","17-warehouse-world","18-locked-mine","19-locked-industry","20-pause","21-achievements","22-help","23-reset-dialog"};
        static ModernVisualCapture() {
            if(SessionState.GetBool(Key,false))EditorApplication.update+=Tick;
            EditorApplication.playModeStateChanged+=state=>{
                if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key+"Done",false)) {
                    SessionState.SetBool(Key+"Done",false);EditorApplication.delayCall+=()=>EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);
                }
            };
        }
        public static void Run() {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Prototype_LumberCamp.unity");
            UnityEngine.Object.FindAnyObjectByType<LocalPersistenceService>(FindObjectsInactive.Include).enabled=false;
            string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-visualIteration");
            SessionState.SetString(Key+"Folder","Logs/ModernVisual/"+(i>=0?args[i+1]:"iteration1"));
            SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Done",false);SessionState.SetBool(Key+"Failed",false);
            EditorApplication.update-=Tick;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
        }
        public static void RebuildAndRun() {
            LumberCampPrototypeBuilder.BuildFromCommandLine();
            VisualCompletionValidation.AuditFromCommandLine();
            Run();
        }
        private static void Tick() {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying)return;
            try {
                frames++;
                if(frames==30) Initialize();
                if(frames<40)return;
                if((frames-40)%16==0)SetupShot();
                if((frames-40)%16==15) {
                    Capture(Names[shot]);shot++;
                    if(shot==Names.Length){Audit();Stop(false);}
                }
            } catch(Exception ex) {Debug.LogException(ex);Stop(true);}
        }
        private static void Initialize() {
            folder=SessionState.GetString(Key+"Folder","");Directory.CreateDirectory(folder);
            camera=Camera.main;canvas=UnityEngine.Object.FindAnyObjectByType<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            UnityEngine.Object.FindAnyObjectByType<SafeAreaFitter>().enabled=false;
            Resize(960,false);
            ApplyFixture(false);
        }
        private static void Resize(int height,bool inset) {
            if(target!=null){camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);}
            target=new RenderTexture(540,height,24,RenderTextureFormat.ARGB32);target.Create();
            pixels=new Texture2D(540,height,TextureFormat.RGB24,false);camera.targetTexture=target;
            var safe=(RectTransform)canvas.transform.Find("Safe Area");
            SafeAreaFitter.CalculateAnchors(inset?new Rect(0,32,540,height-80):new Rect(0,0,540,height),new Vector2Int(540,height),out Vector2 min,out Vector2 max);
            safe.anchorMin=min;safe.anchorMax=max;safe.offsetMin=safe.offsetMax=Vector2.zero;
        }
        internal static void ApplyFixture(bool developed, bool lockedMine = false) {
            long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();var data=M9SaveData.CreateFresh(now);
            if(developed) {
                for(int i=0;i<data.purchasePads.Length;i++){data.purchasePads[i].completed=true;data.purchasePads[i].paidCash=M9PurchasePadIds.GetTotalCost(i);}
                for(int i=0;i<data.mining.purchasePads.Length;i++){data.mining.purchasePads[i].completed=true;data.mining.purchasePads[i].paidCash=M11MiningPurchasePadIds.GetTotalCost(i);}
                data.workshop.purchasePad.completed=true;data.workshop.purchasePad.paidCash=M12WorkshopPurchasePadIds.ToolWorkshopCost;
                data.lumberCampCompleted=true;data.mining.mineUnlocked=true;data.walletCash=28460;data.stockpileWood=18;data.processorInputWood=8;data.processorOutputPlanks=4;
                data.packingInputPlanks=8;data.packingOutputCrates=3;data.mining.smelterInputIronOre=6;data.mining.smelterOutputIronBars=4;data.mining.oreStorageIronOre=12;
                data.workshop.inputPlanks=6;data.workshop.inputIronBars=4;data.workshop.outputTools=2;
                foreach(var metric in data.progression.metrics)metric.value=10000;
                foreach(var flag in data.progression.flags)flag.value=true;
                data.progression.activeContractState=IndustryTycoon.Progression.ContractProgressState.CompletedUnclaimed;
                data.industrial.districtEntered=true;data.industrial.assemblyPlantPurchase.completed=true;data.industrial.assemblyPlantPurchase.paidCash=M14IndustrialTuning.AssemblyPlantUnlockCost;
                data.industrial.assemblyInputIronBars=6;data.industrial.assemblyInputTools=3;
                foreach(var upgrade in data.industrial.buildingUpgrades)upgrade.speedLevel=1;
                foreach(var factory in data.factoryScaling.factories){factory.specialization=IndustryTycoon.Processing.FactorySpecialization.Efficiency;factory.completedSpecializedCycles=30;}
                data.factoryScaling.optimizationObjectiveCompleted=true;data.factoryScaling.upgradedRouteUnitsTransported=100;
                M15FactoryRules.RecalculateDistrictLevel(data.factoryScaling);
            }
            if(lockedMine) data.mining.mineUnlocked=false;
            var persistence=UnityEngine.Object.FindAnyObjectByType<LocalPersistenceService>(FindObjectsInactive.Include);
            typeof(LocalPersistenceService).GetMethod("ApplyStableState",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(persistence,new object[]{data,now});
            if(developed) {
                var progression=UnityEngine.Object.FindAnyObjectByType<IndustryTycoon.Progression.LumberCampProgressionService>();
                progression.RestorePersistentState(data.progression);
                var industry=UnityEngine.Object.FindAnyObjectByType<IndustryTycoon.Progression.IndustrialDistrictService>();
                industry.RestorePersistentState(data.industrial);industry.RestoreFactoryScalingState(data.factoryScaling);
                progression.RecordMarketUnlocked();
                var market=UnityEngine.Object.FindAnyObjectByType<IndustryTycoon.Market.MarketPlanningService>();
                IndustryTycoon.Market.M16MarketRules.RotateDemand(market.State,market.BuildUnlockContext());
                IndustryTycoon.Market.M16MarketRules.RefillOrders(market.State,market.BuildUnlockContext());
            }
        }
        private static void SetupShot() {
            var nav=UnityEngine.Object.FindAnyObjectByType<MobilePanelNavigation>();nav.CloseAll();
            if(shot==1)ApplyFixture(true);
            if(shot==13)Resize(1200,true);
            if(shot==16)Resize(960,false);
            if(shot==17)ApplyFixture(false);
            Vector3 p=shot==0?new Vector3(0,0,-1):shot==1?new Vector3(-4,0,7):shot==2?new Vector3(12,0,25):shot==3?new Vector3(-19,0,19):shot==4?new Vector3(-7,0,35):new Vector3(-4,0,7);
            if(shot==13)p=new Vector3(0,0,-1);
            if(shot==16)p=new Vector3(-18,0,18);
            if(shot==17)p=new Vector3(13,0,22);
            if(shot==18)p=new Vector3(-7,0,34);
            var player=Find("Player");var controller=player.GetComponent<CharacterController>();controller.enabled=false;player.transform.position=p;controller.enabled=true;
            camera.GetComponent<SmoothFollowCamera>().SnapToTarget();
            if(shot==5) {
                // Explicit overview evidence; normal gameplay keeps the actual follow camera.
                camera.GetComponent<SmoothFollowCamera>().enabled=false;
                camera.transform.position=new Vector3(0,65,-30);camera.transform.LookAt(new Vector3(0,0,15));
            } else camera.GetComponent<SmoothFollowCamera>().enabled=true;
            if(shot==6||shot==7||shot==14) {nav.ToggleContent(3);UnityEngine.Object.FindAnyObjectByType<ProductionOverviewView>().SelectBuilding(shot==7?3:0);}
            if(shot==8)nav.ToggleContent(4);
            if(shot==9)nav.ToggleContent(0);
            if(shot==10)typeof(MobilePanelNavigation).GetMethod("SetExclusive",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(nav,new object[]{Find("Main Menu")});
            if(shot==11||shot==15)nav.ToggleContent(5);
            if(shot==12)typeof(MobilePanelNavigation).GetMethod("SetExclusive",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(nav,new object[]{Find("Settings Panel")});
            if(shot==3)nav.ToggleContent(2);
            if(shot==19)nav.OpenPause();
            if(shot==20)nav.ToggleContent(1);
            if(shot==21)nav.OpenHelp();
            if(shot==22) {
                typeof(MobilePanelNavigation).GetMethod("SetExclusive",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(nav,new object[]{Find("Main Menu")});
                Find("Reset Save Confirmation").SetActive(true);
            }
            // Suppress only transient reward toasts after synthetic restoration, not persistent HUD.
            foreach(string n in new[]{"Welcome Back Overlay","Major Unlock Banner","Achievement Toast","Completion Banner"})Find(n)?.SetActive(false);
            Canvas.ForceUpdateCanvases();
        }
        private static void Capture(string name) {
            if(name=="13-settings") {
                foreach(Slider slider in canvas.GetComponentsInChildren<Slider>()) {
                    var pointer=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                    pointer.position=RectTransformUtility.WorldToScreenPoint(camera,slider.transform.TransformPoint(((RectTransform)slider.transform).rect.center));
                    var hits=new List<UnityEngine.EventSystems.RaycastResult>();canvas.GetComponent<GraphicRaycaster>().Raycast(pointer,hits);
                    if(hits.Count==0||hits[0].gameObject!=slider.gameObject)throw new InvalidOperationException("Slider hit surface is obstructed: "+slider.name);
                }
            }
            if(name=="09-market") {
                var market=UnityEngine.Object.FindAnyObjectByType<IndustryTycoon.Market.MarketPlanningService>();
                if(!market.IsMarketUnlocked||Find("Demand Summary").GetComponent<Text>().text.Contains("UNLOCKS"))
                    throw new InvalidOperationException("Developed market panel failed to refresh its authoritative unlocked state.");
            }
            Canvas.ForceUpdateCanvases();camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
            pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();RenderTexture.active=previous;
            File.WriteAllBytes(folder+"/"+name+".png",pixels.EncodeToPNG());
            if(!canvas.enabled)return; // Isolated art diagnostics have no UI or touch targets.
            var rows=new List<string>();
            foreach(Button b in canvas.GetComponentsInChildren<Button>()) {
                Vector3[] corners=new Vector3[4];((RectTransform)b.transform).GetWorldCorners(corners);
                Vector2 lo=RectTransformUtility.WorldToScreenPoint(camera,corners[0]),hi=RectTransformUtility.WorldToScreenPoint(camera,corners[2]);
                if(Mathf.Min((hi-lo).x,(hi-lo).y)<63.5f)throw new InvalidOperationException("Undersized portrait target: "+b.name);
                foreach(Text label in b.GetComponentsInChildren<Text>()) {
                    label.rectTransform.GetWorldCorners(corners);
                    Rect rect=((RectTransform)b.transform).rect;
                    foreach(Vector3 corner in corners) {
                        Vector3 local=b.transform.InverseTransformPoint(corner);
                        if(local.x<rect.xMin-2||local.x>rect.xMax+2||local.y<rect.yMin-2||local.y>rect.yMax+2)
                            throw new InvalidOperationException("Button label escapes its target: "+b.name+"/"+label.name);
                    }
                }
                rows.Add($"{b.name}|rect={lo}..{hi}|size={hi-lo}|interactable={b.interactable}");
            }
            File.WriteAllLines(folder+"/"+name+"-targets.txt",rows);
        }
        private static void Audit() {
            var rows=new List<string>{"Runtime visual audit; schema="+M9SaveSchema.CurrentVersion};
            GameObject[] objects=Objects().ToArray();int missing=objects.Sum(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount);
            if(missing!=0)throw new InvalidOperationException("Missing scripts "+missing);
            rows.Add("missing-scripts=0");
            foreach(Renderer r in objects.SelectMany(g=>g.GetComponents<Renderer>())) {
                if(r.sharedMaterials.Any(m=>m==null||m.shader==null||m.shader.name=="Hidden/InternalErrorShader"))throw new InvalidOperationException("Broken material: "+r.name);
                if(r.name.Contains("Production Visual"))rows.Add($"{r.name}|enabled={r.enabled}|active={r.gameObject.activeInHierarchy}|bounds={r.bounds}|rotation={r.transform.eulerAngles}|scale={r.transform.lossyScale}");
            }
            rows.Add("broken-materials=0");rows.Add("renderer-count="+objects.Sum(g=>g.GetComponents<Renderer>().Length));
            File.WriteAllLines(folder+"/runtime-audit.txt",rows);
            ApplyFixture(true);
            ModernVisualVerification.RuntimeRoutes(folder);
            CaptureProductionDetails();
            Debug.Log("MODERN VISUAL CAPTURE PASS: "+Names.Length+" screens, runtime materials/scripts, controller routes and isolated production detail captures.");
        }
        private static void CaptureProductionDetails() {
            canvas.enabled=false;camera.GetComponent<SmoothFollowCamera>().enabled=false;
            camera.fieldOfView=32;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.76f,.81f,.80f);
            int mask=camera.cullingMask;camera.cullingMask=1<<31;
            var names=Objects().Where(g=>g.name.Contains("Production Visual")).Select(g=>g.name)
                .Concat(new[]{"Crate Visual","Iron Ore Visual","Iron Bar Visual","Tool Visual"}).Distinct().ToArray();
            string previousFolder=folder;folder+="/ProductionDetails";Directory.CreateDirectory(folder);
            foreach(string name in names) {
                GameObject root=Find(name);
                if(root==null)continue;
                var renderers=root.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)continue;
                var ancestors=new List<GameObject>();for(Transform t=root.transform;t!=null;t=t.parent)ancestors.Add(t.gameObject);
                var states=ancestors.Select(g=>g.activeSelf).ToArray();
                var layers=root.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.gameObject,g=>g.gameObject.layer);
                var enabled=renderers.Select(r=>r.enabled).ToArray();
                for(int i=ancestors.Count-1;i>=0;i--)ancestors[i].SetActive(true);
                foreach(var entry in layers)entry.Key.layer=31;
                foreach(Renderer r in renderers)r.enabled=true;
                Bounds bounds=renderers[0].bounds;foreach(Renderer r in renderers)bounds.Encapsulate(r.bounds);
                float radius=Mathf.Max(.2f,bounds.extents.magnitude);
                float halfAngle=Mathf.Atan(Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)*Mathf.Min(1,camera.aspect));
                camera.transform.position=bounds.center+new Vector3(1.7f,1.4f,-3.1f).normalized*(radius/Mathf.Sin(halfAngle)*1.12f);camera.transform.LookAt(bounds.center);
                Capture(name.Replace(' ','-'));
                foreach(var entry in layers)entry.Key.layer=entry.Value;
                for(int i=0;i<renderers.Length;i++)renderers[i].enabled=enabled[i];
                for(int i=0;i<ancestors.Count;i++)ancestors[i].SetActive(states[i]);
            }
            folder=previousFolder;camera.cullingMask=mask;
        }
        private static IEnumerable<GameObject> Objects()=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject);
        private static GameObject Find(string name)=>Objects().FirstOrDefault(g=>g.name==name);
        private static void Stop(bool failed) {
            SessionState.SetBool(Key,false);SessionState.SetBool(Key+"Done",true);SessionState.SetBool(Key+"Failed",failed);
            EditorApplication.update-=Tick;EditorApplication.ExitPlaymode();
        }
    }
}
