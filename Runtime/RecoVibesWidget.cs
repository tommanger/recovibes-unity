using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("RecoVibes.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("RecoVibes.PlayTests")]

namespace RecoVibes
{
    /// <summary>
    /// Shows recommendations from the RecoVibes network inside this RectTransform.
    /// Add it to a UI object under a Canvas, set <see cref="dataId"/>, and size the
    /// rectangle: the widget fills it with as many cards as fit (or <see cref="slots"/>).
    ///
    /// A card counts as viewed once half of it has been on screen for a second while
    /// the game has focus; a tap opens the app's store page. Each time the object is
    /// enabled counts as a new screen view, like a page load on a website.
    /// </summary>
    [AddComponentMenu("UI/RecoVibes Recommendations")]
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class RecoVibesWidget : MonoBehaviour
    {
        public const string WidgetVersion = "unity-1";
        public const string PackageVersion = "1.2.1";

        public enum ThemeMode { FromDashboard, Light, Dark }

        [Tooltip("Your app's ID from the RecoVibes dashboard (Install tab), e.g. rv_ab12cd34.")]
        public string dataId = "";

        [Tooltip("How many recommendations to show. 0 = as many as fit in this rectangle.")]
        [Range(0, RecoMath.MaxSlots)] public int slots = 0;

        [Tooltip("FromDashboard follows the design you picked in the dashboard. Vertical forces one column, Horizontal one row.")]
        public RecoLayout layout = RecoLayout.FromDashboard;

        [Tooltip("FromDashboard uses the theme from your RecoVibes design settings (dark when it's Auto).")]
        public ThemeMode theme = ThemeMode.FromDashboard;

        [Tooltip("Optional: your game's font. Empty = Unity's built-in font.")]
        public Font font;

        [Tooltip("Optional: a monospaced font for the Terminal template. Empty = the font above.")]
        public Font monoFont;

        [Tooltip("Size multiplier for everything (text, spacing, cards). 0 = Auto: sized in real points for the device, whatever your canvas resolution - e.g. ×2.6 on a 1080-wide canvas on a phone. Never scale the object's transform instead: Unity would draw the text small and stretch it (blurry).")]
        [Min(0f)] public float scale = 0f;

        [Tooltip("Leave as is, unless you test against your own RecoVibes server.")]
        public string apiBase = "https://api.recovibes.com";

        /// <summary>Opens a tapped card's link. Replace it to route links yourself.</summary>
        public static Action<string> OpenUrl = Application.OpenURL;

        /// <summary>Raised after each render with the number of cards shown (0 = nothing to show).</summary>
        public event Action<int> Rendered;

        // Tests: (event type, target dataId) for every report sent.
        internal event Action<string, string> Tracked;

        const string AttributionUrl = "https://recovibes.com/?utm_source=unity&utm_medium=attribution";

        RecoResponse data;
        string receipt = "";
        int shownCount;
        bool hostSeen;
        readonly HashSet<string> seen = new HashSet<string>();
        readonly List<CardView> cards = new List<CardView>();
        RectTransform content;
        Vector2 renderedSize;
        float renderedScale = 1f;
        float k = 1f; // the scale in use while rendering
        float tick, lastClick = -10f;
        Coroutine loading;

        class CardView
        {
            public RecoCard card;
            public RectTransform rect;
            public float visibleFor;
        }

        RectTransform Rect => (RectTransform)transform;

        void OnEnable()
        {
            if (string.IsNullOrWhiteSpace(dataId))
            {
                Debug.LogWarning("[RecoVibes] Set the Data Id from your RecoVibes dashboard (Install tab).", this);
                return;
            }
            loading = StartCoroutine(Load());
        }

        void OnDisable()
        {
            if (loading != null) StopCoroutine(loading);
            loading = null;
            foreach (var c in cards) c.visibleFor = 0f;
        }

        IEnumerator Load()
        {
            yield return null; // let the layout settle so we know our size
            int ask = slots > 0 ? slots : RecoMath.MaxSlots;
            string url = Api("/api/widget/" + UnityWebRequest.EscapeURL(dataId.Trim()) + "?slots=" + ask);
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 10;
                SetHeaders(req);
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("[RecoVibes] Couldn't load recommendations: " + req.error, this);
                    loading = null;
                    yield break;
                }
                var parsed = RecoMath.ParseResponse(req.downloadHandler.text);
                if (parsed == null || string.IsNullOrEmpty(parsed.receipt))
                {
                    Debug.LogWarning("[RecoVibes] Unexpected response from the server.", this);
                    loading = null;
                    yield break;
                }
                // A fresh load is a fresh screen view.
                data = parsed;
                receipt = parsed.receipt;
                hostSeen = false;
                seen.Clear();
            }
            loading = null;
            Track("ready");
            Render(data);
        }

        // ---- rendering ----

        /// <summary>Draws <paramref name="response"/> into this rectangle (also used by tests).</summary>
        internal void Render(RecoResponse response)
        {
            data = response;
            if (response != null && !string.IsNullOrEmpty(response.receipt)) receipt = response.receipt;
            if (content != null) DestroyNow(content.gameObject);
            content = null;
            cards.Clear();
            shownCount = 0;
            renderedSize = Rect.rect.size;
            k = renderedScale = EffectiveScale();

            var recs = response?.recommendations ?? new RecoCard[0];
            if (response == null || response.paused || recs.Length == 0)
            {
                Rendered?.Invoke(0);
                return;
            }

            var design = response.widget ?? new RecoDesign();
            var st = response.native != null && response.native.version >= 1 ? response.native : RecoStyle.Classic(design);
            bool dark = theme == ThemeMode.Dark || (theme == ThemeMode.FromDashboard && st.theme != "light");
            var pal = new Palette(dark ? st.dark : st.light);
            style = st;

            content = NewRect("RecoVibes", Rect);
            Stretch(content);
            var panel = Rounded(content.gameObject, pal.Panel, st.panelRadius);
            panel.raycastTarget = false;

            float pad = st.padding * k, top = pad;
            if (st.headingShow)
            {
                string title = !string.IsNullOrEmpty(st.headingText) ? st.headingText : RecoMath.Heading(design.lang);
                top = st.headingBar ? BuildHeadingBar(title, pal) + pad * 0.7f : BuildHeading(title, pal, pad);
            }
            var area = new UnityEngine.Rect(pad, top, renderedSize.x - pad * 2, Mathf.Max(0, renderedSize.y - top - pad));
            int limit = Mathf.Min(slots > 0 ? slots : st.slots > 0 ? st.slots : RecoMath.MaxSlots, recs.Length);

            if (st.layout == "chips") LayoutChips(recs, limit, area, pal);
            else LayoutGrid(recs, limit, area, pal);

            shownCount = cards.Count;
            EnsureEventSystem();
            Rendered?.Invoke(shownCount);
        }

        RecoStyle style = new RecoStyle();

        // Title on the left, attribution on the right. Returns where items start.
        float BuildHeading(string title, Palette pal, float pad)
        {
            float h = Mathf.Max(20f, style.headingSize * 1.6f) * k;
            var t = NewText(content, "Title", style.headingUppercase ? title.ToUpperInvariant() : title, style.headingSize, pal.Muted, true, style.mono);
            Place(t.rectTransform, pad, pad, renderedSize.x - pad * 2, h);
            var by = NewText(content, "By RecoVibes", "by RecoVibes", style.headingSize - 1, pal.Muted, false, style.mono);
            by.alignment = TextAnchor.MiddleRight;
            by.raycastTarget = true;
            Place(by.rectTransform, renderedSize.x - pad - by.preferredWidth - 2 * k, pad, by.preferredWidth + 2 * k, h);
            var button = by.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OpenUrl?.Invoke(AttributionUrl));
            t.rectTransform.sizeDelta = new Vector2(renderedSize.x - pad * 3 - by.preferredWidth, h);
            return pad + h + 10f * k;
        }

        // A window title bar with traffic lights (terminal). Returns its height.
        float BuildHeadingBar(string title, Palette pal)
        {
            float h = (style.headingSize + 18f) * k;
            var bar = NewRect("HeadingBar", content);
            Place(bar, 0, 0, renderedSize.x, h);
            Rounded(bar.gameObject, pal.Bar, style.panelRadius).raycastTarget = false;
            var square = NewRect("BarBottom", bar); // only the top corners are round
            Place(square, 0, h / 2, renderedSize.x, h / 2);
            var sq = square.gameObject.AddComponent<Image>();
            sq.color = pal.Bar;
            sq.raycastTarget = false;
            var line = NewRect("BarLine", bar);
            Place(line, 0, h - Mathf.Max(1f, k), renderedSize.x, Mathf.Max(1f, k));
            line.gameObject.AddComponent<Image>().color = pal.Line;
            float dot = 9f * k, x = 12f * k;
            foreach (var hex in new[] { "#ff5f57", "#febc2e", "#28c840" })
            {
                var d = NewRect("Dot", bar);
                Place(d, x, (h - dot) / 2, dot, dot);
                var img = d.gameObject.AddComponent<Image>();
                img.sprite = Sprites.Circle;
                img.color = RecoMath.ParseColor(hex, Color.gray);
                img.raycastTarget = false;
                x += dot + 5f * k;
            }
            var t = NewText(bar, "Title", style.headingUppercase ? title.ToUpperInvariant() : title, style.headingSize, pal.Muted, false, style.mono);
            Place(t.rectTransform, x + 24f * k, 0, renderedSize.x - x - 36f * k, h);
            return h;
        }

        void LayoutGrid(RecoCard[] recs, int limit, UnityEngine.Rect area, Palette pal)
        {
            float itemH = style.itemHeight * k, gap = style.gap * k, rowGap = style.rowGap * k;
            int cols = layout == RecoLayout.Vertical ? 1
                : style.minWidth > 0 ? Mathf.Clamp(Mathf.FloorToInt((area.width + gap) / (style.minWidth * k + gap)), 1, Mathf.Max(1, style.maxColumns))
                : Mathf.Max(1, style.maxColumns);
            int rows = layout == RecoLayout.Horizontal ? 1 : Mathf.Max(1, Mathf.FloorToInt((area.height + rowGap) / (itemH + rowGap)));
            int n = (slots > 0 || style.slots > 0) ? limit : Mathf.Min(limit, rows * cols);
            if (layout == RecoLayout.Horizontal) cols = Mathf.Max(1, n);
            float cellW = (area.width - gap * (cols - 1)) / cols;
            for (int i = 0; i < n; i++)
            {
                int col = i % cols, row = i / cols;
                cards.Add(BuildItem(recs[i], i, pal, area.x + col * (cellW + gap), area.y + row * (itemH + rowGap), cellW, itemH));
            }
        }

        void LayoutChips(RecoCard[] recs, int limit, UnityEngine.Rect area, Palette pal)
        {
            float h = style.itemHeight * k, gap = style.gap * k, rowGap = style.rowGap * k;
            int lines = layout == RecoLayout.Horizontal ? 1 : Mathf.Max(1, Mathf.FloorToInt((area.height + rowGap) / (h + rowGap)));
            float x = 0, y = 0;
            int line = 0;
            for (int i = 0; i < limit; i++)
            {
                float w = Mathf.Min(area.width, ChipWidth(recs[i]));
                if (x > 0 && x + w > area.width)
                {
                    if (++line >= lines) break;
                    x = 0;
                    y += h + rowGap;
                }
                cards.Add(BuildItem(recs[i], i, pal, area.x + x, area.y + y, w, h));
                x += w + gap;
            }
        }

        float ChipWidth(RecoCard rec)
        {
            var probe = NewText(content, "Probe", DisplayName(rec), style.nameSize, Color.clear, style.nameBold, style.mono);
            float w = probe.preferredWidth;
            DestroyNow(probe.gameObject);
            return style.itemPadX * k * 2 + (style.avatar ? style.avatarSize * k + 8f * k : 0) + w + 2f * k;
        }

        CardView BuildItem(RecoCard rec, int index, Palette pal, float x, float y, float w, float h)
        {
            var card = NewRect("Card " + (index + 1), content);
            Place(card, x, y, w, h);
            Image target;
            if (style.border)
            {
                target = Rounded(card.gameObject, pal.Line, style.radius); // the outline...
                var inner = NewRect("Fill", card);
                float b = Mathf.Max(1f, k);
                Place(inner, b, b, w - b * 2, h - b * 2);
                var fill = Rounded(inner.gameObject, style.cardFill ? pal.Card : pal.Panel, Mathf.Max(0, style.radius - 1));
                fill.raycastTarget = false;
                target = fill;
                card.GetComponent<Image>().raycastTarget = true;
            }
            else
            {
                target = Rounded(card.gameObject, style.cardFill ? pal.Card : new Color(0, 0, 0, 0), style.radius);
            }
            if (style.divider)
            {
                var div = NewRect("Divider", card);
                Place(div, 0, h - Mathf.Max(1f, k), w, Mathf.Max(1f, k));
                var di = div.gameObject.AddComponent<Image>();
                di.color = pal.Line;
                di.raycastTarget = false;
            }

            float px = style.itemPadX * k, cx = px, right = w - px;
            if (!string.IsNullOrEmpty(style.prefix))
            {
                var pre = NewText(card, "Prefix", style.prefix, style.nameSize, pal.Accent, false, style.mono);
                Place(pre.rectTransform, cx, 0, pre.preferredWidth + 1, h);
                cx += pre.preferredWidth + 10f * k;
            }
            if (!string.IsNullOrEmpty(style.suffix))
            {
                var suf = NewText(card, "Suffix", style.suffix, style.nameSize, pal.Muted, false, style.mono);
                suf.alignment = TextAnchor.MiddleRight;
                Place(suf.rectTransform, right - suf.preferredWidth - 1, 0, suf.preferredWidth + 1, h);
                right -= suf.preferredWidth + 8f * k;
            }
            if (style.avatar)
            {
                float a = style.avatarSize * k;
                var av = NewRect("Avatar", card);
                Place(av, cx, (h - a) / 2, a, a);
                var tile = style.avatarRadius * 2 >= style.avatarSize ? av.gameObject.AddComponent<Image>() : Rounded(av.gameObject, Color.white, style.avatarRadius);
                if (tile.sprite == null) tile.sprite = Sprites.Circle;
                tile.color = pal.AvatarColor(index);
                tile.raycastTarget = false;
                var initial = NewText(av, "Initial", Initial(rec), style.avatarSize * 0.43f, Color.white, true, false);
                initial.alignment = TextAnchor.MiddleCenter;
                Stretch(initial.rectTransform);
                cx += a + (style.layout == "chips" ? 8f : 12f) * k;
            }

            float textW = Mathf.Max(0, right - cx);
            float nameH = style.nameSize * 1.35f * k, descH = style.descSize * 1.35f * k;
            string desc = !string.IsNullOrEmpty(rec.description) ? rec.description : string.Join(" · ", rec.categories ?? new string[0]);
            bool showDesc = style.descShow && !string.IsNullOrEmpty(desc);
            var name = NewText(card, "Name", DisplayName(rec), style.nameSize, style.nameAccent ? pal.Accent : pal.Text, style.nameBold, style.mono);
            if (style.descInline || !showDesc)
            {
                float nameW = showDesc ? Mathf.Min(name.preferredWidth, textW * 0.65f) : textW;
                Fit(name, nameW);
                Place(name.rectTransform, cx, 0, nameW, h);
                if (showDesc)
                {
                    float dx = cx + nameW + 10f * k;
                    var d = NewText(card, "Description", desc, style.descSize, pal.Muted, false, style.mono);
                    Fit(d, Mathf.Max(0, right - dx));
                    Place(d.rectTransform, dx, 0, Mathf.Max(0, right - dx), h);
                }
            }
            else
            {
                int lines = Mathf.Max(1, style.descLines);
                float block = nameH + 3f * k + descH * lines;
                float ty = Mathf.Max(0, (h - block) / 2);
                Fit(name, textW);
                Place(name.rectTransform, cx, ty, textW, nameH);
                var d = NewText(card, "Description", desc, style.descSize, pal.Muted, false, style.mono);
                d.alignment = TextAnchor.UpperLeft;
                d.horizontalOverflow = HorizontalWrapMode.Wrap;
                d.verticalOverflow = VerticalWrapMode.Truncate;
                Place(d.rectTransform, cx, ty + nameH + 3f * k, textW, descH * lines);
            }

            var button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = target;
            var colors = button.colors;
            colors.highlightedColor = colors.selectedColor = new Color(0.96f, 0.96f, 0.96f, 1);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1);
            button.colors = colors;
            var view = new CardView { card = rec, rect = card };
            button.onClick.AddListener(() => OnCardClicked(view));
            return view;
        }

        static string DisplayName(RecoCard rec) => string.IsNullOrEmpty(rec.name) ? rec.host : rec.name;

        // Shortens a one-line text with "…" until it fits.
        static void Fit(Text t, float width)
        {
            if (width <= 0 || t.preferredWidth <= width) return;
            string full = t.text;
            int lo = 0, hi = full.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                t.text = full.Substring(0, mid).TrimEnd() + "…";
                if (t.preferredWidth <= width) lo = mid; else hi = mid - 1;
            }
            t.text = lo > 0 ? full.Substring(0, lo).TrimEnd() + "…" : "…";
        }

        // Positions rt at (x, y) from the parent's top-left, w × h.
        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(Mathf.Max(0, w), Mathf.Max(0, h));
        }

        // A rounded rectangle of the given corner radius (points).
        Image Rounded(GameObject go, Color color, float radiusPt)
        {
            var img = go.AddComponent<Image>();
            img.sprite = Sprites.Rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = Sprites.RoundedRadius / Mathf.Max(0.5f, radiusPt * k);
            img.color = color;
            return img;
        }

        // ---- viewability ----

        void Update()
        {
            if (cards.Count == 0) return;
            tick += Time.unscaledDeltaTime;
            if (tick < 0.2f) return;
            float dt = tick;
            tick = 0f;
            bool focused = Application.isFocused || Application.isEditor && Application.isBatchMode;
            foreach (var c in cards)
            {
                if (seen.Contains(c.card.dataId)) continue;
                if (!focused || !IsHalfVisible(c.rect))
                {
                    c.visibleFor = 0f; // must be one continuous second
                    continue;
                }
                c.visibleFor += dt;
                if (c.visibleFor < 1f) continue;
                seen.Add(c.card.dataId);
                if (!hostSeen)
                {
                    hostSeen = true;
                    Track("impression");
                }
                Track("impression", c.card.dataId);
            }
        }

        void LateUpdate()
        {
            // Re-fit when the rectangle or the scale changes (rotation, resizing UI, another screen).
            if (data != null && content != null && ((Rect.rect.size - renderedSize).sqrMagnitude > 4f || Mathf.Abs(EffectiveScale() - renderedScale) > 0.05f)) Render(data);
        }

        internal bool IsHalfVisible(RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            var canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null || !canvas.enabled) return false;
            float alpha = 1f;
            foreach (var g in rt.GetComponentsInParent<CanvasGroup>())
            {
                alpha *= g.alpha;
                if (g.ignoreParentGroups) break;
            }
            if (alpha < 0.05f) return false;
            var root = canvas.rootCanvas;
            var cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            var view = new UnityEngine.Rect(0, 0, Screen.width, Screen.height);
            for (var p = rt.parent; p != null; p = p.parent)
            {
                if (p.GetComponent<RectMask2D>() != null || p.GetComponent<Mask>() != null) view = Intersect(view, ScreenRect((RectTransform)p, cam));
            }
            return RecoMath.VisibleFraction(ScreenRect(rt, cam), view) >= 0.5f;
        }

        static readonly Vector3[] corners = new Vector3[4];

        static UnityEngine.Rect ScreenRect(RectTransform rt, Camera cam)
        {
            rt.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var c in corners)
            {
                var s = RectTransformUtility.WorldToScreenPoint(cam, c);
                min = Vector2.Min(min, s);
                max = Vector2.Max(max, s);
            }
            return UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        static UnityEngine.Rect Intersect(UnityEngine.Rect a, UnityEngine.Rect b)
        {
            float xMin = Mathf.Max(a.xMin, b.xMin), yMin = Mathf.Max(a.yMin, b.yMin);
            return UnityEngine.Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin, Mathf.Min(a.xMax, b.xMax)), Mathf.Max(yMin, Mathf.Min(a.yMax, b.yMax)));
        }

        // ---- clicks ----

        void OnCardClicked(CardView view)
        {
            if (Time.unscaledTime - lastClick < 1f) return; // one tap, one visit
            lastClick = Time.unscaledTime;
            StartCoroutine(Click(view.card));
        }

        IEnumerator Click(RecoCard card)
        {
            var op = Track("click", card.dataId, RecoMath.NewClickId());
            // Give the report a moment to leave before the store takes over the screen.
            for (float t = 0f; op != null && !op.isDone && t < 0.6f; t += Time.unscaledDeltaTime) yield return null;
            OpenUrl?.Invoke(card.url);
        }

        // ---- reporting ----

        UnityWebRequestAsyncOperation Track(string type, string target = "", string clickId = "")
        {
            if (string.IsNullOrEmpty(receipt)) return null;
            Tracked?.Invoke(type, target);
            var body = new TrackBody
            {
                type = type,
                sourceDataId = dataId.Trim(),
                targetDataId = target,
                widgetSlots = shownCount,
                widgetVersion = WidgetVersion,
                receipt = receipt,
                // Views in the editor never earn points.
                automated = Application.isEditor,
                clickId = clickId,
                trusted = true,
            };
            return Send(JsonUtility.ToJson(body), 0);
        }

        UnityWebRequestAsyncOperation Send(string json, int attempt)
        {
            var req = new UnityWebRequest(Api("/api/track"), UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 10,
            };
            req.SetRequestHeader("Content-Type", "application/json");
            SetHeaders(req);
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                bool retry = attempt < 2 && (req.result == UnityWebRequest.Result.ConnectionError || req.responseCode >= 500 || req.responseCode == 429);
                req.Dispose();
                if (retry && this != null && isActiveAndEnabled) StartCoroutine(Retry(json, attempt + 1));
            };
            return op;
        }

        IEnumerator Retry(string json, int attempt)
        {
            yield return new WaitForSecondsRealtime(attempt * 2f);
            Send(json, attempt);
        }

        void SetHeaders(UnityWebRequest req)
        {
            // Our own user agent tells the server this is an app, not a browser.
            req.SetRequestHeader("User-Agent", "RecoVibesUnity/" + PackageVersion + " (" + Application.platform + "; Unity " + Application.unityVersion + ")");
            req.SetRequestHeader("Accept-Language", RecoMath.LanguageCode(Application.systemLanguage));
        }

        string Api(string path) => (string.IsNullOrWhiteSpace(apiBase) ? "https://api.recovibes.com" : apiBase.Trim().TrimEnd('/')) + path;

        // ---- UI helpers ----

        int Px(float points) => Mathf.Max(1, Mathf.RoundToInt(points * k));

        /// <summary>
        /// The multiplier for every size: <see cref="scale"/> when set; otherwise
        /// one point (1/160 inch) per unit on screen, from the device's DPI and
        /// how the canvas maps to it - never smaller than 1.
        /// </summary>
        internal float EffectiveScale()
        {
            if (scale > 0f) return scale;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null || canvas.rootCanvas.renderMode == RenderMode.WorldSpace) return 1f;
            float factor = canvas.rootCanvas.scaleFactor, dpi = Screen.dpi;
            if (factor <= 0f || dpi <= 0f) return 1f;
            return RecoMath.AutoScale(dpi, factor);
        }

        static string Initial(RecoCard c)
        {
            var s = (string.IsNullOrEmpty(c.name) ? c.host : c.name ?? "").Trim();
            return s.Length > 0 ? char.ToUpperInvariant(s[0]).ToString() : "?";
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        Text NewText(Transform parent, string name, string value, float sizePt, Color color, bool bold, bool mono)
        {
            var t = NewRect(name, parent).gameObject.AddComponent<Text>();
            t.font = mono && monoFont != null ? monoFont : font != null ? font : Sprites.DefaultFont;
            t.text = value;
            t.fontSize = Px(sizePt); // drawn at its real size: sharp at any canvas resolution
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.color = color;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; // taps go to the card behind the text
            return t;
        }

        static void DestroyNow(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        void EnsureEventSystem()
        {
            if (!Application.isPlaying || EventSystem.current != null || FindAnyEventSystem()) return;
            // Taps need an EventSystem; add one with whichever input module the project uses.
            var go = new GameObject("EventSystem", typeof(EventSystem));
            var inputSystem = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            go.AddComponent(inputSystem ?? typeof(StandaloneInputModule));
        }

        static bool FindAnyEventSystem()
        {
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<EventSystem>() != null;
#else
            return FindObjectOfType<EventSystem>() != null;
#endif
        }

        readonly struct Palette
        {
            public readonly Color Panel, Card, Line, Text, Muted, Accent, Bar;

            public Palette(RecoPalette p)
            {
                Panel = RecoMath.ParseColor(p.panel, Color.black);
                Card = RecoMath.ParseColor(p.card, Color.black);
                Line = RecoMath.ParseColor(p.line, Color.gray);
                Text = RecoMath.ParseColor(p.text, Color.white);
                Muted = RecoMath.ParseColor(p.muted, Color.gray);
                Accent = RecoMath.ParseColor(p.accent, new Color32(0x7e, 0x7e, 0xff, 0xff));
                Bar = RecoMath.ParseColor(p.bar, Color.black);
            }

            // Each card's avatar steps around the color wheel from the accent.
            public Color AvatarColor(int index)
            {
                Color.RGBToHSV(Accent, out float h, out float s, out float v);
                return Color.HSVToRGB(Mathf.Repeat(h + index * 47f / 360f, 1f), Mathf.Max(s, 0.45f), Mathf.Max(v, 0.75f));
            }
        }
    }

    /// <summary>Rounded shapes and the default font, generated once at runtime (no assets to import).</summary>
    static class Sprites
    {
        static Sprite rounded, circle;
        static Font defaultFont;

        public const float RoundedRadius = 24f;
        public static Sprite Rounded => rounded != null ? rounded : rounded = Make(96, (int)RoundedRadius, sliced: true);
        public static Sprite Circle => circle != null ? circle : circle = Make(96, 48, sliced: false);

        public static Font DefaultFont
        {
            get
            {
                if (defaultFont != null) return defaultFont;
                try { defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (ArgumentException) { }
                if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return defaultFont;
            }
        }

        static Sprite Make(int size, int radius, bool sliced)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance past the rounded corner, anti-aliased over one pixel.
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var border = sliced ? new Vector4(radius, radius, radius, radius) : Vector4.zero;
            var sprite = Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
