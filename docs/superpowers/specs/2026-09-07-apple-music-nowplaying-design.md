# Apple Music Now Playing Style — Design Spec (v2 — Faithful to SimpMusic)

## Goal

Replicate SimpMusic's Apple Music Now Playing style as faithfully as WP8.1 allows. This means matching the **layout structure**, **color system**, **component hierarchy**, and **visual language** 1:1 — not a "simplified" or "inspired" adaptation.

## Layout Architecture

SimpMusic's Apple Music layout uses a **Dock-based view switching** system (not the current Pivot). Three views — MAIN, LYRICS, QUEUE — are selected via a bottom **Dock bar** (3 circular buttons: Lyrics / [Cast] / Queue). All three views share an identical **BottomCluster** at the bottom.

### Existing vs Apple Music layout comparison

```
┌─ CURRENT (Spotify-style) ─────┐     ┌─ APPLE MUSIC (target) ─────────┐
│ [↓ Close]  NOW PLAYING  [⋯]  │     │                                │
│            ● ● ●              │     │     ──── (grabber bar) ────     │
│                               │     │                                │
│  ┌── Pivot (swipe) ─────────┐ │     │  ┌── View body ──────────────┐ │
│  │  Player / Lyrics / Queue │ │     │  │  MAIN: artwork (60%) +    │ │
│  └──────────────────────────┘ │     │  │    title row               │ │
│                               │     │  │  LYRICS: compact header +  │ │
│  [MiniLyric] Title / ♡ / Q   │     │  │    scrollable lyrics       │ │
│  ════ Slider ════             │     │  │  QUEUE: compact header +   │ │
│  00:00            -03:30      │     │  │    pills + queue list      │ │
│  🔀 ⏮ [▶] ⏭ 🔁             │     │  └────────────────────────────┘ │
└───────────────────────────────┘     │                                │
                                      │  ═══ BottomCluster ══════════  │
                                      │  ──── ThinSlider (no thumb) ── │
                                      │  00:00   [AAC badge]   -03:30  │
                                      │     ⏪     ▶(plain white)  ⏩  │
                                      │  🔉 ════ Volume Slider ════ 🔊 │
                                      │     [Lyrics] [Cast] [Queue]    │
                                      └────────────────────────────────┘
```

---

## 1. Background Layer — Frosted Artwork Backdrop

### SimpMusic behavior (lines 232-262)
1. Album art loaded full-size, displayed with `blur(80.dp, Unbounded)` covering the entire screen
2. A semi-transparent gradient wash sits on top: `alpha(0.62f).background(backdropBrush)`
3. `backdropBrush` is a 3-stop vertical gradient derived from the dominant (seed) color:
   - `0.00`: `lerp(seedColor, Black, 0.05)` — barely darkened
   - `0.48`: `lerp(seedColor, Black, 0.32)` — moderately dark
   - `1.00`: `lerp(seedColor, Black, 0.78)` — heavily darkened

### WP8.1 implementation
- Use **Lumia Imaging SDK** `BlurFilter(80)` on a 120×200 bitmap for the frosted backdrop
- `Image x:Name="AppleMusicBackdrop"` stretches to fill (GPU-scale via `Stretch="UniformToFill"`)
- Wash Rectangle on top with `Opacity="0.62"` and a `LinearGradientBrush` using the seed-tinted 3-stop gradient
- Seed color = existing `ExtractDominantColorAsync()` result (skip `AdjustAmbientColor` clamp — Apple Music uses brighter colors than Spotify mode)

### LumiaBlurHelper service

```csharp
// Services/LumiaBlurHelper.cs
internal static class LumiaBlurHelper
{
    static async Task<WriteableBitmap> RenderBlurredAsync(
        Stream source, int targetWidth, int targetHeight, int kernelSize);
}
```

Pipeline: `StreamImageSource(stream)` → `FilterEffect { BlurFilter(80) }` → `WriteableBitmapRenderer(120×200)` → GPU-scale.
LRU cache: max 3 entries keyed by thumbnail URL.

---

## 2. MAIN View — Full-Bleed Artwork + Title Row + BottomCluster

### SimpMusic behavior (lines 372-735)
- Artwork fills the **top ~60%** of the screen (dynamic: `screenHeight - bottomContentHeight`)
- Artwork fades at the bottom via a 300dp alpha mask (`appleMusicVerticalFadeEdges(topFade=0, bottomFade=300dp)`) dissolving into the page gradient
- **Title row** below artwork: `[Title (marquee)] [Artist]` on left, `[⊕] [☆] [⋯]` actions on right
- **BottomCluster** below title row (fixed, shared with all views)

