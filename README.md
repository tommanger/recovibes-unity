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

## Design

The widget draws the design you pick in the dashboard (template, corners, theme, accent, heading, number of cards). Change it there and every copy of your game shows the new look on its next load - no new build, no package update.

## Sizing

The widget sizes itself for the device: on a 1080×1920 canvas it grows text, cards and spacing so they read at a normal size on the phone. **Don't scale the object's transform to make it bigger** - Unity would draw the text small and stretch it, so it looks blurry. Use **Scale** instead.

## How it counts

- A card counts as viewed once half of it has been on screen for a second while your game has focus. Hidden objects, zero-alpha Canvas Groups and masked-out cards don't count.
- Each time the widget is enabled counts as a new screen view.
- A tap reports the click, then opens the app's store page (App Store on iPhone, Google Play on Android). To route links yourself, set `RecoVibesWidget.OpenUrl`.
- Views in the Unity editor never earn points. Your app earns points once its store listing is verified in the dashboard.

## Options

| Field | What it does |
| --- | --- |
| Slots | 0 = the dashboard's setting (or as many as fit); otherwise a fixed number (up to 12) |
| Layout | From Dashboard (the template's layout), or force a Vertical list / Horizontal row |
| Theme | From your dashboard design, or Light / Dark |
| Font | Your game's font (default: Unity's built-in font) |
| Mono Font | A monospaced font for the Terminal template (default: the font above) |
| Scale | 0 = Auto: everything sized in real points for the device, whatever your canvas resolution (e.g. ×2.6 on a 1080-wide canvas on a phone). Or set a multiplier yourself. |

Right-to-left scripts (Hebrew, Arabic) need a font and text setup that supports them; Unity's legacy Text doesn't shape them.
