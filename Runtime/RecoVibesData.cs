using System;
using System.Text;
using UnityEngine;

namespace RecoVibes
{
    // Wire formats of the RecoVibes API (GET /api/widget/<dataId>, POST /api/track).
    [Serializable]
    public class RecoCard
    {
        public string dataId;
        public string name;
        public string url;
        public string host;
        public string description;
        public string[] categories;
    }

    [Serializable]
    public class RecoDesign
    {
        public string theme;   // "", "auto", "light" or "dark"
        public string accent;  // #rrggbb or ""
        public string heading; // "" = localized default
        public bool hideHeading;
        public string lang;
    }

    [Serializable]
    public class RecoResponse
    {
        public string sourceDataId;
        public bool paused;
        public string receipt;
        public RecoCard[] recommendations;
        public RecoDesign widget;
        public RecoStyle native; // how to draw the owner's design (null from older servers)
    }

    /// <summary>Colors as #RRGGBBAA.</summary>
    [Serializable]
    public class RecoPalette
    {
        public string panel = "#111116ff", card = "#1b1b22ff", line = "#ffffff24", text = "#f2f2f5ff", muted = "#f2f2f5ad", accent = "#7e7effff", bar = "#161b22ff", pressed = "#ffffff14";
    }

    /// <summary>
    /// The owner's dashboard design resolved by the server into drawing
    /// instructions (sizes in points). The SDK draws whatever this says, so
    /// templates change on the server without a new package.
    /// </summary>
    [Serializable]
    public class RecoStyle
    {
        public int version;
        public string template = "", theme = "";
        public int slots;
        public string layout = "grid";
        public int maxColumns = 4;
        public float minWidth = 180, itemHeight = 66, gap = 10, rowGap = 10, padding = 14, itemPadX = 14, radius = 10, panelRadius = 14;
        public bool cardFill = true, border = true, divider;
        public bool avatar;
        public float avatarSize, avatarRadius;
        public float nameSize = 15;
        public bool nameBold = true, nameAccent;
        public bool descShow = true;
        public float descSize = 12.5f;
        public int descLines = 2;
        public bool descInline;
        public string prefix = "", suffix = "";
        public bool mono;
        public bool headingShow = true;
        public string headingText = "";
        public float headingSize = 12;
        public bool headingUppercase = true, headingBar;
        public RecoPalette light = new RecoPalette
        {
            panel = "#f5f5f7ff", card = "#ffffffff", line = "#0000001f", text = "#17171cff", muted = "#17171cad", bar = "#eaeef2ff", pressed = "#0000000d",
        };
        public RecoPalette dark = new RecoPalette();

        /// <summary>The classic look, for servers that don't send a style yet.</summary>
        public static RecoStyle Classic(RecoDesign d)
        {
            var s = new RecoStyle { version = 1, template = "classic" };
            if (d != null)
            {
                s.theme = d.theme == "light" || d.theme == "dark" ? d.theme : "";
                s.headingShow = !d.hideHeading;
                s.headingText = d.heading ?? "";
                if (!string.IsNullOrEmpty(d.accent)) s.light.accent = s.dark.accent = d.accent + "ff";
            }
            return s;
        }
    }

    // Every field is one the server knows; empty ones are ignored there.
    [Serializable]
    internal class TrackBody
    {
        public string type;
        public string sourceDataId;
        public string targetDataId;
        public int widgetSlots;
        public string widgetVersion;
        public string receipt;
        public bool automated;
        public string clickId;
        public bool trusted;
    }

    /// <summary>FromDashboard follows the template; Vertical forces one column; Horizontal one row.</summary>
    public enum RecoLayout { Vertical, Horizontal, FromDashboard }

    /// <summary>Pure helpers, kept apart from MonoBehaviour code so they can be unit tested.</summary>
    public static class RecoMath
    {
        public const int MaxSlots = 12;

        /// <summary>How many cards fit in the space given: rows for a vertical list, columns for a row.</summary>
        public static int AutoSlots(RecoLayout layout, float width, float height, bool heading, float cardHeight, float minCardWidth, float gap, float headingHeight)
        {
            int n;
            if (layout == RecoLayout.Vertical)
            {
                float room = height - (heading ? headingHeight + gap : 0f);
                n = Mathf.FloorToInt((room + gap) / (cardHeight + gap));
            }
            else
            {
                n = Mathf.FloorToInt((width + gap) / (minCardWidth + gap));
            }
            return Mathf.Clamp(n, 1, MaxSlots);
        }

        /// <summary>
        /// Size multiplier so one design unit is one point (1/160 inch) on screen:
        /// screen pixels per point divided by canvas pixels per unit. At least 1
        /// (desktop monitors), at most 6.
        /// </summary>
        public static float AutoScale(float dpi, float canvasScaleFactor)
        {
            if (dpi <= 0f || canvasScaleFactor <= 0f) return 1f;
            return Mathf.Clamp(dpi / 160f / canvasScaleFactor, 1f, 6f);
        }

        /// <summary>Fraction of <paramref name="item"/>'s area inside <paramref name="viewport"/> (both in screen space).</summary>
        public static float VisibleFraction(Rect item, Rect viewport)
        {
            float area = item.width * item.height;
            if (area <= 0f) return 0f;
            float w = Mathf.Min(item.xMax, viewport.xMax) - Mathf.Max(item.xMin, viewport.xMin);
            float h = Mathf.Min(item.yMax, viewport.yMax) - Mathf.Max(item.yMin, viewport.yMin);
            if (w <= 0f || h <= 0f) return 0f;
            return Mathf.Clamp01(w * h / area);
        }

        /// <summary>Two-letter code for the Accept-Language header.</summary>
        public static string LanguageCode(SystemLanguage lang)
        {
            switch (lang)
            {
                case SystemLanguage.Hebrew: return "he";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.Arabic: return "ar";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional: return "zh";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.Dutch: return "nl";
                case SystemLanguage.Turkish: return "tr";
                case SystemLanguage.Polish: return "pl";
                default: return "en";
            }
        }

        /// <summary>Default heading. Only scripts Unity's built-in font draws correctly are localized.</summary>
        public static string Heading(string lang)
        {
            switch (lang)
            {
                case "es": return "También te puede gustar";
                case "fr": return "Vous aimerez aussi";
                case "de": return "Das könnte dir auch gefallen";
                case "pt": return "Você também pode gostar";
                case "it": return "Potrebbe piacerti anche";
                case "ru": return "Вам также может понравиться";
                default: return "You might also like";
            }
        }

        /// <summary>A one-time click id: 32 lowercase hex characters.</summary>
        public static string NewClickId(System.Random rng = null)
        {
            var bytes = new byte[16];
            if (rng != null) rng.NextBytes(bytes);
            else
            {
                using (var crypto = System.Security.Cryptography.RandomNumberGenerator.Create()) crypto.GetBytes(bytes);
            }
            var sb = new StringBuilder(32);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }

        public static RecoResponse ParseResponse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var r = JsonUtility.FromJson<RecoResponse>(json);
                if (r != null && r.recommendations == null) r.recommendations = new RecoCard[0];
                return r;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