### WP8.1 implementation
- Replace the current 300×300 centered album art with a **full-width artwork Image** spanning the top portion of the view
- Bottom of artwork uses a gradient overlay Rectangle (transparent → seed-tinted dark) to create the dissolve effect
- Title row: reuse existing `BigTitle` (marquee) + `BigArtist`, but reposition below the artwork area. Add `BigHeartBtn` and menu button inline.

### WP8.1 limitation: No per-element alpha mask
SimpMusic's `appleMusicVerticalFadeEdges` uses `CompositingStrategy.Offscreen` + `BlendMode.DstIn`. WinRT XAML has no equivalent. **Substitute:** A gradient-filled Rectangle overlaying the bottom of the artwork, transitioning from transparent to the page gradient's bottom color. This is visually identical at blur level 80.

---

## 3. BottomCluster — Shared Across All Views

SimpMusic's `AppleMusicBottomCluster` (lines 761-818) renders identically in MAIN, LYRICS, and QUEUE:

### 3.1 ThinSlider (progress bar)
- **No thumb knob** — track-only bar
- Default height: 7dp, **springs to 14dp on touch/drag** (animated)
- Active portion: white (`#EBFFFFFF` = 92% white)
- Inactive track: `#42FFFFFF` (26% white)
- Enclosing Box: fixed 18dp height (prevents layout jump during spring)

### WP8.1 implementation
The existing `ModernSliderStyle` has a thumb. Create a new `AppleMusicSliderStyle` that:
- Hides the thumb (width/height = 0)
- Uses white fill color (not green)
- Sets track height to 7px
- WP8.1 XAML sliders cannot spring-animate height. **Substitute:** Keep the thin track at fixed 7dp. The touch target remains the full Slider control height.

### 3.2 TimesRow
- `elapsed` left, `[codec badge]` center, `-remaining` right
- Remaining shown as negative: `-3:30`
- Codec badge: pill with translucent background (`#29FFFFFF`), GraphicEq icon + codec text (e.g. "AAC")

### WP8.1 implementation
- Change time format to show remaining as `-mm:ss` on right (currently shows total)
- Codec badge: WP8.1 `BackgroundMediaPlayer` doesn't expose codec info. **Substitute:** Omit the badge. Show only elapsed and remaining times.

### 3.3 TransportRow
- **⏪ FastRewind** (46dp icon in 56dp touch target)
- **▶/⏸ Play/Pause** (66dp icon in 76dp touch target) — **plain white, NO container disc/circle**
- **⏩ FastForward** (46dp icon in 56dp touch target)
- Spacing: `spacedBy(58dp, centered)` — NOT spread-evenly
- Disabled buttons: 40% white alpha

### WP8.1 implementation
- Remove the green circle (`#1DB954` Border with CornerRadius=32) from Play/Pause
- Use plain white Play/Pause icon at 50dp in 60dp button (scaled for WP8.1 screen density)
- Prev/Next icons at 36dp in 48dp buttons
- Center-aligned with fixed spacing (not Grid columns stretching evenly)
- **No shuffle/repeat in transport row** — those move to the Queue pills

### 3.4 VolumeRow
- `🔉` (18dp) — ThinSlider (same style as progress) — `🔊` (18dp)
- Icon tint: 72% white (`AppleMusicTextSecondary`)

### WP8.1 implementation
- WP8.1 has hardware volume rocker, but SimpMusic shows this row. **Include it** for visual fidelity.
- Use a second Slider with the same AppleMusicSliderStyle
- Volume control via `MediaElement.Volume` or system volume API
- Icons: Segoe UI Symbol volume glyphs at 18dp, tinted `#B8FFFFFF`

### 3.5 Dock (view switcher)
- 3 circular buttons: **Lyrics** / **[Cast]** / **Queue**
- Each: 40dp circle, 22dp icon
- Active state: light background (seed-derived `lerp(seedColor, White, 0.75)`) + dark glyph (`lerp(seedColor, Black, 0.6)`)
- Inactive: transparent bg + 85% white glyph
- Re-tapping active tab returns to MAIN (toggle behavior)

### WP8.1 implementation
- Replace the current top dot indicators (`DotPlayer/DotLyrics/DotQueue`) with bottom Dock buttons
- 3 Borders with CornerRadius=20, each containing a Segoe UI Symbol icon
- Tapped event toggles between views using existing Pivot (programmatic `SelectedIndex` change, headers remain hidden)
- Active/inactive colors computed from seed color using the same `lerp` formula
- Cast button: WP8.1 has no Miracast API. **Omit Cast** — show only 2 dock buttons (Lyrics + Queue)

