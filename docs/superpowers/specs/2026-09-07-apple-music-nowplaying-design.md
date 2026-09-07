# Apple Music Now Playing Style — Design Spec

## Goal

Add a second Now Playing visual style inspired by Apple Music's frosted-glass aesthetic, selectable via a Settings toggle. The default style (Spotify/Dynamic Gradient) remains unchanged. When "Apple Music" is selected, the Now Playing screen shows a blurred album artwork backdrop with a dark gradient wash, rounded album art, and a depth-of-field lyrics effect.

## Background

The existing Now Playing (`NowPlayingView`) uses a `LinearGradientBrush` with dominant-color extraction for a Spotify-like gradient look. This spec adds an alternative "Apple Music" visual mode that reuses the same Pivot (Player/Lyrics/Queue), transport controls, and playback logic — only the **visual layer** changes.

Reference: SimpMusic's Apple Music composables (`NowPlayingContentAppleMusic.kt`, `AppleMusicShared.kt`, `AppleMusicLyricsLines.kt`).

---

## Architecture Overview

```
┌───────────────────────────────────────────────┐
│  Settings: NowPlayingStyle = "Default" | "AppleMusic"  │
│  (Saved in LocalSettings)                              │
└───────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────┐
│  MainPage.NowPlaying.cs        │
│  ─ On track change / open:     │
│    if AppleMusic → show blur   │
│    backdrop + dark wash        │
│    else → show gradient        │
└─────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────┐
│  Services/LumiaBlurHelper.cs   │
│  ─ RenderBlurredAsync(         │
│      Stream, targetW, targetH, │
│      kernelSize)               │
│  ─ Returns WriteableBitmap     │
│  ─ LRU cache (max 3 entries)   │
└─────────────────────────────────┘
```

**Key design decision:** No new XAML page or UserControl. The Apple Music mode reuses the existing `NowPlayingView` Grid but swaps the background layer (hide gradient Rectangles, show blur Image) and adjusts element styling at runtime via code-behind.

---

## 1. LumiaBlurHelper Service

**File:** `Services/LumiaBlurHelper.cs`

A small static helper that renders a blurred bitmap from a thumbnail stream using the Lumia Imaging SDK.

### API

```csharp
internal static class LumiaBlurHelper
{
    /// <summary>
    /// Renders a heavily blurred version of the source image.
    /// Target bitmap is small (e.g. 120×200) — XAML GPU-scales it via Stretch="UniformToFill".
    /// </summary>
    static async Task<WriteableBitmap> RenderBlurredAsync(
        Stream source, int targetWidth, int targetHeight, int kernelSize);
}
```

### Implementation Details

