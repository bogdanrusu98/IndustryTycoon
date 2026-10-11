using System.Linq;
using IndustryTycoon.Progression;
using IndustryTycoon.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    /// <summary>Idempotent presentation rebuild. Existing runtime owners and bound Text objects survive.</summary>
    public static class IndustrialHudBuilder
    {
        private static Sprite round;
        private static Transform safe;
        public static void Apply(Transform safeArea)
        {
            safe=safeArea;IndustrialHudAssets.Ensure();round=IndustrialHudAssets.Round;
            Transform row=Find("Wood Counter");
            Text cash=Find("Cash Text").GetComponent<Text>();
            Text carry=Find("Wood Text").GetComponent<Text>();
            Text empire=Find("Empire Level Text").GetComponent<Text>();
            foreach(Text value in new[]{cash,carry,empire})value.transform.SetParent(row,false);
            foreach(Transform child in row.Cast<Transform>().ToArray())
                if(child!=cash.transform && child!=carry.transform && child!=empire.transform)Object.DestroyImmediate(child.gameObject);
            row.GetComponent<Image>().enabled=false;
            Top(row,984,120,40);
            Transform cashChip=Panel("CASH Chip",row,0,0,278,120,MobileUiTheme.Cream);
            Emblem(cashChip,HudIllustration.Emblem.Coin,16,20,80);
            TextLabel(cashChip,"CASH",108,78,150,26,22,MobileUiTheme.MutedInk);
            MoveText(cash,cashChip,108,19,154,59,48,MobileUiTheme.IndustrialInk);
            Transform carryChip=Panel("CARRY Chip",row,288,0,322,120,MobileUiTheme.Cream);
            ResourceHudIcon resource=Resource(carryChip,14,28,88);
            Text resourceName=TextLabel(carryChip,"Resource",110,74,192,30,28,MobileUiTheme.IndustrialInk);
            MoveText(carry,carryChip,110,32,192,43,34,MobileUiTheme.IndustrialInk);
            MobileProgressBar capacity=Bar(carryChip,"Carry Capacity",20,14,282,12,MobileUiTheme.CreamMuted,MobileUiTheme.SuccessGreen);
            Transform badge=Panel("Full Badge",carryChip,242,98,68,26,MobileUiTheme.ProgressGold,false);
            TextLabel(badge,"FULL",6,0,56,26,17,MobileUiTheme.IndustrialInk).alignment=TextAnchor.MiddleCenter;
            var wood=new SerializedObject(row.GetComponent<WoodHud>());
            Bind(wood,"resourceLabelText",resourceName);Bind(wood,"resourceIcon",resource);
            Bind(wood,"chipCanvasGroup",carryChip.gameObject.AddComponent<CanvasGroup>());
            Bind(wood,"capacityBar",capacity);Bind(wood,"fullBadge",badge.gameObject);wood.ApplyModifiedPropertiesWithoutUndo();
            Transform empireChip=Panel("EMPIRE Chip",row,620,0,246,120,MobileUiTheme.Cream);
            Emblem(empireChip,HudIllustration.Emblem.Star,14,36,72);
            TextLabel(empireChip,"EMPIRE",96,78,138,26,22,MobileUiTheme.MutedInk);
            MoveText(empire,empireChip,96,37,138,43,34,MobileUiTheme.IndustrialInk);
            var progress=Bar(empireChip,"Empire Progress",18,14,210,14,MobileUiTheme.IndustrialInk,MobileUiTheme.ProgressGold);
            var level=new SerializedObject(row.GetComponent<EmpireLevelView>());
            level.FindProperty("compactLabel").boolValue=true;Bind(level,"progressBar",progress);level.ApplyModifiedPropertiesWithoutUndo();

            Transform pause=Find("Pause Button");
            foreach(Transform child in pause.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
            RemoveStyle(pause);Top(pause,108,120,40);
            ((RectTransform)pause).anchoredPosition=new Vector2(438,-40);
            MobileUiTheme.ApplyButton(pause.GetComponent<Button>(),round,MobileUiTheme.IndustrialInk);
            Depth(pause);InsetHighlight(pause);
            Panel("Pause Left",pause,37,38,11,44,MobileUiTheme.Cream,false).GetComponent<Image>().pixelsPerUnitMultiplier=8;
            Panel("Pause Right",pause,60,38,11,44,MobileUiTheme.Cream,false).GetComponent<Image>().pixelsPerUnitMultiplier=8;

            Transform objective=Find("Main Objective");Text title=Find("Main Objective Text").GetComponent<Text>();
            title.transform.SetParent(objective,false);
            foreach(Transform child in objective.Cast<Transform>().ToArray())if(child!=title.transform)Object.DestroyImmediate(child.gameObject);
            RemoveStyle(objective);Top(objective,984,112,176);
            MobileUiTheme.ApplySurface(objective.GetComponent<Image>(),round,MobileUiTheme.IndustrialInk,false);
            Depth(objective);InsetHighlight(objective);
            Emblem(objective,HudIllustration.Emblem.Flag,20,16,80);
            Text category=TextLabel(objective,"OBJECTIVE",116,69,650,28,24,MobileUiTheme.CreamMuted);
            MoveText(title,objective,116,17,698,52,32,MobileUiTheme.Cream);
            var objectiveProgress=Bar(objective,"Objective Progress",836,30,118,8,MobileUiTheme.DeepTeal,MobileUiTheme.ProgressGold);
            Text valueText=TextLabel(objectiveProgress.transform,"Objective Count",0,14,118,36,29,MobileUiTheme.Cream);
            valueText.alignment=TextAnchor.MiddleRight;objectiveProgress.Configure(objectiveProgress.Fill,valueText);
            var guidance=new SerializedObject(objective.GetComponent<NextUnlockGuidance>());
            Bind(guidance,"categoryText",category);Bind(guidance,"progressRoot",objectiveProgress.gameObject);Bind(guidance,"progressBar",objectiveProgress);guidance.ApplyModifiedPropertiesWithoutUndo();
            Find("District Status").gameObject.SetActive(false);
            // Hints stay under the HUD row so existing M20 menu visibility controls also hide them.
            Transform hint=Panel("Context Hint",row,154,-192,676,52,MobileUiTheme.IndustrialInk);
            Text hintLabel=TextLabel(hint,"Hint",16,7,644,38,22,MobileUiTheme.Cream);
            hintLabel.resizeTextForBestFit=true;hintLabel.resizeTextMinSize=18;hintLabel.resizeTextMaxSize=22;
            hint.gameObject.AddComponent<ContextHintView>().Configure(Object.FindAnyObjectByType<FirstSessionOnboardingService>(FindObjectsInactive.Include),hint.gameObject.AddComponent<CanvasGroup>(),hintLabel);

            Transform toast=Find("Transaction Toast");
            var toastOwner=Object.FindAnyObjectByType<TransactionToastView>(FindObjectsInactive.Include);
            var toastBinding=new SerializedObject(toastOwner);
            Text message=(Text)toastBinding.FindProperty("messageText").objectReferenceValue;
            foreach(Transform child in toast.Cast<Transform>().ToArray())if(child!=message.transform)Object.DestroyImmediate(child.gameObject);
            RemoveStyle(toast);MobileUiTheme.ApplySurface(toast.GetComponent<Image>(),round,MobileUiTheme.IndustrialInk,false);Depth(toast);
            RectTransform toastRect=(RectTransform)toast;toastRect.sizeDelta=new Vector2(210,84);
            ResourceHudIcon pickup=Resource(toast,10,6,72);MoveText(message,toast,91,14,106,56,38,MobileUiTheme.Cream);
            Bind(toastBinding,"resourceIcon",pickup);toastBinding.ApplyModifiedPropertiesWithoutUndo();
        }
        private static Transform Find(string name)=>safe.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
        private static void Bind(SerializedObject owner,string field,Object value)=>owner.FindProperty(field).objectReferenceValue=value;
        private static void RemoveStyle(Transform t) {var style=t.GetComponent<MobileUiStyle>();if(style!=null)Object.DestroyImmediate(style);}
        private static void Top(Transform t,float width,float height,float top)
        {var r=(RectTransform)t;r.anchorMin=r.anchorMax=new Vector2(.5f,1);r.pivot=new Vector2(.5f,1);r.sizeDelta=new Vector2(width,height);r.anchoredPosition=new Vector2(0,-top);}
        private static void Place(Transform t,float x,float y,float w,float h)
        {var r=(RectTransform)t;r.anchorMin=r.anchorMax=r.pivot=Vector2.zero;r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);}
        private static Transform Panel(string name,Transform parent,float x,float y,float w,float h,Color color,bool depth=true)
        {
            var g=new GameObject(name,typeof(RectTransform),typeof(Image));g.transform.SetParent(parent,false);Place(g.transform,x,y,w,h);
            MobileUiTheme.ApplySurface(g.GetComponent<Image>(),round,color,false);
            if(depth){Depth(g.transform);InsetHighlight(g.transform);}return g.transform;
        }
        private static void Depth(Transform t)
        {var shadow=t.GetComponent<Shadow>()??t.gameObject.AddComponent<Shadow>();shadow.effectDistance=new Vector2(0,-5);shadow.effectColor=new Color(.025f,.08f,.07f,.42f);}
        private static void InsetHighlight(Transform t)
        {
            var outline=t.GetComponent<Outline>()??t.gameObject.AddComponent<Outline>();outline.effectDistance=new Vector2(1.4f,1.4f);outline.effectColor=new Color(1,1,.91f,.42f);
        }
        private static Text TextLabel(Transform parent,string name,float x,float y,float w,float h,int size,Color color)
        {
            var g=new GameObject(name,typeof(RectTransform),typeof(Text));g.transform.SetParent(parent,false);
            Text t=g.GetComponent<Text>();t.font=UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.text=name;
            MoveText(t,parent,x,y,w,h,size,color);return t;
        }
        private static void MoveText(Text t,Transform parent,float x,float y,float w,float h,int size,Color color)
        {t.transform.SetParent(parent,false);Place(t.transform,x,y,w,h);t.fontSize=size;t.fontStyle=FontStyle.Bold;t.color=color;t.alignment=TextAnchor.MiddleLeft;t.raycastTarget=false;t.resizeTextForBestFit=true;t.resizeTextMaxSize=size;t.resizeTextMinSize=size-5;}
        private static void Emblem(Transform parent,HudIllustration.Emblem emblem,float x,float y,float size)
        {var g=new GameObject(emblem+" Illustration",typeof(RectTransform),typeof(HudIllustration));g.transform.SetParent(parent,false);Place(g.transform,x,y,size,size);g.GetComponent<HudIllustration>().Configure(emblem);}
        private static ResourceHudIcon Resource(Transform parent,float x,float y,float size)
        {var g=new GameObject("Resource Artwork",typeof(RectTransform),typeof(Image),typeof(ResourceHudIcon));g.transform.SetParent(parent,false);Place(g.transform,x,y,size,size);var icon=g.GetComponent<ResourceHudIcon>();icon.Configure(IndustrialHudAssets.Resources);return icon;}
        private static MobileProgressBar Bar(Transform parent,string name,float x,float y,float w,float h,Color track,Color fill)
        {
            Transform root=Panel(name,parent,x,y,w,h,track,false);
            Transform fillView=Panel(name+" Fill",root,0,0,w,h,fill,false);
            var rect=(RectTransform)fillView;rect.anchorMax=Vector2.one;rect.sizeDelta=Vector2.zero;
            var bar=root.gameObject.AddComponent<MobileProgressBar>();bar.Configure(fillView.GetComponent<Image>());return bar;
        }
    }
}