---

## 4. LYRICS View

### SimpMusic behavior (AppleMusicLyricsView.kt)
1. **Compact header**: 55dp album art thumbnail + title (labelSmall size) + artist (bodySmall) + `[⊕][☆][⋯]` actions
2. **Lyrics body**: full-page scrollable lyrics with depth-of-field focus effect
3. **Auto-hide cluster**: BottomCluster shows on touch/scroll, auto-hides after 8 seconds
4. **Floating action buttons**: bottom-right, 38dp circles with 24% white bg: Share, Fullscreen, Vote
5. **Footer**: sync type text + provider text, right-aligned at bottom of lyrics list

### Depth-of-field lyrics (AppleMusicLyricsLines.kt)
- **All lines same font size** (28sp) — NOT scaled like current Spotify mode
- Active line: **white `#FFFFFF`**, alpha 1.0
- Inactive line: **grey `#9B9B9B`** (a real grey color, not white-with-reduced-alpha)
- Alpha falloff: 0.25 per line distance, floor 0.25
- Already-sung lines get +1 distance (recede faster)
- Pre-roll (no active line yet): all lines at 0.6 alpha
- **Blur per line**: `fontSizeDp × min(distance × 0.095, 0.45)` — sung lines blur with distance

### WP8.1 implementation
- **Compact header**: New XAML row at top of lyrics PivotItem with small Image + TextBlocks + action buttons
- **Lyrics colors**: Change `ColorBrush` to `#9B9B9B` for inactive, `#FFFFFF` for active (currently uses dimmed accent)
- **Font size**: Set all lines to the same configured font size (no size variation between active/inactive)
- **Alpha falloff**: Apply via `Opacity` property on each ListViewItem container: `max(0.25, 1.0 - |distance| × 0.25)` with +1 penalty for past lines
- **No blur**: WinRT has no per-element blur. Alpha + grey color provides 80% of the visual effect. This is an acceptable trade.
- **Auto-hide cluster**: Timer-based. On lyrics view entry, show BottomCluster. On scroll/tap, restart 8s timer. On timer expire, animate cluster out (`Opacity` 1→0, `Height` → 0). The freed space lets lyrics list grow.
- **Floating buttons**: Fullscreen button retained (existing). Share/Vote omitted (not implemented in current app).
- **Lyrics bottom fade**: Existing gradient overlay Rectangle, updated to use Apple Music tinted colors

---

## 5. QUEUE View

### SimpMusic behavior (AppleMusicQueueView.kt)
1. **Compact header** (same as Lyrics)
2. **Pills row**: 4 rounded-rect buttons: `[Info] [PlaylistAdd] [Shuffle] [Repeat]`
   - 40dp height, RoundedCornerShape(20dp)
   - Active: seed-derived light bg + dark icon; Inactive: 24% white bg + white icon
3. **"Continue Playing" header**: "Now Playing" label + playlist name + Endless Queue switch
4. **Queue list**: `SongFullWidthItems` rows with long-press drag-to-reorder
5. **BottomCluster** (always visible, unlike Lyrics)
6. Top/bottom fade edges on queue list (24dp top, 48dp bottom)

### WP8.1 implementation
- **Compact header**: Same component as Lyrics view header
- **Pills row**: 4 Borders with rounded corners. Shuffle + Repeat migrate from current transport row. Info → tap opens track info dialog (existing). PlaylistAdd → existing `AddToPlaylistDialog`.
- **Queue list**: Reuse existing `QueueListView` + `QueueItemTemplate`. No drag-to-reorder (WP8.1 ListView limitation). Show existing "Clear" button instead.
- **"Continue Playing" header**: Show "Now Playing" label + autoplay toggle (existing `AutoplayToggle` concept, repositioned)
- **Endless Queue switch**: Map to existing Autoplay setting
- **BottomCluster**: Always visible (no auto-hide like Lyrics view)
- **Queue fade edges**: Top/bottom gradient overlay Rectangles

---

## 6. Grabber Bar (dismiss handle)

SimpMusic uses a **36×5dp rounded bar** at top center, `35% white` on tap to dismiss.

### WP8.1 implementation
- Replace the current `[↓ Close]` chevron button + `NOW PLAYING` text + `[⋯]` with:
  - A small rounded Rectangle (36×5) at top center for visual consistency
  - The `[↓ Close]` button remains functional but repositioned (or keep tap-on-grabber to close)
  - The `[⋯]` menu button moves to the title row's `AppleMusicHeaderActions`

---

## 7. Color System

All colors derived from dominant/seed color at runtime:

