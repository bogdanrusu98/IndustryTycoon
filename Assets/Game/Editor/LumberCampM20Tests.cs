using IndustryTycoon.UI;
using NUnit.Framework;
using UnityEngine;

namespace IndustryTycoon.EditorTests
{
    public sealed class LumberCampM20Tests
    {
        [Test]
        public void ProductionPresentation_IgnoresDestroyedBuildingsDuringSceneTeardown()
        {
            var owner = new GameObject("District teardown test");
            var buildingObject = new GameObject("Destroyed processor");
            try
            {
                var district = owner.AddComponent<IndustryTycoon.Progression.IndustrialDistrictService>();
                var processor = buildingObject.AddComponent<IndustryTycoon.Processing.WoodProcessor>();
                var serialized = new UnityEditor.SerializedObject(district);
                var buildings = serialized.FindProperty("upgradeableBuildings");
                buildings.arraySize = 1;
                buildings.GetArrayElementAtIndex(0).objectReferenceValue = processor;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Object.DestroyImmediate(buildingObject);
                Assert.That(district.GetProductionStatus("wood_processor"),
                    Is.EqualTo(IndustryTycoon.Processing.ProductionStatus.LockedInactive));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [TestCase(360, 640, 0, 24, 360, 592)]
        [TestCase(1080, 1920, 0, 80, 1080, 1760)]
        [TestCase(1440, 3200, 40, 140, 1360, 2920)]
        public void SafeAreaAnchors_StayNormalizedAcrossPortraitSizes(
            int width, int height, int x, int y, int safeWidth, int safeHeight)
        {
            SafeAreaFitter.CalculateAnchors(
                new Rect(x, y, safeWidth, safeHeight),
                new Vector2Int(width, height),
                out Vector2 min,
                out Vector2 max);
            Assert.That(min.x, Is.InRange(0f, 1f));
            Assert.That(min.y, Is.InRange(0f, 1f));
            Assert.That(max.x, Is.InRange(0f, 1f));
            Assert.That(max.y, Is.InRange(0f, 1f));
            Assert.That(max.x, Is.GreaterThan(min.x));
            Assert.That(max.y, Is.GreaterThan(min.y));
        }

        [Test]
        public void PlayerFacingViews_DoNotUseLegacyImgui()
        {
            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public;
            Assert.That(typeof(IndustrialDistrictView).GetMethod("OnGUI", Flags), Is.Null);
            Assert.That(typeof(MarketPlanningView).GetMethod("OnGUI", Flags), Is.Null);
        }

        [TestCase(0, "0")]
        [TestCase(999, "999")]
        [TestCase(1200, "1.2K")]
        [TestCase(28460, "28.5K")]
        [TestCase(2000000, "2M")]
        [TestCase(int.MaxValue, "2.1B")]
        public void WalletFormatting_HandlesLargeRuntimeValues(int value, string expected)
        {
            Assert.That(WalletHud.FormatAmount(value), Is.EqualTo(expected));
        }

        [Test]
        public void IndustrialPlaycraftTheme_ReservesSemanticColors()
        {
            Assert.That(
                MobileUiTheme.StateColor(MobileUiState.Claimable, Color.black),
                Is.EqualTo(MobileUiTheme.SuccessGreen));
            Assert.That(
                MobileUiTheme.StateColor(MobileUiState.Warning, Color.black),
                Is.EqualTo(MobileUiTheme.RewardOrange));
            Assert.That(
                MobileUiTheme.StateColor(MobileUiState.Disabled, Color.black),
                Is.EqualTo(MobileUiTheme.DisabledSurface));
            Assert.That(MobileUiTheme.MinimumTouchTarget, Is.GreaterThanOrEqualTo(96f));
        }

        [TestCase("OBJECTIVE: UNLOCK WORKER", "Unlock Worker")]
        [TestCase("DRAG TO MOVE — GO TO THE WOOD", "Drag To Move  •  Go To The Wood")]
        [TestCase("NEXT: PROCESSOR\n$25 / $100", "Processor  •  $25 / $100")]
        [TestCase("PRODUCE PLANKS  •  6 / 10", "Produce Planks")]
        public void ObjectivePresentation_IsConciseWithoutChangingSource(
            string source,
            string expected)
        {
            Assert.That(NextUnlockGuidance.BuildPresentationTitle(source), Is.EqualTo(expected));
        }
    }
}
