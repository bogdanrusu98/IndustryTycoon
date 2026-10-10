using System;
using System.Linq;
using IndustryTycoon.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IndustryTycoon.Editor
{
    /// <summary>Presentation-only final pass on the deterministic M20 scene builder.</summary>
    public static class ModernPresentationBuilder
    {
        public static readonly Color Ink = new Color(.10f,.19f,.23f);
        public static readonly Color Paper = new Color(.96f,.95f,.89f);
        public static readonly Color Soft = new Color(.86f,.90f,.86f);
        public static readonly Color Green = new Color(.12f,.39f,.32f);
        private static Sprite round;
        private static Transform safe;

        public static void Apply(Scene scene)
        {
            safe = scene.GetRootGameObjects().First(g => g.name == "HUD").transform.Find("Safe Area");
            round = Find("Production Panel").GetComponent<Image>().sprite;
            foreach (Text t in safe.GetComponentsInChildren<Text>(true))
            {
                t.color = Ink;
                t.fontSize = Mathf.Max(t.fontSize, 28);
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = 26;
                t.resizeTextMaxSize = t.fontSize;
                t.raycastTarget = false;
            }
            foreach (Image img in safe.GetComponentsInChildren<Image>(true))
            {
                if (img.GetComponent<Button>() != null) continue;
                if (img.name.Contains("Fill") || img.name == "Checkmark") continue;
                img.color = img.name.Contains("Overlay") ? new Color(.04f,.09f,.11f,.72f) : Paper;
            }
            foreach (Button b in safe.GetComponentsInChildren<Button>(true))
                ButtonStyle(b, Soft, Ink);
            Hud();
            Navigation();
            Production();
            Market();
            Logistics();
            Goals();
            Shop();
            Menus();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(
                Find("Market Orders Panel").gameObject.AddComponent<PanelShown>().shown,
                UnityEngine.Object.FindAnyObjectByType<MarketPlanningView>().Refresh);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(
                Find("Production Panel").gameObject.AddComponent<PanelShown>().shown,
                safe.GetComponent<ProductionOverviewView>().Refresh);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(
                Find("Shop Panel").gameObject.AddComponent<PanelShown>().shown,
                safe.GetComponentInChildren<MonetizationShopView>(true).Refresh);
            foreach (string name in new[] {"Achievement Toast", "Transaction Toast", "Major Unlock Banner", "Completion Banner"})
            {
                Transform t=Find(name); if(t==null) continue;
                Surface(t, Green);
                foreach(Text label in t.GetComponentsInChildren<Text>(true)) label.color=Color.white;
            }
            var nav=safe.GetComponent<MobilePanelNavigation>();
            var so=new SerializedObject(nav);
            var hud=so.FindProperty("explorationHud"); hud.arraySize=3;
            string[] hudNames={"Wood Counter","Main Objective","Pause Button"};
            for(int i=0;i<3;i++) hud.GetArrayElementAtIndex(i).objectReferenceValue=Find(hudNames[i]).gameObject;
            so.FindProperty("navigationRoot").objectReferenceValue=Find("Mobile Navigation").gameObject;
            so.ApplyModifiedPropertiesWithoutUndo();
            // The legacy presenter is created after CreateM20Ui; wire it here, after all views exist.
            var progressive=new SerializedObject(safe.GetComponent<ProgressiveHudVisibility>());
            progressive.FindProperty("progressionTabs").objectReferenceValue=Find("GOALS Navigation").gameObject;
            progressive.FindProperty("industryButton").objectReferenceValue=Find("ROUTES Navigation").gameObject;
            progressive.ApplyModifiedPropertiesWithoutUndo();
            Find("Progression Tabs").gameObject.SetActive(false);
            Find("Industry Operations Button").gameObject.SetActive(false);
        }

        private static void Hud()
        {
            Transform counter=Find("Wood Counter");
            RectTransform r=(RectTransform)counter;
            r.anchorMin=r.anchorMax=new Vector2(.5f,1); r.pivot=new Vector2(.5f,1);
            r.sizeDelta=new Vector2(1016,112); r.anchoredPosition=new Vector2(0,-24);
            counter.GetComponent<Image>().enabled=false;
            var empire=new SerializedObject(counter.GetComponent<EmpireLevelView>());
            empire.FindProperty("compactLabel").boolValue=true;empire.ApplyModifiedPropertiesWithoutUndo();
            string[] fields={"Cash Text","Wood Text","Empire Level Text"};
            string[] labels={"CASH","CARRY","EMPIRE"};
            TycoonGlyph[] icons={TycoonGlyph.Coin,TycoonGlyph.Crate,TycoonGlyph.Star};
            for(int i=0;i<3;i++)
            {
                Transform chip=Card(labels[i]+" Chip",counter,Paper);
                Place(chip,i==0?0:i==1?342:684,0,i==2?206:326,112);
                Icon(chip,icons[i],18,36,44,i==0?new Color(.82f,.50f,.10f):Green);
                Label(chip,labels[i],72,68,i==2?116:232,28,24,Green);
                Transform text=Find(fields[i]); text.SetParent(chip,false);
                Place(text,72,16,i==2?122:232,52);
                Text tx=text.GetComponent<Text>(); tx.fontSize=i==0?42:32; tx.resizeTextMaxSize=tx.fontSize;
                tx.alignment=TextAnchor.MiddleLeft;
            }
            Transform pause=Find("Pause Button");
            var pr=(RectTransform)pause; pr.anchorMin=pr.anchorMax=new Vector2(1,1); pr.pivot=new Vector2(1,1);
            pr.sizeDelta=new Vector2(128,128); pr.anchoredPosition=new Vector2(-20,-16);
            ButtonStyle(pause.GetComponent<Button>(),Ink,Color.white);
            Transform objective=Find("Main Objective");
            var or=(RectTransform)objective; or.anchorMin=or.anchorMax=new Vector2(.5f,1); or.pivot=new Vector2(.5f,1);
            or.sizeDelta=new Vector2(1016,104); or.anchoredPosition=new Vector2(0,-152);
            Surface(objective,Ink); Icon(objective,TycoonGlyph.Flag,24,30,44,MobileUiTheme.Yellow);
            Place(Find("Main Objective Text"),92,12,892,80);
            Text textObjective=Find("Main Objective Text").GetComponent<Text>();
            textObjective.color=Color.white; textObjective.alignment=TextAnchor.MiddleLeft; textObjective.fontSize=30; textObjective.resizeTextMaxSize=30;
            Find("District Status").gameObject.SetActive(false);
        }

        private static void Navigation()
        {
            Transform nav=Find("Mobile Navigation");
            var rect=(RectTransform)nav; rect.sizeDelta=new Vector2(-48,144); rect.anchoredPosition=new Vector2(0,8);
            Surface(nav,Ink);
            string[] names={"GOALS","AWARDS","ROUTES","FACTORY","MARKET","SHOP"};
            TycoonGlyph[] glyphs={TycoonGlyph.Flag,TycoonGlyph.Star,TycoonGlyph.Routes,TycoonGlyph.Factory,TycoonGlyph.Market,TycoonGlyph.Gem};
            for(int i=0;i<names.Length;i++)
            {
                Transform b=Find(names[i]+" Navigation");
                RectTransform br=(RectTransform)b; br.anchorMin=new Vector2(i/6f,0); br.anchorMax=new Vector2((i+1)/6f,1);
                br.offsetMin=new Vector2(4,8); br.offsetMax=new Vector2(-4,-8);
                ButtonStyle(b.GetComponent<Button>(),Ink,Color.white);
                Text t=b.GetComponentInChildren<Text>(); t.text=names[i]; t.fontSize=24; t.resizeTextMinSize=24; t.resizeTextMaxSize=24;
                var tr=t.rectTransform; tr.anchorMin=new Vector2(0,0); tr.anchorMax=new Vector2(1,.35f); tr.offsetMin=tr.offsetMax=Vector2.zero;
                Transform icon=Icon(b,glyphs[i],0,0,44,Color.white);
                var ir=(RectTransform)icon; ir.anchorMin=ir.anchorMax=new Vector2(.5f,.65f); ir.pivot=new Vector2(.5f,.5f); ir.anchoredPosition=Vector2.zero;
            }
            nav.gameObject.AddComponent<AdaptiveNavigationLayout>().Configure(
                UnityEngine.Object.FindAnyObjectByType<IndustryTycoon.Progression.LumberCampProgressionService>(),
                names.Select(n=>Find(n+" Navigation").GetComponent<Button>()).ToArray());
        }

        private static void Production()
        {
            Transform panel=Sheet("Production Panel",1160);
            string[] tabs={"PROCESS Tab","SMELT Tab","TOOLS Tab","ASSEMBLE Tab"};
            for(int i=0;i<4;i++) Place(Find(tabs[i]),24+i*244,1008,228,128);
            Title("Production Title",112,904,840,88,48);
            Icon(panel,TycoonGlyph.Factory,32,922,58,Green);
            Title("Production Recipe",32,842,936,62,32);
            Transform stock=Card("Production stock card",panel,Soft); Place(stock,24,674,968,152); stock.SetAsFirstSibling();
            Title("Production Buffers",48,720,920,96,30);
            Title("Production Status",48,678,920,44,28);
            Place(Find("Cycle Progress"),32,608,952,48); Surface(Find("Cycle Progress"),Soft);
            Title("Upgrade Summary",32,548,952,48,28);
            string[] buttons={"Speed Upgrade","Input Upgrade","Output Upgrade"};
            for(int i=0;i<3;i++) { Place(Find(buttons[i]),32+i*324,400,304,128); ButtonStyle(Find(buttons[i]).GetComponent<Button>(),Green,Color.white); }
            Title("Specialization Summary",32,330,952,58,28);
            Place(Find("Throughput Specialization"),32,188,468,128);
            Place(Find("Efficiency Specialization"),516,188,468,128);
            ButtonStyle(Find("Throughput Specialization").GetComponent<Button>(),new Color(.96f,.76f,.37f),Ink);
            ButtonStyle(Find("Efficiency Specialization").GetComponent<Button>(),new Color(.96f,.76f,.37f),Ink);
            Place(Find("Close Production"),32,28,952,128);
        }

        private static void Market()
        {
            Transform panel=Sheet("Market Orders Panel",1280);
            Title("Market Title",112,1152,856,88,42); Find("Market Title").GetComponent<Text>().text="Market orders";
            Icon(panel,TycoonGlyph.Market,32,1170,56,Green);
            Transform summary=Card("Market demand card",panel,Soft); Place(summary,24,940,968,188); summary.SetAsFirstSibling();
            Title("Demand Summary",48,956,920,156,30);
            for(int i=0;i<3;i++)
            {
                Transform card=Find("Order Card "+(i+1)); Place(card,24,692-i*252,968,228); Surface(card,Soft);
                Icon(card,TycoonGlyph.Crate,24,144,44,Green);
                Place(Find("Order Text "+(i+1)),88,24,572,180);
                Place(Find("Order Action "+(i+1)),688,48,252,128);
                ButtonStyle(Find("Order Action "+(i+1)).GetComponent<Button>(),Green,Color.white);
            }
            Place(Find("Close Market"),32,28,952,128);
        }

        private static void Logistics()
        {
            Transform panel=Sheet("Industrial Operations Panel",1400);
            Title("Industrial Empire Progress",32,1272,780,88,38);
            Place(Find("Close Industrial Operations"),816,1256,168,128);
            Transform stock=Card("Warehouse stock card",panel,Soft); Place(stock,24,724,472,516); stock.SetAsFirstSibling();
            Transform routes=Card("Warehouse route card",panel,Soft); Place(routes,512,724,480,516); routes.SetAsFirstSibling();
            Title("Warehouse Overview",48,748,424,468,28);
            Title("Route Status",536,1120,432,104,28);
            string[] buttons={"Toggle Processor Warehouse Route","Toggle Warehouse Packing Route","Toggle Smelter Warehouse Route"};
            for(int i=0;i<3;i++) Place(Find(buttons[i]),536,980-i*132,432,128);
            Transform goals=Card("Logistics goal card",panel,Soft); Place(goals,24,176,968,524); goals.SetAsFirstSibling();
            Title("Daily Weekly Goals",48,200,920,476,28);
            Place(Find("Claim Daily Goal"),32,28,468,128); Place(Find("Claim Weekly Goal"),516,28,468,128);
        }

        private static void Goals()
        {
            Transform panel=Sheet("Contract Panel",608);
            Icon(panel,TycoonGlyph.Flag,32,500,56,Green); Label(panel,"Your next milestone",112,488,824,80,42,Ink);
            Title("Contract Description",32,384,952,88,38);
            Title("Contract Progress",32,282,420,88,42);
            Title("Contract Reward",488,282,496,88,34);
            Title("Contract State",32,188,952,64,28);
            Place(Find("Claim Contract Button"),32,28,952,128); ButtonStyle(Find("Claim Contract Button").GetComponent<Button>(),Green,Color.white);
            Sheet("Achievements Panel",1280);
            Title("Achievements Title",32,1148,952,100,42);
            Find("Achievements Title").GetComponent<Text>().text="Empire milestones";
            Place(Find("Achievements Viewport"),24,24,968,1100);
            Find("Achievements Content").GetComponent<Text>().fontSize=30;
        }

        private static void Shop()
        {
            Transform panel=Sheet("Shop Panel",1480);
            Title("Shop Title",112,1352,856,88,44); Find("Shop Title").GetComponent<Text>().text="Make room to grow";
            Icon(panel,TycoonGlyph.Gem,32,1370,56,Green);
            Title("Gems Balance",32,1288,952,64,36);
            for(int i=0;i<4;i++) {
                Transform product=Find("Shop Product "+(i+1)); Place(product,32+(i%2)*484,1084-(i/2)*196,468,176);
                Icon(product,i==0?TycoonGlyph.Star:TycoonGlyph.Gem,20,104,40,Green);
                Text label=product.GetComponentInChildren<Text>(); label.rectTransform.offsetMin=new Vector2(20,12); label.rectTransform.offsetMax=new Vector2(-20,-52);
            }
            Title("Boost Status",32,808,952,68,32);
            string[] offers={"Rewarded Boost","Gem Boost","Rewarded Refresh","Gem Refresh"};
            for(int i=0;i<4;i++) { Transform offer=Find(offers[i]); Place(offer,32+(i%2)*484,644-(i/2)*160,468,144); ButtonStyle(offer.GetComponent<Button>(),i%2==0?Green:new Color(.96f,.76f,.37f),i%2==0?Color.white:Ink); }
            Title("Shop Status",32,352,952,100,28);
            Place(Find("Restore Purchases"),32,188,952,128);
            Place(Find("Close Shop"),32,28,952,128);
        }

        private static void Menus()
        {
            Transform main=Sheet("Main Menu",1040);
            Icon(main,TycoonGlyph.Factory,448,872,120,Green);
            Title("Game Title",32,628,952,224,82);
            Find("Game Title").GetComponent<Text>().alignment=TextAnchor.MiddleCenter;
            Title("Game Subtitle",32,550,952,64,30);
            Find("Game Subtitle").GetComponent<Text>().alignment=TextAnchor.MiddleCenter;
            string[] names={"Continue","New Game","Main Settings"};
            for(int i=0;i<3;i++) Place(Find(names[i]),32,364-i*164,952,140);
            ButtonStyle(Find("Continue").GetComponent<Button>(),Green,Color.white);
            Sheet("Pause Menu",880);
            Title("Paused Title",32,692,952,128,60);
            Place(Find("Resume"),32,488,952,144); Place(Find("Pause Settings"),32,300,952,144); Place(Find("Help"),32,112,952,144);
            ButtonStyle(Find("Resume").GetComponent<Button>(),Green,Color.white);
            Settings(); Sheet("Help Panel",1152);
            Title("Help Title",32,992,952,112,48);
            // The pause button also owns a child named Help Text; scope the body lookup.
            Text helpBody=Find("Help Panel").Find("Help Text").GetComponent<Text>();
            Place(helpBody.transform,48,208,920,748);helpBody.fontSize=30;helpBody.resizeTextMaxSize=30;
            helpBody.alignment=TextAnchor.UpperLeft;
            Place(Find("Help Back"),32,28,952,128);
            // Dialogs are opaque islands, not stacks of translucent panels.
            foreach(string n in new[]{"Reset Save Confirmation","Specialization Confirmation"}) {
                Transform t=Find(n); if(t!=null) Surface(t,Paper);
            }
        }

        private static void Settings()
        {
            Sheet("Settings Panel",1456);
            Title("Settings Title",32,1312,952,104,48);
            string[] volumes={"Master Volume","Music Volume","SFX Volume"};
            for(int i=0;i<volumes.Length;i++)
            {
                float y=1152-i*148;
                Title(volumes[i]+" Label",32,y,340,128,30);
                Slider slider=Find(volumes[i]).GetComponent<Slider>();
                Place(slider.transform,392,y,568,128);
                // A full-height transparent hit surface makes the entire row draggable.
                Image hit=slider.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
                RectTransform background=(RectTransform)slider.transform.Find("Background");
                background.anchorMin=new Vector2(0,.4f);background.anchorMax=new Vector2(1,.6f);
                background.offsetMin=background.offsetMax=Vector2.zero;Surface(background,Soft);
                RectTransform fillArea=(RectTransform)slider.transform.Find("Fill Area");
                fillArea.anchorMin=new Vector2(0,.4f);fillArea.anchorMax=new Vector2(1,.6f);
                fillArea.offsetMin=fillArea.offsetMax=Vector2.zero;
                slider.fillRect.GetComponent<Image>().color=Green;
                var handleArea=new GameObject("Handle Area",typeof(RectTransform));handleArea.transform.SetParent(slider.transform,false);
                var area=(RectTransform)handleArea.transform;area.anchorMin=new Vector2(0,.5f);area.anchorMax=new Vector2(1,.5f);area.sizeDelta=new Vector2(-56,72);
                var handle=new GameObject("Volume Handle",typeof(RectTransform),typeof(Image));handle.transform.SetParent(handleArea.transform,false);
                var hr=handle.GetComponent<RectTransform>();hr.sizeDelta=new Vector2(56,0);
                Surface(handle.transform,Green);handle.GetComponent<Image>().raycastTarget=false;
                slider.handleRect=hr;slider.targetGraphic=handle.GetComponent<Image>();
            }
            Place(Find("Haptics"),32,704,952,128);
            Place(Find("Graphics Preset"),32,552,952,128);
            Place(Find("Privacy Consent"),32,400,952,128);
            foreach(string name in new[]{"Haptics","Privacy Consent"}) {
                Transform toggle=Find(name);Transform check=toggle.Find("Checkmark");
                Transform track=Card("Toggle Track",toggle,Soft);track.SetAsFirstSibling();
                var rect=(RectTransform)track;rect.anchorMin=new Vector2(.78f,.18f);rect.anchorMax=new Vector2(.96f,.82f);rect.offsetMin=rect.offsetMax=Vector2.zero;
                check.GetComponent<Image>().color=Green;
            }
            Place(Find("Privacy Policy"),32,248,952,128);
            Title("Privacy Status",32,172,952,56,26);
            Place(Find("Settings Back"),32,28,952,128);
            ButtonStyle(Find("Settings Back").GetComponent<Button>(),Green,Color.white);
        }

        private static Transform Sheet(string name,float height)
        {
            Transform t=Find(name); if(t==null) throw new InvalidOperationException("Missing M20 view "+name);
            var r=(RectTransform)t; r.anchorMin=new Vector2(.03f,0); r.anchorMax=new Vector2(.97f,0); r.pivot=new Vector2(.5f,0);
            r.anchoredPosition=new Vector2(0,168); r.sizeDelta=new Vector2(0,height); Surface(t,Paper);
            return t;
        }
        private static void Title(string name,float x,float y,float w,float h,int size) {
            Transform t=Find(name); Place(t,x,y,w,h); Text text=t.GetComponent<Text>(); text.fontSize=size; text.resizeTextMaxSize=size; text.alignment=TextAnchor.MiddleLeft;
        }
        internal static Transform Find(string name) => safe.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);
        private static void Place(Transform t,float x,float y,float w,float h) {
            if(t==null) throw new InvalidOperationException("Missing presentation element");
            var r=(RectTransform)t; r.anchorMin=r.anchorMax=Vector2.zero; r.pivot=Vector2.zero; r.anchoredPosition=new Vector2(x,y); r.sizeDelta=new Vector2(w,h);
        }
        private static Transform Card(string name,Transform parent,Color color) {
            var g=new GameObject(name,typeof(RectTransform),typeof(Image)); g.transform.SetParent(parent,false); Surface(g.transform,color); g.GetComponent<Image>().raycastTarget=false; return g.transform;
        }
        private static Transform Icon(Transform parent,TycoonGlyph glyph,float x,float y,float size,Color color) {
            var g=new GameObject(glyph+" Icon",typeof(RectTransform),typeof(TycoonIcon)); g.transform.SetParent(parent,false); Place(g.transform,x,y,size,size);
            var icon=g.GetComponent<TycoonIcon>(); icon.Glyph=glyph; icon.color=color; icon.raycastTarget=false; return g.transform;
        }
        private static void Label(Transform parent,string value,float x,float y,float w,float h,int size,Color color) {
            var g=new GameObject(value,typeof(RectTransform),typeof(Text));g.transform.SetParent(parent,false);Place(g.transform,x,y,w,h);
            var t=g.GetComponent<Text>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.text=value;t.fontSize=size;t.fontStyle=FontStyle.Bold;t.color=color;t.alignment=TextAnchor.MiddleLeft;t.raycastTarget=false;
        }
        private static void Surface(Transform t,Color color) { MobileUiTheme.ApplySurface(t.GetComponent<Image>(),round,color,t.GetComponent<Image>().raycastTarget); }
        private static void ButtonStyle(Button b,Color color,Color textColor) {
            MobileUiTheme.ApplyButton(b,round,color); Text t=b.GetComponentInChildren<Text>(true);if(t!=null)t.color=textColor;
        }
    }
}
