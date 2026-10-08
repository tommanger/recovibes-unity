# RecoVibes for Unity

Show recommendations from the RecoVibes network inside your game, and get your game recommended in others. The cards are drawn with Unity UI, so they sit on any screen and look like part of your game.

## Install

1. In Unity, open **Window → Package Manager**, press **+**, choose **Add package from git URL…**
2. Paste `https://github.com/tommanger/recovibes-unity.git` and press **Add**.

Needs Unity 2021.3 or newer and Unity UI (built in).

## Use

1. Right-click in the Hierarchy → **UI → RecoVibes Recommendations**. (Or add the **RecoVibes Recommendations** component to any UI object under a Canvas.)
2. Paste your app's **Data Id** from the RecoVibes dashboard (your app → Install tab).
3. Size the rectangle where you want recommendations. The widget fills it with as many cards as fit, or set **Slots** for a fixed number.

From code:

```csharp
var go = new GameObject("RecoVibes", typeof(RectTransform));
go.transform.SetParent(myPanel, false);
var widget = go.AddComponent<RecoVibes.RecoVibesWidget>();
widget.dataId = "rv_xxxxxxxx";
```

## How it counts

- A card counts as viewed once half of it has been on screen for a second while your game has focus. Hidden objects, zero-alpha Canvas Groups and masked-out cards don't count.
- Each time the widget is enabled counts as a new screen view.
- A tap reports the click, then opens the app's store page (App Store on iPhone, Google Play on Android). To route links yourself, set `RecoVibesWidget.OpenUrl`.
- Views in the Unity editor never earn points. Your app earns points once its store listing is verified in the dashboard.

## Options

| Field | What it does |
| --- | --- |
| Slots | 0 = as many as fit; otherwise a fixed number (up to 12) |
| Layout | Vertical list or Horizontal row |
| Theme | From your dashboard design, or Light / Dark |
| Font | Your game's font (default: Unity's built-in font) |
| Card Height / Min Card Width / Spacing | Card sizing, in canvas units |

Right-to-left scripts (Hebrew, Arabic) need a font and text setup that supports them; Unity's legacy Text doesn't shape them.
