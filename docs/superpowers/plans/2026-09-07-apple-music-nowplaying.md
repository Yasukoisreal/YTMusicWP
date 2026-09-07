# Apple Music Now Playing Style — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a second Now Playing visual style ("Apple Music / Blurred Artwork") that faithfully reproduces SimpMusic's Apple Music layout — frosted artwork backdrop, full-bleed artwork, BottomCluster with ThinSlider/transport/volume/dock, compact headers, depth-of-field lyrics — toggled via Settings.

**Architecture:** The existing `NowPlayingView` Grid in MainPage.xaml gains a parallel set of XAML elements (Apple Music backdrop, full-bleed artwork, BottomCluster, Dock bar) that are shown/hidden based on a `NowPlayingStyle` setting. Code-behind branches on `_isAppleMusicStyle` boolean at key points (gradient update, lyrics styling, transport layout). Lumia Imaging SDK renders the blurred backdrop offline to a small WriteableBitmap.

**Tech Stack:** WPA81 / C# 6.0 / XAML / Lumia Imaging SDK 2.0 (`BlurFilter`)

**Spec:** [`docs/superpowers/specs/2026-09-07-apple-music-nowplaying-design.md`](file:///d:/Documents/Visual%20Studio%202015/Projects/YTMusicWP/docs/superpowers/specs/2026-09-07-apple-music-nowplaying-design.md)

## Global Constraints

- **Target:** WPA81 (Windows Phone 8.1), 512MB RAM devices (Lumia 520/630)
- **C# version:** 6.0 only — no tuples, no local functions, no `out var`
- **IPC payload:** Max 100 items around current track in ValueSet arrays
- **No terminal for code ops:** ALWAYS use native Antigravity tools (view_file, replace_file_content, write_to_file) — NEVER PowerShell cat/grep/sed
- **PowerShell:** Use `;` separator, never `&&`
- **Encoding:** Preserve UTF-8 characters (icons like ♡, ♫, ⛶)
- **Lumia SDK:** Already in `packages/LumiaImagingSDK.2.0.208`, `.csproj` line 224 imports targets. `StreamImageSource` takes `System.IO.Stream` (not IRandomAccessStream)

---

### Task 1: LumiaBlurHelper Service

**Files:**
- Create: `Services/LumiaBlurHelper.cs`

**Interfaces:**
- Consumes: Nothing (standalone utility)
- Produces: `LumiaBlurHelper.RenderBlurredAsync(Stream source, int targetWidth, int targetHeight, int kernelSize) → Task<WriteableBitmap>`

- [ ] **Step 1: Create `Services/LumiaBlurHelper.cs`**

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media.Imaging;
using Lumia.Imaging;
using Lumia.Imaging.Adjustments;

namespace YTMusicWP.Services
{
    internal static class LumiaBlurHelper
    {
        // LRU cache: max 3 entries keyed by URL
        private static readonly string[] _cacheKeys = new string[3];
        private static readonly WriteableBitmap[] _cacheValues = new WriteableBitmap[3];
        private static int _cacheIndex;

        public static WriteableBitmap GetCached(string key)
        {
            for (int i = 0; i < _cacheKeys.Length; i++)
            {
                if (_cacheKeys[i] == key && _cacheValues[i] != null)
                    return _cacheValues[i];
            }
            return null;
        }

        public static void PutCache(string key, WriteableBitmap bitmap)
        {
            _cacheKeys[_cacheIndex] = key;
            _cacheValues[_cacheIndex] = bitmap;
            _cacheIndex = (_cacheIndex + 1) % _cacheKeys.Length;
        }

        /// <summary>
        /// Renders a blurred version of the source image at the specified dimensions.
        /// Uses Lumia Imaging SDK BlurFilter for ARM NEON-accelerated blur.
        /// Typical usage: 120×200 target with kernelSize=80 for frosted backdrop.
        /// </summary>
        public static async Task<WriteableBitmap> RenderBlurredAsync(
            Stream source, int targetWidth, int targetHeight, int kernelSize)
        {
            var bitmap = new WriteableBitmap(targetWidth, targetHeight);
            using (var imageSource = new StreamImageSource(source))
            using (var filterEffect = new FilterEffect(imageSource))
            {
                filterEffect.Filters = new IFilter[] { new BlurFilter(kernelSize) };
                using (var renderer = new WriteableBitmapRenderer(filterEffect, bitmap))
                {
                    await renderer.RenderAsync();
                }
            }
            return bitmap;
        }
    }
}
```

- [ ] **Step 2: Add file to `.csproj`**

Search `YTMusicWP.csproj` for an existing `<Compile Include="Services\` line and add `<Compile Include="Services\LumiaBlurHelper.cs" />` adjacent to it. If no `Services\` folder exists in the csproj, add it near other `<Compile>` entries.

- [ ] **Step 3: Build and verify compilation**

Run: `msbuild YTMusicWP.csproj /t:Build /p:Configuration=Debug /p:Platform=ARM /v:m`
Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Commit**

```
git add Services/LumiaBlurHelper.cs YTMusicWP.csproj
git commit -m "feat: add LumiaBlurHelper blur rendering service"
```

---

### Task 2: Settings Toggle + AppleMusicSliderStyle

**Files:**
- Modify: `MainPage.xaml` (lines ~348-377 for new style, lines ~1485-1488 for settings section)
- Modify: `Pages/MainPage.Settings.cs` (or wherever settings load/save lives) — add `NowPlayingStyle` load/save

**Interfaces:**
- Consumes: Nothing
- Produces: `AppleMusicSliderStyle` (XAML Style resource), `NowPlayingStyleComboBox` (ComboBox element), `_isAppleMusicStyle` (bool field read by Tasks 3-6)

- [ ] **Step 1: Add `AppleMusicSliderStyle` to MainPage.xaml Resources**

Insert after the existing `ModernSliderStyle` (around line 377). This style is identical but with: no visible thumb, 7px track height, white foreground, dark-translucent background.

```xml
        <Style x:Key="AppleMusicSliderStyle" TargetType="Slider">
            <Setter Property="Background" Value="#42FFFFFF"/>
            <Setter Property="Foreground" Value="#EBFFFFFF"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Slider">
                        <Grid Margin="0,10" Background="Transparent">
                            <Grid x:Name="HorizontalTemplate" Margin="0,0">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto"/>
                                    <ColumnDefinition Width="Auto"/>
                                    <ColumnDefinition Width="*"/>
                                </Grid.ColumnDefinitions>
                                <Rectangle x:Name="HorizontalTrackRect" Grid.ColumnSpan="3" Height="7" Fill="{TemplateBinding Background}" VerticalAlignment="Center" RadiusX="4" RadiusY="4"/>
                                <Rectangle x:Name="HorizontalDecreaseRect" Grid.Column="0" Height="7" Fill="{TemplateBinding Foreground}" VerticalAlignment="Center" RadiusX="4" RadiusY="4"/>
                                <Thumb x:Name="HorizontalThumb" Grid.Column="1" Margin="-1,0,-1,0" Padding="0">
                                    <Thumb.Template>
                                        <ControlTemplate TargetType="Thumb">
                                            <Grid Background="Transparent" Width="2" Height="28"/>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Grid>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
```

- [ ] **Step 2: Add Appearance section in Settings XAML**

Insert between the Playback Border closing tag (line ~1485) and the Live Tile Border opening tag (line ~1488):

```xml
                    <!-- ═══════ APPEARANCE ═══════ -->
                    <Border Background="#1A1A1A" CornerRadius="16" Margin="0,0,0,16" Padding="18,16">
                        <StackPanel>
                            <StackPanel Orientation="Horizontal" Margin="0,0,0,14">
                                <Border Background="#EC4899" CornerRadius="12" Width="28" Height="28" Margin="0,0,10,0">
                                    <TextBlock Text="&#xE2B1;" FontFamily="Segoe UI Symbol" FontSize="13" Foreground="White" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
                                <TextBlock Text="Appearance" FontSize="16" FontFamily="{StaticResource MontserratSemiBold}" Foreground="White" VerticalAlignment="Center"/>
                            </StackPanel>

                            <Grid Margin="0,0,0,0">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <StackPanel VerticalAlignment="Center">
                                    <TextBlock Text="Now Playing Style" Foreground="White" FontSize="15" FontFamily="{StaticResource MontserratSemiBold}"/>
                                    <TextBlock Text="Visual style for the player screen" Foreground="#555" FontSize="11" Margin="0,2,0,0"/>
                                </StackPanel>
                                <ComboBox x:Name="NowPlayingStyleComboBox" Grid.Column="1" Background="#252525" Foreground="White" BorderThickness="0" Width="160" VerticalAlignment="Center" SelectionChanged="NowPlayingStyleComboBox_SelectionChanged">
                                    <ComboBoxItem Content="Default (Gradient)" Tag="0" IsSelected="True"/>
                                    <ComboBoxItem Content="Apple Music (Blur)" Tag="1"/>
                                </ComboBox>
                            </Grid>
                        </StackPanel>
                    </Border>
```

- [ ] **Step 3: Add `_isAppleMusicStyle` field and settings load/save logic**

In the appropriate settings code-behind file, add:

```csharp
// Field — set once on MiniPlayer_Tapped from LocalSettings, NOT hot-swapped
private bool _isAppleMusicStyle;

private void NowPlayingStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    var combo = sender as ComboBox;
    if (combo == null || combo.SelectedItem == null) return;
    var item = combo.SelectedItem as ComboBoxItem;
    if (item == null) return;
    var tag = item.Tag as string;
    Windows.Storage.ApplicationData.Current.LocalSettings.Values["NowPlayingStyle"] = tag;
}
```

In the settings loading method (where `AutoplayToggle`, `LiveTileModeComboBox` etc. are initialized), add:

```csharp
// Load Now Playing Style
var npStyle = Windows.Storage.ApplicationData.Current.LocalSettings.Values["NowPlayingStyle"] as string;
if (npStyle == "1")
    NowPlayingStyleComboBox.SelectedIndex = 1;
else
    NowPlayingStyleComboBox.SelectedIndex = 0;
```

- [ ] **Step 4: Build and verify**

Run: `msbuild YTMusicWP.csproj /t:Build /p:Configuration=Debug /p:Platform=ARM /v:m`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```
git add MainPage.xaml Pages/MainPage.Settings.cs
git commit -m "feat: add AppleMusicSliderStyle and NowPlayingStyle setting"
```

---

### Task 3: NowPlayingView XAML Restructure

**Files:**
- Modify: `MainPage.xaml` (lines 2191-2391: NowPlayingView section)

**Interfaces:**
- Consumes: `AppleMusicSliderStyle` (from Task 2)
- Produces: New XAML elements: `AppleMusicBackdrop`, `AppleMusicWash`, `AppleMusicArtwork`, `AppleMusicArtworkFade`, `AppleMusicCompactHeader` (Image+TextBlocks), `AppleMusicSlider`, `AppleMusicCurrentTime`/`AppleMusicRemainingTime`, `AppleMusicPlayBtn`, `AppleMusicVolumeSlider`, `DockLyricsBtn`/`DockQueueBtn`, `AppleMusicBottomCluster`, `AppleMusicGrabber`

This is the largest task. The strategy is: ADD new Apple Music elements alongside existing ones (both sets initially Collapsed), then Task 5 handles showing/hiding based on `_isAppleMusicStyle`.

- [ ] **Step 1: Add Apple Music backdrop layers**

Inside the `NowPlayingView` Grid (after the existing two background Rectangles at lines ~2196-2205), add:

```xml
            <!-- Apple Music: frosted artwork backdrop (Collapsed by default, shown by code) -->
            <Image x:Name="AppleMusicBackdrop" Stretch="UniformToFill" Visibility="Collapsed"/>
            <!-- Apple Music: tinted gradient wash over the blur -->
            <Rectangle x:Name="AppleMusicWash" Opacity="0.62" Visibility="Collapsed">
                <Rectangle.Fill>
                    <LinearGradientBrush x:Name="AppleMusicWashGradient" StartPoint="0,0" EndPoint="0,1">
                        <GradientStop x:Name="AppleMusicGradTop" Color="#1A1A2E" Offset="0"/>
                        <GradientStop x:Name="AppleMusicGradMid" Color="#121212" Offset="0.48"/>
                        <GradientStop x:Name="AppleMusicGradBot" Color="#0D0D0D" Offset="1.0"/>
                    </LinearGradientBrush>
                </Rectangle.Fill>
            </Rectangle>
```

- [ ] **Step 2: Add Apple Music grabber bar**

Inside the inner `Grid Canvas.ZIndex="10"`, in Row 0, add:

```xml
                <!-- Apple Music: grabber bar (replaces header in AM mode) -->
                <Border x:Name="AppleMusicGrabber" Visibility="Collapsed" Grid.Row="0" Background="Transparent" Tapped="CloseNowPlaying_Click" Height="40" VerticalAlignment="Top" Margin="0,25,0,0">
                    <Border Background="#59FFFFFF" CornerRadius="3" Width="36" Height="5" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
```

- [ ] **Step 3: Add Apple Music full-bleed artwork in Player PivotItem**

Inside the first PivotItem (Player page), add alongside existing centered art:

```xml
                            <!-- Apple Music: full-bleed artwork with bottom fade -->
                            <Grid x:Name="AppleMusicArtworkGrid" Visibility="Collapsed" VerticalAlignment="Top" HorizontalAlignment="Stretch">
                                <Image x:Name="AppleMusicArtwork" Stretch="UniformToFill" HorizontalAlignment="Center"/>
                                <Rectangle x:Name="AppleMusicArtworkFade" VerticalAlignment="Bottom" Height="200" IsHitTestVisible="False">
                                    <Rectangle.Fill>
                                        <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
                                            <GradientStop x:Name="AppleMusicArtFadeTop" Color="#00000000" Offset="0"/>
                                            <GradientStop x:Name="AppleMusicArtFadeBot" Color="#CC000000" Offset="1"/>
                                        </LinearGradientBrush>
                                    </Rectangle.Fill>
                                </Rectangle>
                            </Grid>
```

- [ ] **Step 4: Add Apple Music compact headers for Lyrics and Queue PivotItems**

At top of Lyrics PivotItem Grid, add:

```xml
                            <!-- Apple Music: compact header -->
                            <Grid x:Name="AppleMusicLyricsHeader" Visibility="Collapsed" Height="70" Margin="15,10,15,0">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="55"/>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <Rectangle Grid.Column="0" Width="55" Height="55" RadiusX="4" RadiusY="4">
                                    <Rectangle.Fill>
                                        <ImageBrush x:Name="AppleMusicLyricsThumb" Stretch="UniformToFill"/>
                                    </Rectangle.Fill>
                                </Rectangle>
                                <StackPanel Grid.Column="1" VerticalAlignment="Center" Margin="12,0,0,0">
                                    <TextBlock x:Name="AppleMusicLyricsTitle" Text="" FontSize="13" FontFamily="{StaticResource MontserratSemiBold}" Foreground="White" TextTrimming="CharacterEllipsis" MaxLines="1"/>
                                    <TextBlock x:Name="AppleMusicLyricsArtist" Text="" FontSize="12" Foreground="#B3B3B3" TextTrimming="CharacterEllipsis" MaxLines="1" Margin="0,2,0,0"/>
                                </StackPanel>
                                <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                                    <Button Content="♡" Click="HeartButton_Click" Style="{StaticResource IconButtonStyle}" Foreground="White" FontSize="22" Width="40" Height="40" Padding="0" MinWidth="0" MinHeight="0"/>
                                    <Button Click="OpenNowPlayingMenu_Click" Style="{StaticResource IconButtonStyle}" Width="40" Height="40" Padding="0" MinWidth="0" MinHeight="0">
                                        <Viewbox Width="18" Height="18"><Path Data="M12,16A2,2 0 0,1 10,14A2,2 0 0,1 12,12A2,2 0 0,1 14,14A2,2 0 0,1 12,16M12,10A2,2 0 0,1 10,8A2,2 0 0,1 12,6A2,2 0 0,1 14,8A2,2 0 0,1 12,10M12,22A2,2 0 0,1 10,20A2,2 0 0,1 12,18A2,2 0 0,1 14,20A2,2 0 0,1 12,22Z" Fill="White"/></Viewbox>
                                    </Button>
                                </StackPanel>
                            </Grid>
```

Repeat same structure for Queue PivotItem with names: `AppleMusicQueueHeader`, `AppleMusicQueueThumb`, `AppleMusicQueueTitle`, `AppleMusicQueueArtist`.

- [ ] **Step 5: Add Queue pills row in Queue PivotItem**

```xml
                            <!-- Apple Music: queue action pills -->
                            <StackPanel x:Name="AppleMusicQueuePills" Visibility="Collapsed" Orientation="Horizontal" HorizontalAlignment="Stretch" Margin="15,4,15,10">
                                <Border Background="#3DFFFFFF" CornerRadius="20" Height="40" Margin="0,0,8,0" HorizontalAlignment="Stretch" Tapped="QueueInfoPill_Tapped">
                                    <FontIcon FontFamily="Segoe UI Symbol" Glyph="&#xE946;" Foreground="White" FontSize="18" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
                                <Border Background="#3DFFFFFF" CornerRadius="20" Height="40" Margin="0,0,8,0" HorizontalAlignment="Stretch" Tapped="QueueAddPill_Tapped">
                                    <FontIcon FontFamily="Segoe UI Symbol" Glyph="&#xE109;" Foreground="White" FontSize="18" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
                                <Border x:Name="AppleMusicShufflePill" Background="#3DFFFFFF" CornerRadius="20" Height="40" Margin="0,0,8,0" HorizontalAlignment="Stretch" Tapped="ShuffleButton_Click">
                                    <FontIcon x:Name="AppleMusicShuffleIcon" FontFamily="Segoe UI Symbol" Glyph="&#xE14B;" Foreground="White" FontSize="18" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
                                <Border x:Name="AppleMusicRepeatPill" Background="#3DFFFFFF" CornerRadius="20" Height="40" HorizontalAlignment="Stretch" Tapped="RepeatButton_Click">
                                    <FontIcon x:Name="AppleMusicRepeatIcon" FontFamily="Segoe UI Symbol" Glyph="&#xE1CD;" Foreground="White" FontSize="18" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
                            </StackPanel>
```

- [ ] **Step 6: Add Apple Music BottomCluster**

After existing controls StackPanel (Row 2), add:

```xml
                <!-- Apple Music: BottomCluster -->
                <StackPanel x:Name="AppleMusicBottomCluster" Grid.Row="2" Visibility="Collapsed" Margin="20,0,20,20">
                    <!-- ThinSlider -->
                    <Slider x:Name="AppleMusicSlider" Minimum="0" Maximum="100" Value="0" PointerCaptureLost="MusicSlider_PointerCaptureLost" PointerReleased="MusicSlider_PointerCaptureLost" PointerPressed="MusicSlider_PointerPressed" Style="{StaticResource AppleMusicSliderStyle}"/>
                    <!-- TimesRow -->
                    <Grid Margin="0,2,0,0">
                        <TextBlock x:Name="AppleMusicCurrentTime" Text="0:00" FontSize="12" Foreground="#B8FFFFFF" HorizontalAlignment="Left"/>
                        <TextBlock x:Name="AppleMusicRemainingTime" Text="-0:00" FontSize="12" Foreground="#B8FFFFFF" HorizontalAlignment="Right"/>
                    </Grid>
                    <!-- TransportRow -->
                    <Grid Margin="0,12,0,0" HorizontalAlignment="Center">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="58"/>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="58"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <Button Grid.Column="0" Click="PrevButton_Click" Style="{StaticResource IconButtonStyle}" Width="48" Height="48">
                            <FontIcon FontFamily="Segoe UI Symbol" Glyph="&#xE100;" Foreground="White" FontSize="30"/>
                        </Button>
                        <Button x:Name="AppleMusicPlayBtn" Grid.Column="2" Click="PlayPauseButton_Click" Style="{StaticResource IconButtonStyle}" Width="60" Height="60">
                            <SymbolIcon x:Name="AppleMusicPlayIcon" Symbol="Play" Foreground="White" RenderTransformOrigin="0.5,0.5">
                                <SymbolIcon.RenderTransform>
                                    <ScaleTransform ScaleX="2.2" ScaleY="2.2"/>
                                </SymbolIcon.RenderTransform>
                            </SymbolIcon>
                        </Button>
                        <Button Grid.Column="4" Click="NextButton_Click" Style="{StaticResource IconButtonStyle}" Width="48" Height="48">
                            <FontIcon FontFamily="Segoe UI Symbol" Glyph="&#xE101;" Foreground="White" FontSize="30"/>
                        </Button>
                    </Grid>
                    <!-- VolumeRow -->
                    <Grid Margin="0,14,0,0">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <FontIcon Grid.Column="0" FontFamily="Segoe UI Symbol" Glyph="&#xE993;" Foreground="#B8FFFFFF" FontSize="16" VerticalAlignment="Center" Margin="0,0,10,0"/>
                        <Slider x:Name="AppleMusicVolumeSlider" Grid.Column="1" Minimum="0" Maximum="100" Value="100" Style="{StaticResource AppleMusicSliderStyle}" ValueChanged="AppleMusicVolumeSlider_ValueChanged"/>
                        <FontIcon Grid.Column="2" FontFamily="Segoe UI Symbol" Glyph="&#xE994;" Foreground="#B8FFFFFF" FontSize="16" VerticalAlignment="Center" Margin="10,0,0,0"/>
                    </Grid>
                    <!-- Dock -->
                    <Grid Margin="0,14,0,0">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <Border x:Name="DockLyricsBtn" Grid.Column="0" Background="Transparent" CornerRadius="20" Width="40" Height="40" HorizontalAlignment="Center" Tapped="DockLyrics_Tapped">
                            <TextBlock Text="♪" FontSize="20" Foreground="#D9FFFFFF" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                        <Border x:Name="DockQueueBtn" Grid.Column="1" Background="Transparent" CornerRadius="20" Width="40" Height="40" HorizontalAlignment="Center" Tapped="DockQueue_Tapped">
                            <FontIcon FontFamily="Segoe UI Symbol" Glyph="&#xE142;" Foreground="#D9FFFFFF" FontSize="18" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                    </Grid>
                </StackPanel>
```

- [ ] **Step 7: Build and verify**

Run: `msbuild YTMusicWP.csproj /t:Build /p:Configuration=Debug /p:Platform=ARM /v:m`
Expected: Build succeeds. Add empty stubs for new event handlers (`DockLyrics_Tapped`, `DockQueue_Tapped`, `AppleMusicVolumeSlider_ValueChanged`, `QueueInfoPill_Tapped`, `QueueAddPill_Tapped`) if compilation fails.

- [ ] **Step 8: Commit**

```
git add MainPage.xaml
git commit -m "feat: add Apple Music XAML layout (backdrop, artwork, BottomCluster, dock)"
```

---

### Task 4: Apple Music Color System + Backdrop Rendering

**Files:**
- Modify: `Pages/MainPage.Playback.cs` (lines ~970-1060 and ~1155-1223)

**Interfaces:**
- Consumes: `LumiaBlurHelper.RenderBlurredAsync()` (Task 1), `AppleMusicBackdrop`/`AppleMusicGrad*`/`AppleMusicArtFade*` (Task 3), `_isAppleMusicStyle` (Task 2)
- Produces: `LerpColor()`, `UpdateAppleMusicBackdropAsync()`, modified `AnimateGradientTo()`

- [ ] **Step 1: Add `LerpColor` helper**

Near `AdjustAmbientColor` (~line 970):

```csharp
        private static Windows.UI.Color LerpColor(Windows.UI.Color from, Windows.UI.Color to, double t)
        {
            return Windows.UI.Color.FromArgb(255,
                (byte)(from.R + (to.R - from.R) * t),
                (byte)(from.G + (to.G - from.G) * t),
                (byte)(from.B + (to.B - from.B) * t));
        }
```

- [ ] **Step 2: Add `UpdateAppleMusicBackdropAsync`**

```csharp
        private async Task UpdateAppleMusicBackdropAsync(string thumbnailUrl, Windows.UI.Color seedColor)
        {
            if (string.IsNullOrEmpty(thumbnailUrl)) return;

            var black = Windows.UI.Colors.Black;
            if (AppleMusicGradTop != null) AppleMusicGradTop.Color = LerpColor(seedColor, black, 0.05);
            if (AppleMusicGradMid != null) AppleMusicGradMid.Color = LerpColor(seedColor, black, 0.32);
            if (AppleMusicGradBot != null) AppleMusicGradBot.Color = LerpColor(seedColor, black, 0.78);
            if (AppleMusicArtFadeBot != null) AppleMusicArtFadeBot.Color = LerpColor(seedColor, black, 0.78);

            var cached = Services.LumiaBlurHelper.GetCached(thumbnailUrl);
            if (cached != null)
            {
                if (AppleMusicBackdrop != null) AppleMusicBackdrop.Source = cached;
                return;
            }

            try
            {
                var httpClient = new System.Net.Http.HttpClient();
                var bytes = await httpClient.GetByteArrayAsync(thumbnailUrl);
                using (var stream = new System.IO.MemoryStream(bytes))
                {
                    var blurred = await Services.LumiaBlurHelper.RenderBlurredAsync(stream, 120, 200, 80);
                    Services.LumiaBlurHelper.PutCache(thumbnailUrl, blurred);
                    if (AppleMusicBackdrop != null) AppleMusicBackdrop.Source = blurred;
                }
            }
            catch { }
        }
```

- [ ] **Step 3: Branch `AnimateGradientTo` for Apple Music**

Add Apple Music branch at the start of `AnimateGradientTo` (before the existing `targetColor = AdjustAmbientColor(targetColor)` line):

```csharp
            if (_isAppleMusicStyle)
            {
                _currentGradientColor = targetColor;
                var black = Windows.UI.Colors.Black;
                if (AppleMusicGradTop != null) AppleMusicGradTop.Color = LerpColor(targetColor, black, 0.05);
                if (AppleMusicGradMid != null) AppleMusicGradMid.Color = LerpColor(targetColor, black, 0.32);
                if (AppleMusicGradBot != null) AppleMusicGradBot.Color = LerpColor(targetColor, black, 0.78);

                var fadeColor = LerpColor(targetColor, black, 0.78);
                if (LyricsFadeBottomStop0 != null) LyricsFadeBottomStop0.Color = fadeColor;
                if (LyricsFadeBottomStop1 != null)
                    LyricsFadeBottomStop1.Color = Windows.UI.Color.FromArgb(0, fadeColor.R, fadeColor.G, fadeColor.B);

                bool isVisible = (NowPlayingView != null && NowPlayingView.Visibility == Visibility.Visible);
                if (isVisible) UpdateStatusBarColor(true);
                return;
            }
```

- [ ] **Step 4: Call backdrop update from `UpdateNowPlayingGradient`**

After `ExtractDominantColorAsync` succeeds (~line 1200), add:

```csharp
            if (_isAppleMusicStyle && thumbUrl != null)
            {
                var ignored = UpdateAppleMusicBackdropAsync(thumbUrl, dominantColor);
            }
```

- [ ] **Step 5: Build and verify**

Run: `msbuild YTMusicWP.csproj /t:Build /p:Configuration=Debug /p:Platform=ARM /v:m`

- [ ] **Step 6: Commit**

```
git add Pages/MainPage.Playback.cs
git commit -m "feat: Apple Music color system — LerpColor, backdrop blur, gradient branching"
```

---

### Task 5: NowPlaying Interaction Logic

**Files:**
- Modify: `Pages/MainPage.NowPlaying.cs`

**Interfaces:**
- Consumes: `_isAppleMusicStyle` (Task 2), all Apple Music XAML elements (Task 3), `LerpColor()` (Task 4)
- Produces: `ApplyNowPlayingStyle()`, dock handlers, volume handler, compact header update, play icon sync

- [ ] **Step 1: Add `ApplyNowPlayingStyle` method**

```csharp
        private void ApplyNowPlayingStyle()
        {
            var npStyle = Windows.Storage.ApplicationData.Current.LocalSettings.Values["NowPlayingStyle"] as string;
            _isAppleMusicStyle = (npStyle == "1");

            var amVis = _isAppleMusicStyle ? Visibility.Visible : Visibility.Collapsed;
            var spVis = _isAppleMusicStyle ? Visibility.Collapsed : Visibility.Visible;

            // Background
            if (AppleMusicBackdrop != null) AppleMusicBackdrop.Visibility = amVis;
            if (AppleMusicWash != null) AppleMusicWash.Visibility = amVis;

            // Header vs Grabber
            if (AppleMusicGrabber != null) AppleMusicGrabber.Visibility = amVis;

            // Player page art
            if (AppleMusicArtworkGrid != null) AppleMusicArtworkGrid.Visibility = amVis;
            if (BigCoverRectangle != null) BigCoverRectangle.Visibility = spVis;
            if (BigCoverShadow != null) BigCoverShadow.Visibility = spVis;

            // Controls
            if (AppleMusicBottomCluster != null) AppleMusicBottomCluster.Visibility = amVis;

            // Compact headers
            if (AppleMusicLyricsHeader != null) AppleMusicLyricsHeader.Visibility = amVis;
            if (AppleMusicQueueHeader != null) AppleMusicQueueHeader.Visibility = amVis;
            if (AppleMusicQueuePills != null) AppleMusicQueuePills.Visibility = amVis;

            UpdateDockActiveState(-1);
        }
```

- [ ] **Step 2: Call from `MiniPlayer_Tapped`**

Add `ApplyNowPlayingStyle();` after `NowPlayingView.Visibility = Visibility.Visible;` (line ~140).

Also add compact header update call:

```csharp
            if (_isAppleMusicStyle)
            {
                UpdateAppleMusicCompactHeaders();
            }
```

- [ ] **Step 3: Add dock handlers**

```csharp
        private void DockLyrics_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (NowPlayingPivot == null) return;
            if (NowPlayingPivot.SelectedIndex == 1)
            {
                NowPlayingPivot.SelectedIndex = 0;
                UpdateDockActiveState(-1);
            }
            else
            {
                NowPlayingPivot.SelectedIndex = 1;
                UpdateDockActiveState(1);
            }
        }

        private void DockQueue_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (NowPlayingPivot == null) return;
            if (NowPlayingPivot.SelectedIndex == 2)
            {
                NowPlayingPivot.SelectedIndex = 0;
                UpdateDockActiveState(-1);
            }
            else
            {
                NowPlayingPivot.SelectedIndex = 2;
                UpdateDockActiveState(2);
            }
        }

        private void UpdateDockActiveState(int viewIndex)
        {
            if (!_isAppleMusicStyle) return;

            var white = Windows.UI.Colors.White;
            var black = Windows.UI.Colors.Black;
            var activeBg = new SolidColorBrush(LerpColor(_currentGradientColor, white, 0.75));
            var activeIcon = new SolidColorBrush(LerpColor(_currentGradientColor, black, 0.6));
            var inactiveBg = new SolidColorBrush(Windows.UI.Colors.Transparent);
            var inactiveIcon = new SolidColorBrush(Windows.UI.Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF));

            if (DockLyricsBtn != null)
            {
                DockLyricsBtn.Background = viewIndex == 1 ? activeBg : inactiveBg;
                var tb = DockLyricsBtn.Child as TextBlock;
                if (tb != null) tb.Foreground = viewIndex == 1 ? activeIcon : inactiveIcon;
            }
            if (DockQueueBtn != null)
            {
                DockQueueBtn.Background = viewIndex == 2 ? activeBg : inactiveBg;
                var fi = DockQueueBtn.Child as FontIcon;
                if (fi != null) fi.Foreground = viewIndex == 2 ? activeIcon : inactiveIcon;
            }
        }
```

- [ ] **Step 4: Add volume slider handler**

```csharp
        private void AppleMusicVolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            try
            {
                Windows.Media.Playback.BackgroundMediaPlayer.Current.Volume = e.NewValue / 100.0;
            }
            catch { }
        }
```

- [ ] **Step 5: Add compact header update**

```csharp
        private void UpdateAppleMusicCompactHeaders()
        {
            if (!_isAppleMusicStyle || currentTrack == null) return;

            var title = currentTrack.Title ?? "";
            var artist = currentTrack.ChannelName ?? "";

            if (AppleMusicLyricsTitle != null) AppleMusicLyricsTitle.Text = title;
            if (AppleMusicLyricsArtist != null) AppleMusicLyricsArtist.Text = artist;
            if (AppleMusicQueueTitle != null) AppleMusicQueueTitle.Text = title;
            if (AppleMusicQueueArtist != null) AppleMusicQueueArtist.Text = artist;

            if (BigCoverImage != null && BigCoverImage.ImageSource != null)
            {
                if (AppleMusicLyricsThumb != null) AppleMusicLyricsThumb.ImageSource = BigCoverImage.ImageSource;
                if (AppleMusicQueueThumb != null) AppleMusicQueueThumb.ImageSource = BigCoverImage.ImageSource;
                if (AppleMusicArtwork != null)
                    AppleMusicArtwork.Source = BigCoverImage.ImageSource as Windows.UI.Xaml.Media.Imaging.BitmapImage;
            }
        }
```

- [ ] **Step 6: Sync AppleMusicPlayIcon**

Find where `BigPlayIcon.Symbol` is toggled between `Play` and `Pause`, add:

```csharp
            if (AppleMusicPlayIcon != null)
                AppleMusicPlayIcon.Symbol = BigPlayIcon.Symbol;
```

- [ ] **Step 7: Sync AppleMusicSlider and time texts**

Find where `MusicSlider.Value` is updated in the position tracking timer, add:

```csharp
            if (_isAppleMusicStyle)
            {
                if (AppleMusicSlider != null) AppleMusicSlider.Value = MusicSlider.Value;
                if (AppleMusicCurrentTime != null) AppleMusicCurrentTime.Text = CurrentTimeText.Text;
                if (AppleMusicRemainingTime != null && totalSeconds > 0)
                {
                    var remain = totalSeconds - currentSeconds;
                    AppleMusicRemainingTime.Text = "-" + string.Format("{0}:{1:D2}", (int)remain / 60, (int)remain % 60);
                }
            }
```

- [ ] **Step 8: Add queue pill stubs**

```csharp
        private void QueueInfoPill_Tapped(object sender, TappedRoutedEventArgs e)
        {
            OpenNowPlayingMenu_Click(sender, e);
        }

        private void QueueAddPill_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // Reuse existing add-to-playlist flow if available
        }
```

- [ ] **Step 9: Sync dock on pivot change**

In existing `NowPlayingPivot_SelectionChanged`, add:

```csharp
            if (_isAppleMusicStyle)
                UpdateDockActiveState(NowPlayingPivot.SelectedIndex == 0 ? -1 : NowPlayingPivot.SelectedIndex);
```

- [ ] **Step 10: Build and verify**

Run: `msbuild YTMusicWP.csproj /t:Build /p:Configuration=Debug /p:Platform=ARM /v:m`

- [ ] **Step 11: Commit**

```
git add Pages/MainPage.NowPlaying.cs
git commit -m "feat: Apple Music NowPlaying logic — dock, style switch, compact headers, volume"
```

---

### Task 6: Apple Music Lyrics Styling

**Files:**
- Modify: `Pages/MainPage.Lyrics.cs` (lines ~429-438 and lyric update logic)
- Modify: `Pages/MainPage.Playback.cs` (lyric color/size update sections)

**Interfaces:**
- Consumes: `_isAppleMusicStyle` (Task 2), `_currentLyricIndex` field, `LyricsListView`
- Produces: Modified `LyricsListView_ContainerContentChanging`, Apple Music lyric color/alpha logic

- [ ] **Step 1: Branch `LyricsListView_ContainerContentChanging`**

Replace the method at lines 429-438:

```csharp
        private void LyricsListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer == null) return;

            if (_isAppleMusicStyle)
            {
                // Apple Music: no scale, alpha based on distance from active line
                args.ItemContainer.RenderTransform = null;
                int distance = Math.Abs(args.ItemIndex - _currentLyricIndex);
                if (args.ItemIndex < _currentLyricIndex && _currentLyricIndex >= 0)
                    distance += 1; // past-line penalty

                if (_currentLyricIndex < 0)
                    args.ItemContainer.Opacity = 0.6; // pre-roll
                else if (args.ItemIndex == _currentLyricIndex)
                    args.ItemContainer.Opacity = 1.0;
                else
                    args.ItemContainer.Opacity = Math.Max(0.25, 1.0 - distance * 0.25);
            }
            else
            {
                // Spotify: scale + dim
                args.ItemContainer.Opacity = 0.5;
                var st = new Windows.UI.Xaml.Media.ScaleTransform { ScaleX = 0.85, ScaleY = 0.85 };
                args.ItemContainer.RenderTransformOrigin = new Point(0, 0.5);
                args.ItemContainer.RenderTransform = st;
            }
        }
```

- [ ] **Step 2: Apple Music lyric colors**

Where `ColorBrush` is set on lyric items during active line updates, add the AM branch:

```csharp
            if (_isAppleMusicStyle)
            {
                currentLyrics[newIndex].ColorBrush = new SolidColorBrush(Windows.UI.Colors.White);
                if (oldIndex >= 0 && oldIndex < currentLyrics.Count)
                    currentLyrics[oldIndex].ColorBrush = new SolidColorBrush(
                        Windows.UI.Color.FromArgb(0xFF, 0x9B, 0x9B, 0x9B));
            }
```

- [ ] **Step 3: Update visible container opacity on active line change**

After the active line changes, update opacity on all visible containers:

```csharp
            if (_isAppleMusicStyle)
            {
                for (int i = 0; i < currentLyrics.Count; i++)
                {
                    var container = LyricsListView.ContainerFromIndex(i) as ListViewItem;
                    if (container == null) continue;
                    int dist = Math.Abs(i - newIndex);
                    if (i < newIndex) dist += 1;
                    container.Opacity = (i == newIndex) ? 1.0 : Math.Max(0.25, 1.0 - dist * 0.25);
                    container.RenderTransform = null;
                }
            }
```

- [ ] **Step 4: Uniform font size in Apple Music mode**

Where font size differs between active/inactive, add:

```csharp
            if (_isAppleMusicStyle)
            {
                // All lines same font size
                currentLyrics[newIndex].FontSize = _lyricsFontSize;
                if (oldIndex >= 0 && oldIndex < currentLyrics.Count)
                    currentLyrics[oldIndex].FontSize = _lyricsFontSize;
            }
```

- [ ] **Step 5: Build and verify**

Run: `msbuild YTMusicWP.csproj /t:Build /p:Configuration=Debug /p:Platform=ARM /v:m`

- [ ] **Step 6: Commit**

```
git add Pages/MainPage.Lyrics.cs Pages/MainPage.Playback.cs
git commit -m "feat: Apple Music lyrics — uniform size, #9B9B9B inactive, alpha distance falloff"
```

---

## Verification Plan

### Build Verification
After each task: `msbuild YTMusicWP.csproj /t:Build /p:Configuration=Debug /p:Platform=ARM /v:m` → 0 errors.

### Manual Verification (Deploy to Device)
1. **Settings:** Open Settings → Appearance → change to "Apple Music (Blur)" → verify saved
2. **MAIN view:** Tap MiniPlayer → frosted backdrop, tinted wash, full-bleed art, ThinSlider, plain white Play/Pause, volume row, dock bar
3. **Dock toggle:** Tap Lyrics → compact header + lyrics → tap again → returns to MAIN
4. **Lyrics:** Active=white, inactive=#9B9B9B, same font, alpha falloff, no scale transform
5. **Queue:** Compact header + pills row (Info/Playlist/Shuffle/Repeat)
6. **Revert:** Switch to "Default (Gradient)" → verify original Spotify layout unchanged
7. **Status bar:** Color matches seed-tinted gradient top
