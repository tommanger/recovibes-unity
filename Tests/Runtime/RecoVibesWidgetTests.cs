using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RecoVibes.PlayTests
{
    public class RecoVibesWidgetTests
    {
        GameObject canvasGo;
        RecoVibesWidget widget;
        readonly List<string> tracked = new List<string>();
        readonly List<string> opened = new List<string>();
        Action<string> savedOpenUrl;

        static RecoResponse Sample(int n, string theme = "") => new RecoResponse
        {
            receipt = "test-receipt",
            recommendations = Enumerable.Range(1, n).Select(i => new RecoCard { dataId = "rv_" + i, name = "Game " + i, url = "https://example.com/" + i, description = "A fine game" }).ToArray(),
            widget = new RecoDesign { theme = theme, lang = "en" },
        };

        [SetUp]
        public void SetUp()
        {
            canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var go = new GameObject("RecoVibes", typeof(RectTransform));
            go.SetActive(false); // no network load: tests render sample data
            go.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(Mathf.Min(600, Screen.width), Mathf.Min(400, Screen.height));
            widget = go.AddComponent<RecoVibesWidget>();
            widget.apiBase = "http://127.0.0.1:9"; // nothing listens: reports go nowhere
            widget.Tracked += (type, target) => tracked.Add(type + ":" + target);
            savedOpenUrl = RecoVibesWidget.OpenUrl;
            RecoVibesWidget.OpenUrl = url => opened.Add(url);
            tracked.Clear();
            opened.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            RecoVibesWidget.OpenUrl = savedOpenUrl;
            UnityEngine.Object.Destroy(canvasGo);
        }

        IEnumerator Show(RecoResponse data)
        {
            widget.gameObject.SetActive(true);
            widget.Render(data);
            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        [UnityTest]
        public IEnumerator FillsTheRectangleAndCountsOneVisibleSecond()
        {
            int rendered = -1;
            widget.Rendered += n => rendered = n;
            yield return Show(Sample(8));
            Assert.Greater(rendered, 0);
            Assert.LessOrEqual(rendered, 8);
            var names = widget.GetComponentsInChildren<Text>().Where(t => t.name == "Name").Select(t => t.text).ToList();
            Assert.AreEqual(rendered, names.Count);
            Assert.AreEqual("Game 1", names[0]);

            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsFalse(tracked.Any(t => t.StartsWith("impression")), "counted before a full second");
            yield return new WaitForSecondsRealtime(1.0f);
            Assert.AreEqual(1, tracked.Count(t => t == "impression:"), "one widget view");
            Assert.AreEqual(1, tracked.Count(t => t == "impression:rv_1"), "first card counted once");
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.AreEqual(1, tracked.Count(t => t == "impression:rv_1"), "a card is counted once per screen view");
        }

        [UnityTest]
        public IEnumerator HiddenCardsAreNotCounted()
        {
            canvasGo.AddComponent<CanvasGroup>().alpha = 0f;
            yield return Show(Sample(3));
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.IsFalse(tracked.Any(t => t.StartsWith("impression")), "invisible widget counted");
        }

        [UnityTest]
        public IEnumerator TapReportsTheClickThenOpensTheStore()
        {
            yield return Show(Sample(3));
            var card = widget.GetComponentsInChildren<Button>().First(b => b.name == "Card 2");
            card.onClick.Invoke();
            card.onClick.Invoke(); // a double tap is still one visit
            yield return new WaitForSecondsRealtime(1.0f);
            CollectionAssert.AreEqual(new[] { "click:rv_2" }, tracked.Where(t => t.StartsWith("click")).ToArray());
            CollectionAssert.AreEqual(new[] { "https://example.com/2" }, opened);
        }

        [UnityTest]
        public IEnumerator PausedOrEmptyShowsNothing()
        {
            int rendered = -1;
            widget.Rendered += n => rendered = n;
            var paused = Sample(3);
            paused.paused = true;
            yield return Show(paused);
            Assert.AreEqual(0, rendered);
            Assert.AreEqual(0, widget.GetComponentsInChildren<Button>().Length);
            yield return Show(Sample(0));
            Assert.AreEqual(0, rendered);
        }

        [UnityTest]
        public IEnumerator FixedSlotsAndTheme()
        {
            widget.slots = 2;
            widget.layout = RecoLayout.Horizontal;
            yield return Show(Sample(5, "light"));
            Assert.AreEqual(2, widget.GetComponentsInChildren<Button>().Count(b => b.name.StartsWith("Card")));
            var panel = widget.transform.Find("RecoVibes").GetComponent<Image>();
            Assert.Greater(panel.color.r, 0.5f, "light theme from the dashboard");
        }

        [UnityTest]
        public IEnumerator ScaleGrowsTextAndLayoutNotTheTransform()
        {
            widget.slots = 2;
            yield return Show(Sample(3));
            var baseName = widget.GetComponentsInChildren<Text>().First(t => t.name == "Name");
            int baseFont = baseName.fontSize;
            float baseCard = Card(1).rect.height;

            widget.scale = 2.5f;
            widget.Render(Sample(3));
            yield return null;
            var name = widget.GetComponentsInChildren<Text>().First(t => t.name == "Name");
            Assert.AreEqual(Mathf.RoundToInt(baseFont * 2.5f), name.fontSize, "text must be drawn bigger, not stretched");
            Assert.AreEqual(baseCard * 2.5f, Card(1).rect.height, 0.01f);
            Assert.AreEqual(Vector3.one, widget.transform.localScale);
            Assert.AreEqual(24f / (10f * 2.5f), Card(1).GetComponent<Image>().pixelsPerUnitMultiplier, 0.001f, "corners scale too");
        }

        // ---- the dashboard's templates, as the server describes them ----

        RectTransform Card(int n) => (RectTransform)widget.GetComponentsInChildren<Button>().First(b => b.name == "Card " + n).transform;

        IEnumerable<RectTransform> Cards() => widget.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Card")).Select(b => (RectTransform)b.transform);

        static RecoResponse Styled(int n, Action<RecoStyle> tune)
        {
            var r = Sample(n);
            r.recommendations[1].name = "A much longer game name";
            r.native = new RecoStyle { version = 1 };
            tune(r.native);
            return r;
        }

        [UnityTest]
        public IEnumerator DashboardSlotsAndColumns()
        {
            yield return Show(Styled(8, s => { s.slots = 3; s.minWidth = 180; s.maxColumns = 4; }));
            var cards = Cards().ToList();
            Assert.AreEqual(3, cards.Count, "the dashboard's slot count");
            Assert.AreEqual(cards[0].anchoredPosition.y, cards[2].anchoredPosition.y, 0.01f, "600 wide fits three 180-wide columns");
        }

        [UnityTest]
        public IEnumerator PillsFlowAsChipsSizedToTheirNames()
        {
            yield return Show(Styled(6, s =>
            {
                s.template = "pills"; s.layout = "chips"; s.maxColumns = 0; s.itemHeight = 36; s.radius = 999; s.gap = 8; s.rowGap = 8;
                s.avatar = true; s.avatarSize = 24; s.avatarRadius = 12; s.nameSize = 13; s.nameBold = false; s.descShow = false;
            }));
            Assert.IsFalse(widget.GetComponentsInChildren<Text>().Any(t => t.name == "Description"));
            Assert.Greater(Card(2).rect.width, Card(1).rect.width + 40, "a pill is as wide as its name");
            Assert.AreEqual(Card(1).anchoredPosition.y, Card(2).anchoredPosition.y, 0.01f, "pills share a line");
            Assert.AreEqual(36f, Card(1).rect.height, 0.01f);
            Assert.AreEqual(widget.GetComponentsInChildren<Button>().Count(b => b.name.StartsWith("Card")), widget.GetComponentsInChildren<Image>().Count(i => i.name == "Avatar"));
        }

        [UnityTest]
        public IEnumerator TerminalHasATitleBarPrefixesAndOneColumn()
        {
            yield return Show(Styled(4, s =>
            {
                s.template = "terminal"; s.maxColumns = 1; s.minWidth = 0; s.itemHeight = 24; s.gap = 0; s.rowGap = 0; s.itemPadX = 0;
                s.cardFill = false; s.border = false; s.nameAccent = true; s.nameBold = false; s.descInline = true; s.descLines = 1;
                s.prefix = "→"; s.mono = true; s.headingBar = true; s.headingUppercase = false;
            }));
            Assert.IsNotNull(widget.transform.Find("RecoVibes/HeadingBar"));
            var cards = Cards().ToList();
            Assert.Greater(cards.Count, 1);
            Assert.IsTrue(cards.All(c => Mathf.Approximately(c.anchoredPosition.x, cards[0].anchoredPosition.x)), "one column");
            Assert.IsTrue(cards.All(c => c.Find("Prefix")?.GetComponent<Text>().text == "→"));
            Assert.AreEqual(0f, cards[0].GetComponent<Image>().color.a, "no card background");
        }

        [UnityTest]
        public IEnumerator MinimalIsAListWithDividers()
        {
            yield return Show(Styled(4, s =>
            {
                s.template = "minimal"; s.maxColumns = 3; s.minWidth = 260; s.itemHeight = 41; s.gap = 28; s.rowGap = 0; s.itemPadX = 2;
                s.cardFill = false; s.border = false; s.divider = true; s.descInline = true; s.descLines = 1; s.suffix = "↗";
            }));
            var cards = Cards().ToList();
            Assert.IsTrue(cards.All(c => c.Find("Divider") != null && c.Find("Suffix") != null));
            Assert.AreEqual(cards[0].anchoredPosition.y, cards[1].anchoredPosition.y, 0.01f, "description beside the name, two columns in 600");
        }

        // End to end against a running server (set RECOVIBES_TEST_API and
        // RECOVIBES_TEST_DATA_ID); skipped otherwise.
        [UnityTest]
        public IEnumerator LoadsAndReportsAgainstARealServer()
        {
            var api = Environment.GetEnvironmentVariable("RECOVIBES_TEST_API");
            var id = Environment.GetEnvironmentVariable("RECOVIBES_TEST_DATA_ID");
            if (string.IsNullOrEmpty(api) || string.IsNullOrEmpty(id)) Assert.Ignore("RECOVIBES_TEST_API / RECOVIBES_TEST_DATA_ID not set");
            int rendered = -1;
            widget.Rendered += n => rendered = n;
            widget.apiBase = api;
            widget.dataId = id;
            widget.gameObject.SetActive(true);
            for (float t = 0; rendered < 0 && t < 10f; t += Time.unscaledDeltaTime) yield return null;
            Assert.Greater(rendered, 0, "no recommendations from the server");
            yield return new WaitForSecondsRealtime(1.6f);
            Assert.Contains("ready:", tracked);
            Assert.IsTrue(tracked.Any(t => t.StartsWith("impression:rv_")), "no card impression reported");
            widget.GetComponentsInChildren<Button>().First(b => b.name == "Card 1").onClick.Invoke();
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.AreEqual(1, opened.Count);
            Debug.Log("[RecoVibes test] opened " + opened[0]);
        }
    }
}
