# 🎵 YTMusicWP

<p align="center">
  <img src="YTMusicWP/Assets/Logo.scale-240.png" width="120" height="120" alt="YTMusicWP Logo" />
</p>

<p align="center">
  <strong>A modern, lightning-fast native YouTube Music client crafted for Windows Phone 8.1 and Windows 10 Mobile.</strong><br>
  <em>Breathe new life into legacy Lumia devices with direct stream playback, synced karaoke lyrics, iconic Live Tiles, and seamless Google login — fully optimized for 512MB RAM devices.</em>
</p>

<p align="center">
  <a href="https://github.com/Yasukoisreal/YTMusicWP/releases"><img src="https://img.shields.io/badge/Platform-Windows%20Phone%208.1%20%7C%20W10M-0078D7?logo=windows" alt="Platform"></a>
  <a href="https://github.com/Yasukoisreal/YTMusicWP"><img src="https://img.shields.io/badge/Language-C%23%20%2F%20XAML-239120?logo=c-sharp" alt="Language"></a>
  <a href="https://github.com/Yasukoisreal/YTMusicWP"><img src="https://img.shields.io/badge/RAM%20Target-512MB%20Optimized-orange" alt="RAM Target"></a>
  <a href="https://github.com/Yasukoisreal/YTMusicWP/releases"><img src="https://img.shields.io/badge/Version-2.1%20BETA-blue" alt="Version"></a>
  <a href="https://github.com/Yasukoisreal/YTMusicWP/blob/main/LICENSE"><img src="https://img.shields.io/badge/License-MIT-green" alt="License"></a>
</p>

---

## ✨ Features at a Glance

### 🎵 1. Native Background Playback (`AudioPlayerTask`)
- **Direct Stream Playback:** High-performance direct m4a streaming directly from YouTube's infrastructure without third-party proxy latency.
- **System Media Controls (SMTC):** Full hardware volume bar, lock screen controls, and headset media button integration.
- **Gapless & Crossfade:** Continuous music streaming with pre-resolving and customizable crossfade transitions (1s – 10s).
- **Auto-Loop & Smart Queue:** Queue manipulation, shuffle mode, repeat one/all, and automatic infinite playback recommendations.

### 🎤 2. Real-Time Synced Karaoke Lyrics
- **Synchronized Lyrics:** Smooth scrolling lyrics highlight automatically in real time as the song plays.
- **Dual Engine:** Aggregates time-synced lyrics from LRCLIB and embedded YouTube closed-caption subtitle tracks.
- **Customizable Experience:** Adjustable lyric font size slider, dynamic color gradients, and full-screen lyrics viewing mode.

### 🎨 3. Iconic Metro Live Tiles
- **Now Playing Flip Tile:** Live album artwork and track metadata flip dynamically on your Start Screen.
- **People Hub Style Mosaic (2x2 / 3x3):** Unique mosaic grid blending album artwork of your favorite artists and liked tracks inspired by the classic Windows Phone People Hub.
- **Pin Secondary Tiles:** Pin any artist, album, or playlist directly to your Start Screen for instant one-tap access.

### 🔑 4. Seamless Google Account Integration
- **Zero-Browser OAuth 2.0 Device Flow:** Log in securely via `google.com/device` using a code or by scanning a built-in on-screen **QR Code** (ISO/IEC 18004 native generator).
- **Full Library Sync:** Synchronizes your Liked Music, custom YouTube playlists, and subscribed artists.

### 📥 5. Offline Downloads & Library Hub
- **Save Offline:** Download songs directly to your device storage (`.m4a`) to enjoy music with zero internet connection.
- **Library Management:** Manage local downloads, create custom local playlists, and track playback history.

### 🎧 6. Shorts Music Discovery
- **Vertical Swipe Feed:** Discover trending music snippets with smooth vertical gestures and intelligent mood/genre categorization.

### ⚡ 7. Engineered for 512MB RAM Devices
- **Aggressive Memory Management:** Strict `DecodePixelWidth` downsampling, brush caching, and surface disposal to ensure zero Out-Of-Memory crashes even on devices like the Nokia Lumia 520 / 530.

---

## 📸 Screenshots

