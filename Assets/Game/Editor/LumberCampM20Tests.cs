using IndustryTycoon.UI;
using NUnit.Framework;
using UnityEngine;

namespace IndustryTycoon.EditorTests
{
    public sealed class LumberCampM20Tests
    {
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
    }
}
