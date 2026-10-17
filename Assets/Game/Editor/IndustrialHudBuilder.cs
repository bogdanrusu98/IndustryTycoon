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
            Top(row, 984, 112, 42);

            Transform cashChip = Card("CASH Chip", row, 0, 0, 278, 112, false);
            Emblem(cashChip, "Coin", 10, 10, 94);
            TextLabel(cashChip, "CASH", 110, 75, 152, 28, 24, MobileUiTheme.IndustrialInk);
            MoveText(cash, cashChip, 110, 14, 156, 62, 50, MobileUiTheme.IndustrialInk);
            cash.resizeTextMinSize = 32;

            Transform carryChip = Card("CARRY Chip", row, 290, 0, 320, 112, false);
            ResourceHudIcon resource = Resource(carryChip, 10, 22, 100);
            Text resourceName = TextLabel(carryChip, "Resource", 120, 61, 186, 36, 32, MobileUiTheme.IndustrialInk);
            MoveText(carry, carryChip, 120, 27, 186, 36, 34, MobileUiTheme.IndustrialInk);
            MobileProgressBar capacity = Bar(carryChip, "Carry Capacity", 22, 14, 276, 12,
                MobileUiTheme.CreamMuted, MobileUiTheme.SuccessGreen);
            Transform badge = Panel("Full Badge", carryChip, 234, 99, 74, 28, MobileUiTheme.ProgressGold);
            TextLabel(badge, "FULL", 4, 0, 66, 28, 18, MobileUiTheme.IndustrialInk).alignment = TextAnchor.MiddleCenter;
            var wood = new SerializedObject(row.GetComponent<WoodHud>());
            Bind(wood, "resourceLabelText", resourceName);
            Bind(wood, "resourceIcon", resource);
            Bind(wood, "chipCanvasGroup", carryChip.gameObject.AddComponent<CanvasGroup>());
            Bind(wood, "capacityBar", capacity);
            Bind(wood, "fullBadge", badge.gameObject);
            wood.ApplyModifiedPropertiesWithoutUndo();

            Transform empireChip = Card("EMPIRE Chip", row, 622, 0, 244, 112, false);
            Emblem(empireChip, "Star", 10, 29, 84);
            TextLabel(empireChip, "EMPIRE", 102, 75, 132, 28, 24, MobileUiTheme.IndustrialInk);
            MoveText(empire, empireChip, 102, 29, 132, 48, 40, MobileUiTheme.IndustrialInk);
            var progress = Bar(empireChip, "Empire Progress", 20, 14, 204, 15,
                MobileUiTheme.IndustrialInk, MobileUiTheme.ProgressGold);
            var level = new SerializedObject(row.GetComponent<EmpireLevelView>());
            level.FindProperty("compactLabel").boolValue = true;
            Bind(level, "progressBar", progress);
            level.ApplyModifiedPropertiesWithoutUndo();

            Transform pause = Find("Pause Button");
            ClearChildren(pause);
            RemoveStyle(pause);
            Top(pause, 106, 106, 44);
            ((RectTransform)pause).anchoredPosition = new Vector2(439, -44);
            MobileUiTheme.ApplyButton(pause.GetComponent<Button>(), round, MobileUiTheme.IndustrialInk);
            Skin(pause, 106, 106, true);
            Panel("Pause Left", pause, 35, 34, 11, 38, MobileUiTheme.Cream).GetComponent<Image>().pixelsPerUnitMultiplier = 10;
            Panel("Pause Right", pause, 60, 34, 11, 38, MobileUiTheme.Cream).GetComponent<Image>().pixelsPerUnitMultiplier = 10;

            Transform objective = Find("Main Objective");
            Text title = Find("Main Objective Text").GetComponent<Text>();
            title.transform.SetParent(objective, false);
            foreach (Transform child in objective.Cast<Transform>().ToArray())
                if (child != title.transform) Object.DestroyImmediate(child.gameObject);
            RemoveStyle(objective);
            Top(objective, 984, 112, 174);
            Skin(objective, 984, 112, true);
            // Existing title survives, but must render above the newly constructed face.
            title.transform.SetAsLastSibling();
            Emblem(objective, "Flag", 24, 12, 88);
            Text category = TextLabel(objective, "OBJECTIVE", 132, 73, 660, 30, 28, MobileUiTheme.CreamMuted);
            MoveText(title, objective, 132, 8, 692, 65, 32, MobileUiTheme.Cream);
            var objectiveProgress = Bar(objective, "Objective Progress", 840, 20, 112, 8,
                MobileUiTheme.DeepTeal, MobileUiTheme.ProgressGold);
            Text valueText = TextLabel(objectiveProgress.transform, "Objective Count", 0, 14, 112, 46, 34, MobileUiTheme.Cream);
            valueText.alignment = TextAnchor.MiddleRight;
            objectiveProgress.Configure(objectiveProgress.Fill, valueText);
            objectiveProgress.GetComponent<Image>().enabled = false;
            // SetProgress legitimately reenables its fill Image. A transparent fill
            // preserves that binding while this card presents only the numeric count.
            objectiveProgress.Fill.color = Color.clear;
            var guidance = new SerializedObject(objective.GetComponent<NextUnlockGuidance>());
            Bind(guidance, "categoryText", category);
            Bind(guidance, "progressRoot", objectiveProgress.gameObject);
            Bind(guidance, "progressBar", objectiveProgress);
            guidance.ApplyModifiedPropertiesWithoutUndo();
            Find("District Status").gameObject.SetActive(false);

            // Kept inside the existing navigation-owned HUD so menus hide it automatically.
            Transform hint = Card("Context Hint", row, 232, -196, 520, 48, true);
            Text hintLabel = TextLabel(hint, "Hint", 16, 4, 488, 40, 22, MobileUiTheme.Cream);
            hintLabel.alignment = TextAnchor.MiddleCenter;
            hint.gameObject.AddComponent<ContextHintView>().Configure(
                Object.FindAnyObjectByType<FirstSessionOnboardingService>(FindObjectsInactive.Include),
                hint.gameObject.AddComponent<CanvasGroup>(), hintLabel);

            // M18 explanations use the existing queue, in a subordinate temporary card.
            Transform unlock = Find("Major Unlock Banner");
            Text unlockText = unlock.GetComponentInChildren<Text>(true);
            foreach (Transform child in unlock.Cast<Transform>().ToArray())
                if (child != unlockText.transform) Object.DestroyImmediate(child.gameObject);
            RemoveStyle(unlock);
            Top(unlock, 780, 64, 366);
            Skin(unlock, 780, 64, true);
            MoveText(unlockText, unlock, 20, 9, 740, 46, 24, MobileUiTheme.Cream);
            unlockText.alignment = TextAnchor.MiddleCenter;

            Transform toast = Find("Transaction Toast");
            var toastBinding = new SerializedObject(Object.FindAnyObjectByType<TransactionToastView>(FindObjectsInactive.Include));
            Text message = (Text)toastBinding.FindProperty("messageText").objectReferenceValue;
            foreach (Transform child in toast.Cast<Transform>().ToArray())
                if (child != message.transform) Object.DestroyImmediate(child.gameObject);
            RemoveStyle(toast);
            var toastRect = (RectTransform)toast;
            toastRect.anchorMin = toastRect.anchorMax = new Vector2(.62f, .30f);
            toastRect.anchoredPosition = Vector2.zero;
            toastRect.sizeDelta = new Vector2(180, 78);
            Skin(toast, 180, 78, true);
            ResourceHudIcon pickup = Resource(toast, 6, 3, 72);
            MoveText(message, toast, 85, 10, 86, 58, 42, MobileUiTheme.Cream);
            Bind(toastBinding, "resourceIcon", pickup);
            toastBinding.ApplyModifiedPropertiesWithoutUndo();
            ApplyNavigationArtwork();
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
            // Full-rect sliced sprites retain a clean rounded edge at every card size.
            Image shell = card.GetComponent<Image>();
            MobileUiTheme.ApplySurface(shell, IndustrialHudAssets.Surface(dark), Color.white, card.GetComponent<Button>() != null);
            var shadow = card.GetComponent<Shadow>() ?? card.gameObject.AddComponent<Shadow>();
            shadow.effectDistance = new Vector2(0, -5); shadow.effectColor = new Color(.015f, .045f, .04f, .35f);
            var oldOutline = card.GetComponent<Outline>();
            if (oldOutline != null) Object.DestroyImmediate(oldOutline);
        }
        private static Text TextLabel(Transform parent, string name, float x, float y, float w, float h, int size, Color color)
        {
            var g = new GameObject(name, typeof(RectTransform), typeof(Text)); g.transform.SetParent(parent, false);
            Text t = g.GetComponent<Text>(); t.text = name;
            MoveText(t, parent, x, y, w, h, size, color); return t;
        }
        private static void MoveText(Text t, Transform parent, float x, float y, float w, float h, int size, Color color)
        {
            t.transform.SetParent(parent, false); t.transform.SetAsLastSibling(); Place(t.transform, x, y, w, h);
            // Legacy label shadows made small type look doubled at phone resolution.
            foreach (Shadow effect in t.GetComponents<Shadow>()) Object.DestroyImmediate(effect);
            t.font = IndustrialHudAssets.Font(size >= 40);
            t.fontSize = size; t.fontStyle = FontStyle.Normal; t.color = color; t.alignment = TextAnchor.MiddleLeft;
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
            fillView.gameObject.AddComponent<UiVerticalGradient>();
            var rect = (RectTransform)fillView; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
            var bar = root.gameObject.AddComponent<MobileProgressBar>(); bar.Configure(fillView.GetComponent<Image>()); return bar;
        }

        internal static void ApplyNavigationArtwork()
        {
            foreach (var entry in new[] { ("GOALS Navigation", "Clipboard"), ("AWARDS Navigation", "Trophy"), ("FACTORY Navigation", "Hardhat") })
            {
                Transform button = Find(entry.Item1);
                foreach (TycoonIcon glyph in button.GetComponentsInChildren<TycoonIcon>(true)) Object.DestroyImmediate(glyph.gameObject);
                Transform old = button.Find(entry.Item2 + " Illustration");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                Emblem(button, entry.Item2, 0, 0, 74);
                var icon = (RectTransform)button.Find(entry.Item2 + " Illustration");
                icon.anchorMin = icon.anchorMax = new Vector2(.5f, .65f);
                icon.pivot = new Vector2(.5f, .5f);
                icon.anchoredPosition = Vector2.zero;
            }
            foreach (Text label in Find("Mobile Navigation").GetComponentsInChildren<Text>(true))
            {
                foreach (Shadow effect in label.GetComponents<Shadow>()) Object.DestroyImmediate(effect);
                label.font = IndustrialHudAssets.Font();
                label.fontStyle = FontStyle.Normal;
            }
        }
    }
}