| | | | | | | | |
|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| <img src="Pictures/01.png" width="250"> | <img src="Pictures/02.png" width="250"> | <img src="Pictures/03.png" width="250"> | <img src="Pictures/04.png" width="250"> | <img src="Pictures/05.png" width="250"> | <img src="Pictures/06.png" width="250"> | <img src="Pictures/07.png" width="250"> | <img src="Pictures/08.png" width="250"> |

---

## 🏛️ Architecture

YTMusicWP is built natively on the **Windows Runtime (WinRT)** architecture for maximum speed and battery efficiency:

```text
┌──────────────────────────────────────────────────────────────────────────┐
│                         YTMusicWP (Foreground UI)                        │
│  XAML Metro Pages  •  InnerTube Client  •  TileService  •  QR Generator  │
└─────────────────────────────────────┬────────────────────────────────────┘
                                      │ ValueSet IPC & LocalSettings
┌─────────────────────────────────────▼────────────────────────────────────┐
│                      AudioPlayerTask (Background Task)                   │
│  IBackgroundTask  •  BackgroundMediaPlayer  •  Direct m4a Stream Resolver│
└──────────────────────────────────────────────────────────────────────────┘
```

- **Frontend:** C# 5.0, XAML, WinRT Windows Store Apps framework.
- **Background Task:** Isolated `Windows.ApplicationModel.Background.IBackgroundTask` running under `BackgroundMediaPlayer`.
- **API Engine:** Custom direct `InnerTubeClient` leveraging the `ANDROID_VR` client endpoint with lightweight `sw.js_data` visitorData token generation.
- **Storage:** `Windows.Storage.ApplicationData` with encrypted tokens in `LocalSettings` and serialized JSON collections.

---

## 📱 Supported Devices

| Category | Tested Models | Status |
| :--- | :--- | :--- |
| **512MB Low-End** | Lumia 520, 525, 530, 535, 620, 625, 630, 635 | ✅ Ultra-Smooth (Optimized) |
| **1GB+ Mid-Range & Flagships** | Lumia 720, 730/735, 820, 830, 920, 925, 930, 1020, 1520, Icon | ✅ Flawless Experience |
| **Windows 10 Mobile** | Lumia 550, 640/640 XL, 650, 950/950 XL, HP Elite x3, Alcatel Idol 4S | ✅ Fully Compatible |

---

## 🚀 Installation Guide

### Option A: Windows Phone 8.1
1. Download the latest `YTMusicWP.appx` and certificate `YTMusicWP.cer` from [Releases](https://github.com/Yasukoisreal/YTMusicWP/releases).
2. Install the `.cer` certificate on your Lumia device first (open via email or file manager).
3. Sideload the `.appx` using **Windows Phone Application Deployment (WPAD)**, **WPV Xap Deployer**, or **Windows Phone Power Tools**.

### Option B: Windows 10 Mobile
1. Navigate to **Settings** > **Update & Security** > **For developers** and enable **Developer mode**.
2. Download the `.appx` package to your phone.
3. Open the file in **File Explorer** and tap **Install**, or deploy via **Windows Device Portal**.

---

## 🛠️ Building from Source

### Prerequisites
- Windows 8.1 / 10 / 11
- Visual Studio 2015 (with Windows Phone 8.1 SDK installed)
- MSBuild v14.0

### Build Instructions
```powershell
# Clone the repository
git clone https://github.com/Yasukoisreal/YTMusicWP.git
cd YTMusicWP

# Restore NuGet packages
.\nuget.exe restore YTMusicWP.sln

# Build ARM configuration
& "C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" YTMusicWP.sln /p:Configuration=Release /p:Platform=ARM
```

---

## 📝 Changelog

### v2.1 BETA (Latest)
- 🔐 **QR Code Login:** Added QR Code to make the login process more convenient.
- 🖼️ **Live Tiles:** Added Live Tile support for the Start Screen.
- ⚡ **Performance:** Bug fixes and memory optimizations.

---

## 🤝 Contributing

Contributions, bug reports, and pull requests are warmly welcome!
- Found a bug? Open an [Issue](https://github.com/Yasukoisreal/YTMusicWP/issues).
- Want to add a feature? Fork the repository and submit a PR.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).

---

<p align="center">
  Crafted with ❤️ for the Windows Phone & Lumia community by <strong>Yasuko (An)</strong>.
</p>