| Token | SimpMusic Value | Usage |
|-------|----------------|-------|
| `backdropBrush top` | `lerp(seed, Black, 0.05)` | Top of page gradient |
| `backdropBrush mid@48%` | `lerp(seed, Black, 0.32)` | Mid of page gradient |
| `backdropBrush bot` | `lerp(seed, Black, 0.78)` | Bottom of page gradient |
| `BACKDROP_TINT_ALPHA` | `0.62` | Opacity of gradient wash over blur |
| `activePillContainer` | `lerp(seed, White, 0.75)` | Active dock/pill bg |
| `activePillContent` | `lerp(seed, Black, 0.6)` | Active dock/pill icon |
| `TextSecondary` | `White @ 72%` = `#B8FFFFFF` | Volume icons, times text |
| `PillInactive` | `White @ 24%` = `#3DFFFFFF` | Inactive pill bg |
| `TrackInactive` | `White @ 26%` = `#42FFFFFF` | Slider inactive track |
| `TrackActive` | `White @ 92%` = `#EBFFFFFF` | Slider active track |
| `InactiveLineColor` | `#9B9B9B` | Lyrics inactive line |
| `Active line` | `#FFFFFF` | Lyrics active line |

### lerp implementation for WP8.1

```csharp
static Windows.UI.Color LerpColor(Windows.UI.Color from, Windows.UI.Color to, double t)
{
    return Windows.UI.Color.FromArgb(255,
        (byte)(from.R + (to.R - from.R) * t),
        (byte)(from.G + (to.G - from.G) * t),
        (byte)(from.B + (to.B - from.B) * t));
}
```

---

## 8. Settings Toggle

ComboBox in a new **Appearance** section in Settings:
- "Default (Dynamic Gradient)" — current behavior
- "Apple Music (Blurred Artwork)" — this spec

Saved as `NowPlayingStyle` in `LocalSettings`. Style applied on next `MiniPlayer_Tapped` open (not hot-swapped mid-session).

---

## 9. Status Bar

Use the seed color directly (same `lerp(seed, Black, 0.05)` as the gradient top) for status bar background. Existing `UpdateStatusBarColor()` + `AnimateStatusBarColorAsync()` work unchanged — just feed a different target color based on mode.

---

## 10. WP8.1 Adaptations Summary

| SimpMusic Feature | WP8.1 Adaptation | Reason |
|-------------------|-------------------|--------|
| `Modifier.blur()` on artwork | Lumia Imaging `BlurFilter` pre-render | No runtime XAML blur |
| `Modifier.blur()` on lyrics text | Alpha + grey color falloff only | No per-element blur in WinRT |
| `appleMusicVerticalFadeEdges` (DstIn mask) | Gradient overlay Rectangles | No compositing blend modes |
| ThinSlider spring animation (7→14dp) | Fixed 7dp track | No Slider track height animation |
| `appleMusicPressInflate` spring scale | Omit | Complex per-element spring, minimal visual impact |
| Cast dock button | Omit | No Miracast API on WP8.1 |
| Codec badge pill | Omit | No codec info from BackgroundMediaPlayer |
| Drag-to-reorder queue | Omit | ListView reorder unreliable on WP8.1 |
| HorizontalPager (artwork swipe) | No horizontal swipe on artwork | Pivot already handles view switching |
| Canvas/video backdrop | Omit | No MediaElement compositing |
| Crossfade between MAIN/LYRICS/QUEUE | Pivot handles transitions | Native Pivot slide animation |
| `Endless Queue` switch | Map to existing Autoplay toggle | Same concept |

---

## 11. File Change Summary

| File | Change |
|------|--------|
| `Services/LumiaBlurHelper.cs` | **NEW** — Blur rendering helper |
| `MainPage.xaml` | Major restructure of NowPlayingView: new background layer, full-bleed artwork, BottomCluster layout, Dock bar, compact headers, Apple Music slider style, grabber bar. Add Appearance section in Settings. |
| `Pages/MainPage.Playback.cs` | Branch `UpdateNowPlayingGradient()` on style; add blur backdrop update; add `LerpColor()`; modify `AnimateGradientTo()` for Apple Music color system |
| `Pages/MainPage.NowPlaying.cs` | Add `ApplyNowPlayingStyle()`, dock button handlers, auto-hide cluster timer, compact header update, transport reconfig |
| `Pages/MainPage.Lyrics.cs` | Apple Music lyrics styling: uniform font size, `#9B9B9B` inactive color, alpha falloff with distance penalty |

**Estimated new/modified code:** ~500-600 lines across all files.