- **Pipeline:** `StreamImageSource(source)` → `FilterEffect { Filters = [BlurFilter(kernelSize)] }` → `WriteableBitmapRenderer(effect, bitmap)` → `RenderAsync()`.
- **Target size:** 120×200 pixels. At kernel size 80+, resolution is imperceptible. This keeps RAM usage under 100KB per bitmap.
- **Kernel size:** 80 (matches SimpMusic's `BACKDROP_BLUR_RADIUS = 80.dp`). Capped at 256 per SDK limit.
- **LRU cache:** `Dictionary<string, WriteableBitmap>` keyed by thumbnail URL, max 3 entries. On eviction, oldest entry removed. Cache is cleared on low-memory events.
- **No WinRT dependency in signature:** Takes `System.IO.Stream`, matching `StreamImageSource`'s constructor.

### Memory Budget (512MB target)

| Item | Size |
|------|------|
| WriteableBitmap 120×200 BGRA | ~96 KB |
| Cache (3 entries) | ~288 KB |
| Lumia pipeline transient | ~200 KB (freed after render) |
| **Total peak** | **~490 KB** |

---

## 2. XAML Changes — NowPlayingView Background Layer

**File:** `MainPage.xaml` (lines ~2192-2207)

Add new elements **inside** the existing `NowPlayingView` Grid, before the gradient Rectangles:

```xml
<!-- Apple Music: Blurred backdrop image (hidden by default) -->
<Image x:Name="AppleMusicBackdrop" Stretch="UniformToFill" Visibility="Collapsed" />

<!-- Apple Music: Dark gradient wash over the blur -->
<Rectangle x:Name="AppleMusicWash" Visibility="Collapsed">
    <Rectangle.Fill>
        <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
            <GradientStop x:Name="AppleMusicWashTop" Color="#0D000000" Offset="0"/>
            <GradientStop x:Name="AppleMusicWashMid" Color="#52000000" Offset="0.48"/>
            <GradientStop x:Name="AppleMusicWashBot" Color="#C8000000" Offset="1.0"/>
        </LinearGradientBrush>
    </Rectangle.Fill>
</Rectangle>
```

**Wash gradient math** (from SimpMusic):
- Top: `lerp(seedColor, Black, 0.05)` → for a wash overlay on blur, use alpha ~5% black = `#0D000000`
- Mid @48%: `lerp(seedColor, Black, 0.32)` → alpha ~32% black = `#52000000`
- Bottom: `lerp(seedColor, Black, 0.78)` → alpha ~78% black = `#C8000000`

### Visibility Toggle Logic

When Apple Music mode is active:
- `AppleMusicBackdrop.Visibility = Visible`
- `AppleMusicWash.Visibility = Visible`
- The existing `#99000000` overlay Rectangle → `Collapsed`
- The existing gradient Rectangle → `Collapsed`

When Default mode is active:
- `AppleMusicBackdrop.Visibility = Collapsed`
- `AppleMusicWash.Visibility = Collapsed`
- Existing gradient Rectangles → `Visible` (as they are now)

---

## 3. Album Art Display — Apple Music Mode

In Apple Music mode, the Player PivotItem changes the album art appearance:

| Property | Default (Spotify) | Apple Music |
|----------|-------------------|-------------|
| Size | 300×300 | 260×260 |
| Corner radius | 16 | 20 |
| Shadow | Offset shadow rectangle | Drop shadow via darker copy underneath |
| Margin top | -10 | 10 (centered with backdrop visible) |

This is done by adjusting the existing `BigCoverRectangle` properties in code-behind — no new XAML elements needed.

---

## 4. Lyrics — Depth-of-Field Effect

In Apple Music mode, the lyrics view uses a "focus" effect where the current line is bold white, and lines farther from the current index become progressively dimmer and smaller.

### Color Scheme

| Line | Color | Opacity |
|------|-------|---------|
| Active (current) | White `#FFFFFF` | 1.0 |
| ±1 line | `#9B9B9B` | 0.85 |
| ±2 lines | `#9B9B9B` | 0.60 |
| ±3+ lines | `#9B9B9B` | 0.40 |

### Font Size Scaling

| Line | Font Size |
|------|-----------|
| Active | User's configured size (default 24) |
| ±1 | Active × 0.90 |
| ±2 | Active × 0.82 |
| ±3+ | Active × 0.75 |

**WP8.1 constraint — no real-time blur on text.** SimpMusic uses Compose's `Modifier.blur()` for a true depth-of-field effect on lyric text. WinRT XAML has no per-element blur. Instead, we approximate the effect using opacity + font-size falloff, which is visually similar and costs zero GPU.

### Implementation

The existing `ForceUpdateLyricUI()` in `MainPage.Playback.cs` already iterates visible lyric items and applies Opacity + Scale transforms. In Apple Music mode, it will:
1. Skip the Scale animation (no zoom in/out).
2. Apply the opacity/size table above based on `|index - currentLyricIndex|`.
3. Set the foreground color to `#9B9B9B` for inactive lines (vs the current green-accent dimming).

---

## 5. Transport Controls — Apple Music Mode Adjustments

In Apple Music mode, the transport controls area (`Grid.Row="2"` StackPanel) receives minor visual tweaks via code-behind property changes:

| Element | Default (Spotify) | Apple Music |
|---------|-------------------|-------------|
| Play/Pause button | Green circle `#1DB954` with black icon | White circle with black icon |
| Slider accent | `#1DB954` | White `#FFFFFF` |
| Shuffle/Repeat active dot | `#1DB954` | White `#FFFFFF` |
| Time text | `#B3B3B3` | `#B8FFFFFF` (72% white) |

**No structural layout changes** — only Foreground/Background color swaps in code-behind when the mode switches.

---

## 6. Settings Toggle

**File:** `MainPage.xaml` (Settings panel, after the Playback section ~line 1485)

Add a new settings section:

```xml
<!-- ═══════ APPEARANCE ═══════ -->
<Border Background="#1A1A1A" CornerRadius="16" Margin="0,0,0,16" Padding="18,16">
    <StackPanel>
        <StackPanel Orientation="Horizontal" Margin="0,0,0,14">
            <Border Background="#EC4899" CornerRadius="12" Width="28" Height="28" Margin="0,0,10,0">
                <TextBlock Text="🎨" FontSize="13" HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <TextBlock Text="Appearance" FontSize="16" FontFamily="{StaticResource MontserratSemiBold}" Foreground="White" VerticalAlignment="Center"/>
        </StackPanel>
        
        <!-- Now Playing Style -->
        <TextBlock Text="Now Playing Style" Foreground="White" FontSize="15" FontFamily="{StaticResource MontserratSemiBold}" Margin="0,0,0,4"/>
        <TextBlock Text="Visual style for the music player screen" Foreground="#555" FontSize="11" Margin="0,0,0,10"/>
        <ComboBox x:Name="NowPlayingStyleComboBox" Background="#252525" Foreground="White" BorderThickness="0" HorizontalAlignment="Stretch" SelectionChanged="NowPlayingStyleComboBox_SelectionChanged">
            <ComboBoxItem Content="Default (Dynamic Gradient)" Tag="Default" IsSelected="True"/>
            <ComboBoxItem Content="Apple Music (Blurred Artwork)" Tag="AppleMusic"/>
        </ComboBox>
    </StackPanel>
</Border>
```

### Persistence & Activation

- **When does a style change take effect?** Immediately on the next `MiniPlayer_Tapped` (opening Now Playing). If Now Playing is already open when the user changes the setting, it does **not** hot-swap — the new style applies when they close and reopen. This avoids complex mid-session state transitions.
- **Key:** `NowPlayingStyle` in `ApplicationData.Current.LocalSettings`
- **Values:** `"Default"` (default), `"AppleMusic"`
- Read on app startup in `MainPage` constructor
- Written on `ComboBox.SelectionChanged`
- Helper property in code-behind:

```csharp
private bool IsAppleMusicStyle
{
    get
    {
        var val = ApplicationData.Current.LocalSettings.Values["NowPlayingStyle"];
        return val != null && val.ToString() == "AppleMusic";
    }
}
```

---

## 7. Backdrop Update Flow

When a new track starts playing (or Now Playing opens):

```
1. UpdateNowPlayingGradient() is called (existing)
2. Check IsAppleMusicStyle:
   a. If false → existing gradient flow (unchanged)
   b. If true →
      i.   Download thumbnail (reuse existing _dominantHttpClient + byte[])
      ii.  Call LumiaBlurHelper.RenderBlurredAsync(stream, 120, 200, 80)
      iii. Set AppleMusicBackdrop.Source = resultBitmap
      iv.  Extract dominant color (existing ExtractDominantColorAsync)
      v.   Tint the wash gradient stops using the dominant color
      vi.  Update status bar color to match tinted backdrop
```

The tinting in step (v) adjusts the wash `GradientStop` colors:
- Top: `Color.FromArgb(13, seed.R, seed.G, seed.B)` — very subtle seed tint
- Mid: `Color.FromArgb(82, seed.R, seed.G, seed.B)` — moderate tint
- Bottom: `Color.FromArgb(200, seed.R, seed.G, seed.B)` — heavy dark tint

This gives the frosted-glass backdrop a warm color cast matching the album art, similar to SimpMusic.

---

## 8. Status Bar Integration

When Apple Music mode is active, the status bar uses the tinted dominant color (same as Default mode). The existing `UpdateStatusBarColor()` and `AnimateStatusBarColorAsync()` work unchanged — they already read `_currentGradientColor`.

---

## 9. Scope Exclusions

The following SimpMusic features are **intentionally excluded** to keep scope minimal and respect WP8.1 limitations:

| Feature | Reason |
|---------|--------|
| Video/Canvas background | No MediaElement compositing on WP8.1 |
| Crossfade between views | Adds complexity, low value on small screens |
| Volume row | WP8.1 has hardware volume rocker |
| Drag-to-reorder queue | ListView reorder is unreliable on WP8.1 |
| Codec badge pill | No codec info available from BackgroundMediaPlayer |
| Auto-hide lyrics cluster timer | Adds state machine complexity for minimal UX gain |
| Real-time per-element text blur | No XAML blur support in WinRT; approximated via opacity |

---

## 10. File Change Summary

| File | Change |
|------|--------|
| `Services/LumiaBlurHelper.cs` | **NEW** — Static blur rendering helper |
| `MainPage.xaml` | Add `AppleMusicBackdrop` Image + `AppleMusicWash` Rectangle in NowPlayingView; Add Appearance section in Settings |
| `Pages/MainPage.Playback.cs` | Modify `UpdateNowPlayingGradient()` to branch on `IsAppleMusicStyle`; add blur backdrop update logic |
| `Pages/MainPage.NowPlaying.cs` | Add `ApplyNowPlayingStyle()` method; modify `MiniPlayer_Tapped()` to apply style; add settings change handler |
| `Pages/MainPage.Lyrics.cs` | Modify lyric item styling in Apple Music mode (opacity/size table) |

**Total estimated new code:** ~200 lines across all files.
