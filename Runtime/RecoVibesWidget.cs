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
        public const string PackageVersion = "1.1.0";

        public enum ThemeMode { FromDashboard, Light, Dark }

        [Tooltip("Your app's ID from the RecoVibes dashboard (Install tab), e.g. rv_ab12cd34.")]
        public string dataId = "";

        [Tooltip("How many recommendations to show. 0 = as many as fit in this rectangle.")]
        [Range(0, RecoMath.MaxSlots)] public int slots = 0;

        [Tooltip("Vertical: a list of cards. Horizontal: cards side by side.")]
        public RecoLayout layout = RecoLayout.Vertical;

        [Tooltip("FromDashboard uses the theme from your RecoVibes design settings (dark when it's Auto).")]
        public ThemeMode theme = ThemeMode.FromDashboard;

        [Tooltip("Optional: your game's font. Empty = Unity's built-in font.")]
        public Font font;

        [Tooltip("Card height in the vertical layout, in canvas units.")]
        public float cardHeight = 76f;

        [Tooltip("Smallest card width in the horizontal layout, in canvas units.")]
        public float minCardWidth = 200f;

        [Tooltip("Space between cards, in canvas units.")]
        public float spacing = 10f;

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

        const float Padding = 12f;
        const float HeadingHeight = 20f;
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
            bool dark = theme == ThemeMode.Dark || (theme == ThemeMode.FromDashboard && design.theme != "light");
            var pal = new Palette(dark, RecoMath.ParseColor(design.accent, new Color32(0x7e, 0x7e, 0xff, 0xff)));
            bool heading = !design.hideHeading;

            var size = renderedSize - new Vector2(Padding * 2, Padding * 2) * k;
            int fit = slots > 0 ? slots : RecoMath.AutoSlots(layout, size.x, size.y, heading, cardHeight * k, minCardWidth * k, spacing * k, HeadingHeight * k);
            shownCount = Mathf.Min(fit, recs.Length);

            content = NewRect("RecoVibes", Rect);
            Stretch(content);
            var panel = content.gameObject.AddComponent<Image>();
            panel.sprite = Sprites.Rounded;
            panel.type = Image.Type.Sliced;
            panel.pixelsPerUnitMultiplier = 1f / k; // corners grow with the scale
            panel.color = pal.Panel;

            // Fixed placement: heading pinned to the top, cards fill the rest.
            if (heading) BuildHeading(content, string.IsNullOrEmpty(design.heading) ? RecoMath.Heading(design.lang) : design.heading, pal);

            var list = NewRect("Cards", content);
            Stretch(list);
            list.offsetMin = new Vector2(Padding, Padding) * k;
            list.offsetMax = new Vector2(-Padding, -(Padding + (heading ? HeadingHeight + spacing : 0f))) * k;
            HorizontalOrVerticalLayoutGroup group = layout == RecoLayout.Vertical
                ? (HorizontalOrVerticalLayoutGroup)list.gameObject.AddComponent<VerticalLayoutGroup>()
                : list.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing * k;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = layout == RecoLayout.Horizontal;
            group.childAlignment = TextAnchor.UpperLeft;

            for (int i = 0; i < shownCount; i++) cards.Add(BuildCard(list, recs[i], i, pal));

            EnsureEventSystem();
            Rendered?.Invoke(shownCount);
        }

        void BuildHeading(RectTransform parent, string title, Palette pal)
        {
            var row = NewRect("Heading", parent);
            row.anchorMin = new Vector2(0, 1);
            row.anchorMax = Vector2.one;
            row.pivot = new Vector2(0.5f, 1);
            row.offsetMin = new Vector2(Padding, -(Padding + HeadingHeight)) * k;
            row.offsetMax = new Vector2(-Padding, -Padding) * k;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;

            var t = NewText("Title", row, title.ToUpperInvariant(), 13, pal.Muted, FontStyle.Bold);
            t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var by = NewText("By RecoVibes", row, "by RecoVibes", 12, pal.Muted, FontStyle.Normal);
            by.alignment = TextAnchor.MiddleRight;
            by.raycastTarget = true;
            var button = by.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OpenUrl?.Invoke(AttributionUrl));
        }

        CardView BuildCard(RectTransform parent, RecoCard rec, int index, Palette pal)
        {
            var card = NewRect("Card " + (index + 1), parent);
            var bg = card.gameObject.AddComponent<Image>();
            bg.sprite = Sprites.Rounded;
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1f / k;
            bg.color = pal.Card;
            var le = card.gameObject.AddComponent<LayoutElement>();
            if (layout == RecoLayout.Vertical) le.preferredHeight = cardHeight * k;
            else
            {
                // Equal widths: ignore what the text would like, share the row.
                le.minWidth = 0;
                le.preferredWidth = 1;
                le.flexibleWidth = 1;
            }

            var row = card.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(Px(12), Px(12), Px(10), Px(10));
            row.spacing = 12 * k;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            // Avatar: the name's first letter on an accent-hued circle.
            var avatar = NewRect("Avatar", card);
            var avLe = avatar.gameObject.AddComponent<LayoutElement>();
            avLe.preferredWidth = avLe.preferredHeight = avLe.minWidth = avLe.minHeight = 44 * k;
            var circle = avatar.gameObject.AddComponent<Image>();
            circle.sprite = Sprites.Circle;
            circle.color = pal.AvatarColor(index);
            circle.raycastTarget = false;
            var initial = NewText("Initial", avatar, Initial(rec), 20, Color.white, FontStyle.Bold);
            initial.alignment = TextAnchor.MiddleCenter;
            Stretch(initial.rectTransform);

            var body = NewRect("Text", card);
            body.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var col = body.gameObject.AddComponent<VerticalLayoutGroup>();
            col.childControlWidth = col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
            col.childAlignment = TextAnchor.MiddleLeft;
            col.spacing = 2 * k;

            var name = NewText("Name", body, string.IsNullOrEmpty(rec.name) ? rec.host : rec.name, 16, pal.Fg, FontStyle.Bold);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.verticalOverflow = VerticalWrapMode.Truncate;
            name.gameObject.AddComponent<LayoutElement>().preferredHeight = 21 * k;
            string desc = !string.IsNullOrEmpty(rec.description) ? rec.description : string.Join(" · ", rec.categories ?? new string[0]);
            if (!string.IsNullOrEmpty(desc))
            {
                var d = NewText("Description", body, desc, 13, pal.Muted, FontStyle.Normal);
                d.horizontalOverflow = HorizontalWrapMode.Wrap;
                d.verticalOverflow = VerticalWrapMode.Truncate;
                d.gameObject.AddComponent<LayoutElement>().preferredHeight = Mathf.Max(18, cardHeight - 46) * k;
            }

            var button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            var colors = button.colors;
            colors.highlightedColor = colors.selectedColor = new Color(1, 1, 1, 0.92f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1);
            button.colors = colors;
            var view = new CardView { card = rec, rect = card };
            button.onClick.AddListener(() => OnCardClicked(view));
            return view;
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

        Text NewText(string name, Transform parent, string value, int size, Color color, FontStyle style)
        {
            var t = NewRect(name, parent).gameObject.AddComponent<Text>();
            t.font = font != null ? font : Sprites.DefaultFont;
            t.text = value;
            t.fontSize = Px(size); // drawn at its real size: sharp at any canvas resolution
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleLeft;
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
            public readonly Color Panel, Card, Fg, Muted, Accent;

            public Palette(bool dark, Color accent)
            {
                Accent = accent;
                Panel = dark ? new Color32(0x12, 0x12, 0x15, 0xff) : new Color32(0xf3, 0xf3, 0xf5, 0xff);
                Card = dark ? new Color32(0x1e, 0x1e, 0x23, 0xff) : new Color32(0xff, 0xff, 0xff, 0xff);
                Fg = dark ? new Color32(0xf2, 0xf2, 0xf4, 0xff) : new Color32(0x14, 0x14, 0x18, 0xff);
                Muted = dark ? new Color32(0x9a, 0x9a, 0xa6, 0xff) : new Color32(0x6b, 0x6b, 0x76, 0xff);
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

        public static Sprite Rounded => rounded != null ? rounded : rounded = Make(48, 12, sliced: true);
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
