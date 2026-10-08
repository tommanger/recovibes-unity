using NUnit.Framework;
using UnityEngine;

namespace RecoVibes.Tests
{
    public class RecoMathTests
    {
        [Test]
        public void AutoSlotsFillsTheSpaceGiven()
        {
            // 376 tall: 20 heading + 10 gap leaves 346 → 4 cards of 76 + 10.
            Assert.AreEqual(4, RecoMath.AutoSlots(RecoLayout.Vertical, 600, 376, true, 76, 200, 10, 20));
            Assert.AreEqual(4, RecoMath.AutoSlots(RecoLayout.Vertical, 600, 346, false, 76, 200, 10, 20));
            // 836 wide fits 4 cards of at least 200 + 10.
            Assert.AreEqual(4, RecoMath.AutoSlots(RecoLayout.Horizontal, 836, 100, true, 76, 200, 10, 20));
            Assert.AreEqual(1, RecoMath.AutoSlots(RecoLayout.Vertical, 600, 10, true, 76, 200, 10, 20), "always at least one");
            Assert.AreEqual(RecoMath.MaxSlots, RecoMath.AutoSlots(RecoLayout.Vertical, 600, 99999, true, 76, 200, 10, 20));
        }

        [Test]
        public void AutoScaleSizesInRealPoints()
        {
            // iPhone (460 dpi, 1179 px wide) showing a 1080-wide canvas: ~2.6x.
            Assert.AreEqual(2.63f, RecoMath.AutoScale(460, 1179f / 1080f), 0.02f);
            // Android at xxhdpi (480 dpi) with a 1080 canvas on a 1080 px screen: 3x.
            Assert.AreEqual(3f, RecoMath.AutoScale(480, 1f), 0.001f);
            // A 400-unit canvas on that phone already maps units to about a point (0.98 → 1).
            Assert.AreEqual(1f, RecoMath.AutoScale(460, 1179f / 400f), 0.001f);
            // Desktop monitor: never shrink below 1.
            Assert.AreEqual(1f, RecoMath.AutoScale(96, 1f));
            Assert.AreEqual(1f, RecoMath.AutoScale(0, 1f), "unknown dpi");
            Assert.AreEqual(6f, RecoMath.AutoScale(5000, 0.1f), "capped");
        }

        [Test]
        public void VisibleFractionIsTheShareOnScreen()
        {
            var screen = new Rect(0, 0, 1000, 1000);
            Assert.AreEqual(1f, RecoMath.VisibleFraction(new Rect(10, 10, 100, 100), screen), 1e-4);
            Assert.AreEqual(0.5f, RecoMath.VisibleFraction(new Rect(-50, 10, 100, 100), screen), 1e-4);
            Assert.AreEqual(0.25f, RecoMath.VisibleFraction(new Rect(950, 950, 100, 100), screen), 1e-4);
            Assert.AreEqual(0f, RecoMath.VisibleFraction(new Rect(2000, 0, 100, 100), screen));
            Assert.AreEqual(0f, RecoMath.VisibleFraction(new Rect(0, 0, 0, 100), screen));
        }

        [Test]
        public void ClickIdsAreFreshHex()
        {
            var a = RecoMath.NewClickId();
            StringAssert.IsMatch("^[0-9a-f]{32}$", a);
            Assert.AreNotEqual(a, RecoMath.NewClickId());
        }

        [Test]
        public void LanguagesAndHeadings()
        {
            Assert.AreEqual("he", RecoMath.LanguageCode(SystemLanguage.Hebrew));
            Assert.AreEqual("zh", RecoMath.LanguageCode(SystemLanguage.ChineseTraditional));
            Assert.AreEqual("en", RecoMath.LanguageCode(SystemLanguage.Unknown));
            Assert.AreEqual("Vous aimerez aussi", RecoMath.Heading("fr"));
            Assert.AreEqual("You might also like", RecoMath.Heading("he"), "scripts the built-in font can't draw fall back to English");
        }

        [Test]
        public void ParsesTheServerResponse()
        {
            var r = RecoMath.ParseResponse("{\"sourceDataId\":\"rv_a\",\"paused\":false,\"receipt\":\"sig\",\"recommendations\":[{\"dataId\":\"rv_b\",\"name\":\"Starvex\",\"url\":\"https://api.recovibes.com/go/rv_b\",\"host\":\"android:com.x\",\"categories\":[\"Games/Arcade\"]}],\"widget\":{\"theme\":\"light\",\"accent\":\"#ff5a5f\",\"heading\":\"\",\"hideHeading\":false,\"lang\":\"en\",\"showOnMobile\":true}}");
            Assert.AreEqual("sig", r.receipt);
            Assert.AreEqual("Starvex", r.recommendations[0].name);
            Assert.AreEqual("light", r.widget.theme);
            Assert.AreEqual(new Color(1f, 90 / 255f, 95 / 255f), RecoMath.ParseColor(r.widget.accent, Color.black));
            Assert.AreEqual(0, RecoMath.ParseResponse("{\"receipt\":\"x\"}").recommendations.Length);
            Assert.IsNull(RecoMath.ParseResponse("not json"));
        }
    }
}
