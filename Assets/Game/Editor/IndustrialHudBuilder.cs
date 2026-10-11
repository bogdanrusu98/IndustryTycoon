using System.Linq;
using IndustryTycoon.Progression;
using IndustryTycoon.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    /// <summary>Rebuilds the composition while retaining M20 owners and their bound value objects.</summary>
    public static class IndustrialHudBuilder
    {
        private static Sprite round;
        private static Transform safe;

        public static void Apply(Transform safeArea)
        {
            safe = safeArea;
            IndustrialHudAssets.Ensure();
            round = IndustrialHudAssets.Round;
            Transform row = Find("Wood Counter");
            Text cash = Find("Cash Text").GetComponent<Text>();
            Text carry = Find("Wood Text").GetComponent<Text>();
            Text empire = Find("Empire Level Text").GetComponent<Text>();
            foreach (Text value in new[] { cash, carry, empire }) value.transform.SetParent(row, false);
            foreach (Transform child in row.Cast<Transform>().ToArray())
                if (child != cash.transform && child != carry.transform && child != empire.transform)
                    Object.DestroyImmediate(child.gameObject);
            row.GetComponent<Image>().enabled = false;
            Top(row, 1000, 160, 40);

            Transform cashChip = Card("CASH Chip", row, 0, 0, 278, 160, false);
            Emblem(cashChip, "Coin", 12, 26, 108);
            MoveText(cash, cashChip, 128, 38, 138, 84, 60, MobileUiTheme.IndustrialInk);
            cash.resizeTextMinSize = 30; // Compact K/M/B values still fit the same bound value zone.

            Transform carryChip = Card("CARRY Chip", row, 290, 0, 320, 160, false);
            ResourceHudIcon resource = Resource(carryChip, 8, 33, 122);
            Text resourceName = TextLabel(carryChip, "Resource", 140, 90, 164, 44, 36, MobileUiTheme.IndustrialInk);
            MoveText(carry, carryChip, 140, 43, 164, 49, 44, MobileUiTheme.IndustrialInk);
            MobileProgressBar capacity = Bar(carryChip, "Carry Capacity", 22, 20, 276, 13,
                MobileUiTheme.CreamMuted, MobileUiTheme.SuccessGreen);
            Transform badge = Panel("Full Badge", carryChip, 234, 143, 74, 28, MobileUiTheme.ProgressGold);
            TextLabel(badge, "FULL", 4, 0, 66, 28, 18, MobileUiTheme.IndustrialInk).alignment = TextAnchor.MiddleCenter;
            var wood = new SerializedObject(row.GetComponent<WoodHud>());
            Bind(wood, "resourceLabelText", resourceName);
            Bind(wood, "resourceIcon", resource);
            Bind(wood, "chipCanvasGroup", carryChip.gameObject.AddComponent<CanvasGroup>());
            Bind(wood, "capacityBar", capacity);
            Bind(wood, "fullBadge", badge.gameObject);
            wood.ApplyModifiedPropertiesWithoutUndo();

            Transform empireChip = Card("EMPIRE Chip", row, 622, 0, 248, 160, false);
            Emblem(empireChip, "Star", 10, 42, 100);
            MoveText(empire, empireChip, 120, 52, 116, 70, 48, MobileUiTheme.IndustrialInk);
            var progress = Bar(empireChip, "Empire Progress", 22, 20, 204, 16,
                MobileUiTheme.IndustrialInk, MobileUiTheme.ProgressGold);
            var level = new SerializedObject(row.GetComponent<EmpireLevelView>());
            level.FindProperty("compactLabel").boolValue = true;
            Bind(level, "progressBar", progress);
            level.ApplyModifiedPropertiesWithoutUndo();

            Transform pause = Find("Pause Button");
            ClearChildren(pause);
            RemoveStyle(pause);
            Top(pause, 118, 118, 61);
            ((RectTransform)pause).anchoredPosition = new Vector2(441, -61);
            MobileUiTheme.ApplyButton(pause.GetComponent<Button>(), round, MobileUiTheme.IndustrialInk);
            Skin(pause, 118, 118, true);
            Panel("Pause Left", pause, 35, 32, 16, 54, MobileUiTheme.Cream).GetComponent<Image>().pixelsPerUnitMultiplier = 7;
            Panel("Pause Right", pause, 67, 32, 16, 54, MobileUiTheme.Cream).GetComponent<Image>().pixelsPerUnitMultiplier = 7;

            Transform objective = Find("Main Objective");
            Text title = Find("Main Objective Text").GetComponent<Text>();
            title.transform.SetParent(objective, false);
            foreach (Transform child in objective.Cast<Transform>().ToArray())
                if (child != title.transform) Object.DestroyImmediate(child.gameObject);
            RemoveStyle(objective);
            Top(objective, 1000, 148, 224);
            Skin(objective, 1000, 148, true);
            // Existing title survives, but must render above the newly constructed face.
            title.transform.SetAsLastSibling();
            Emblem(objective, "Flag", 24, 19, 110);
            Text category = TextLabel(objective, "OBJECTIVE", 154, 102, 650, 28, 24, MobileUiTheme.CreamMuted);
            MoveText(title, objective, 154, 16, 678, 84, 36, MobileUiTheme.Cream);
            var objectiveProgress = Bar(objective, "Objective Progress", 852, 34, 118, 9,
                MobileUiTheme.DeepTeal, MobileUiTheme.ProgressGold);
            Text valueText = TextLabel(objectiveProgress.transform, "Objective Count", 0, 17, 118, 46, 34, MobileUiTheme.Cream);
            valueText.alignment = TextAnchor.MiddleRight;
            objectiveProgress.Configure(objectiveProgress.Fill, valueText);
            var guidance = new SerializedObject(objective.GetComponent<NextUnlockGuidance>());
            Bind(guidance, "categoryText", category);
            Bind(guidance, "progressRoot", objectiveProgress.gameObject);
            Bind(guidance, "progressBar", objectiveProgress);
            guidance.ApplyModifiedPropertiesWithoutUndo();
            Find("District Status").gameObject.SetActive(false);

            // Kept inside the existing navigation-owned HUD so menus hide it automatically.
            Transform hint = Card("Context Hint", row, 185, -240, 630, 48, true);
            Text hintLabel = TextLabel(hint, "Hint", 16, 4, 598, 40, 21, MobileUiTheme.Cream);
            hint.gameObject.AddComponent<ContextHintView>().Configure(
                Object.FindAnyObjectByType<FirstSessionOnboardingService>(FindObjectsInactive.Include),
                hint.gameObject.AddComponent<CanvasGroup>(), hintLabel);

            // M18 explanations use the existing queue, in a subordinate temporary card.
            Transform unlock = Find("Major Unlock Banner");
            Text unlockText = unlock.GetComponentInChildren<Text>(true);
            foreach (Transform child in unlock.Cast<Transform>().ToArray())
                if (child != unlockText.transform) Object.DestroyImmediate(child.gameObject);
            RemoveStyle(unlock);
            Top(unlock, 780, 64, 460);
            Skin(unlock, 780, 64, true);
            MoveText(unlockText, unlock, 20, 9, 740, 46, 24, MobileUiTheme.Cream);
            unlockText.alignment = TextAnchor.MiddleCenter;

            Transform toast = Find("Transaction Toast");
            var toastBinding = new SerializedObject(Object.FindAnyObjectByType<TransactionToastView>(FindObjectsInactive.Include));
            Text message = (Text)toastBinding.FindProperty("messageText").objectReferenceValue;
            foreach (Transform child in toast.Cast<Transform>().ToArray())
                if (child != message.transform) Object.DestroyImmediate(child.gameObject);
            RemoveStyle(toast);
            ((RectTransform)toast).sizeDelta = new Vector2(230, 100);
            Skin(toast, 230, 100, true);
            ResourceHudIcon pickup = Resource(toast, 9, 3, 94);
            MoveText(message, toast, 112, 18, 106, 64, 46, MobileUiTheme.Cream);
            Bind(toastBinding, "resourceIcon", pickup);
            toastBinding.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform Find(string name) => safe.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
        private static void Bind(SerializedObject owner, string field, Object value) => owner.FindProperty(field).objectReferenceValue = value;
        private static void ClearChildren(Transform t) { foreach (Transform child in t.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject); }
        private static void RemoveStyle(Transform t) { var style = t.GetComponent<MobileUiStyle>(); if (style != null) Object.DestroyImmediate(style); }
        private static void Top(Transform t, float width, float height, float top)
        {
            var r = (RectTransform)t;
            r.anchorMin = r.anchorMax = new Vector2(.5f, 1); r.pivot = new Vector2(.5f, 1);
            r.sizeDelta = new Vector2(width, height); r.anchoredPosition = new Vector2(0, -top);
        }
        private static void Place(Transform t, float x, float y, float w, float h)
        {
            var r = (RectTransform)t; r.anchorMin = r.anchorMax = r.pivot = Vector2.zero;
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
        }
        private static Transform Panel(string name, Transform parent, float x, float y, float w, float h, Color color)
        {
            var g = new GameObject(name, typeof(RectTransform), typeof(Image));
            g.transform.SetParent(parent, false); Place(g.transform, x, y, w, h);
            MobileUiTheme.ApplySurface(g.GetComponent<Image>(), round, color, false);
            return g.transform;
        }
        private static Transform Card(string name, Transform parent, float x, float y, float w, float h, bool dark)
        {
            Transform card = Panel(name, parent, x, y, w, h, Color.white);
            Skin(card, w, h, dark);
            return card;
        }
        private static void Skin(Transform card, float w, float h, bool dark)
        {
            // Shared three-layer shell: contact shadow, thick lower bevel, inset lit face.
            Image shell = card.GetComponent<Image>();
            MobileUiTheme.ApplySurface(shell, round, dark ? new Color(.16f, .30f, .30f) : new Color(.72f, .63f, .39f), card.GetComponent<Button>() != null);
            var shadow = card.GetComponent<Shadow>() ?? card.gameObject.AddComponent<Shadow>();
            shadow.effectDistance = new Vector2(0, -7); shadow.effectColor = new Color(.015f, .045f, .04f, .48f);
            var oldOutline = card.GetComponent<Outline>();
            if (oldOutline != null) Object.DestroyImmediate(oldOutline);
            Transform face = Panel("Inset Face", card, 2, 7, w - 4, h - 9,
                dark ? MobileUiTheme.IndustrialInk : new Color(1f, .962f, .835f));
            var highlight = face.gameObject.AddComponent<Outline>();
            highlight.effectDistance = new Vector2(1.2f, 1.2f);
            highlight.effectColor = dark ? new Color(.61f, .77f, .72f, .55f) : new Color(1, 1, .94f, .95f);
        }
        private static Text TextLabel(Transform parent, string name, float x, float y, float w, float h, int size, Color color)
        {
            var g = new GameObject(name, typeof(RectTransform), typeof(Text)); g.transform.SetParent(parent, false);
            Text t = g.GetComponent<Text>(); t.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.text = name;
            MoveText(t, parent, x, y, w, h, size, color); return t;
        }
        private static void MoveText(Text t, Transform parent, float x, float y, float w, float h, int size, Color color)
        {
            t.transform.SetParent(parent, false); t.transform.SetAsLastSibling(); Place(t.transform, x, y, w, h);
            t.fontSize = size; t.fontStyle = FontStyle.Bold; t.color = color; t.alignment = TextAnchor.MiddleLeft;
            t.raycastTarget = false; t.resizeTextForBestFit = true; t.resizeTextMaxSize = size; t.resizeTextMinSize = size - 6;
        }
        private static void Emblem(Transform parent, string emblem, float x, float y, float size)
        {
            var g = new GameObject(emblem + " Illustration", typeof(RectTransform), typeof(Image));
            g.transform.SetParent(parent, false); Place(g.transform, x, y, size, size);
            Image image = g.GetComponent<Image>(); image.sprite = IndustrialHudAssets.Emblem(emblem);
            image.preserveAspect = true; image.raycastTarget = false;
        }
        private static ResourceHudIcon Resource(Transform parent, float x, float y, float size)
        {
            var g = new GameObject("Resource Artwork", typeof(RectTransform), typeof(Image), typeof(ResourceHudIcon));
            g.transform.SetParent(parent, false); Place(g.transform, x, y, size, size);
            var icon = g.GetComponent<ResourceHudIcon>(); icon.Configure(IndustrialHudAssets.Resources); return icon;
        }
        private static MobileProgressBar Bar(Transform parent, string name, float x, float y, float w, float h, Color track, Color fill)
        {
            Transform root = Panel(name, parent, x, y, w, h, track);
            root.GetComponent<Image>().pixelsPerUnitMultiplier = 6;
            Transform fillView = Panel(name + " Fill", root, 0, 0, w, h, fill);
            fillView.GetComponent<Image>().pixelsPerUnitMultiplier = 6;
            var rect = (RectTransform)fillView; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
            var bar = root.gameObject.AddComponent<MobileProgressBar>(); bar.Configure(fillView.GetComponent<Image>()); return bar;
        }
    }
}
