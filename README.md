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
  <a href="https://github.com/Yasukoisreal/YTMusicWP/releases"><img src="https://img.shields.io/badge/Version-2.1.3.1%20BETA-blue" alt="Version"></a>
  <a href="https://github.com/Yasukoisreal/YTMusicWP/blob/main/LICENSE"><img src="https://img.shields.io/badge/License-MIT-green" alt="License"></a>
</p>

## ✨ Features at a Glance

### 🎵 1. Background Playback
- **Direct Streaming:** Listen to high-quality music instantly, straight from YouTube without delays.
- **Media Controls:** Control your music using the volume buttons, from the lock screen, or with your headset.
- **Crossfade:** Smooth transitions between songs so the music never stops (adjustable from 1s to 10s).
- **Smart Queue:** Shuffle, repeat, and let the app automatically recommend songs to keep the music playing forever.

### 🎤 2. Real-Time Synced Lyrics
- **Synchronized Lyrics:** Smooth scrolling lyrics highlight automatically in real time as the song plays.
- **Huge Database:** Automatically finds the best lyrics for your songs.
- **Customizable Experience:** Adjustable lyric text size, colors, and full-screen viewing mode.

### 🎨 3. Iconic Metro Live Tiles
- **Now Playing Flip Tile:** Live album artwork and track info flip dynamically on your Start Screen.
- **People Hub Style Mosaic:** Unique mosaic grid blending album artwork of your favorite artists and liked tracks.
- **Pin Secondary Tiles:** Pin any artist, album, or playlist directly to your Start Screen for instant one-tap access.

### 🔑 4. Seamless Google Account Integration
- **Easy Login:** Log in securely by simply scanning a QR Code with your phone.
- **Full Library Sync:** Synchronizes your Liked Music, custom YouTube playlists, and subscribed artists.

### 📥 5. Offline Downloads & Library
- **Save Offline:** Download songs directly to your phone to enjoy music without internet.
- **Library Management:** Manage local downloads, create custom local playlists, and track playback history.

### 🎧 6. Shorts Music Discovery
- **Vertical Swipe Feed:** Discover trending music snippets with smooth vertical gestures and intelligent mood/genre categorization.

### ⚡ 7. Engineered for 512MB RAM Devices
- **Highly Optimized:** Carefully built to run perfectly without crashing, even on older phones with just 512MB RAM like the Nokia Lumia 520.

---

## 📸 Screenshots

<p align="center">
  <img src="Pictures/01.png" width="22%"> &nbsp;
  <img src="Pictures/02.png" width="22%"> &nbsp;
  <img src="Pictures/03.png" width="22%"> &nbsp;
  <img src="Pictures/04.png" width="22%">
</p>
<p align="center">
  <img src="Pictures/05.png" width="22%"> &nbsp;
  <img src="Pictures/06.png" width="22%"> &nbsp;
  <img src="Pictures/07.png" width="22%"> &nbsp;
  <img src="Pictures/08.png" width="22%">
</p>

---


## 📱 Supported Devices

| Category | Tested Models | Status |
| :--- | :--- | :--- |
| **512MB Low-End** | Lumia 520, 525, 530, 535, 620, 625, 630, 635 | ✅ Ultra-Smooth (Optimized) |
| **1GB+ Mid-Range & Flagships** | Lumia 720, 730/735, 820, 830, 920, 925, 930, 1020, 1520, Icon | ✅ Flawless Experience |
| **Windows 10 Mobile** | Lumia 550, 640/640 XL, 650, 950/950 XL, HP Elite x3, Alcatel Idol 4S | ✅ Fully Compatible |

---

## 🚀 Installation Guide

### Method 1: Live Store (Recommended)
The easiest way to install and update YTMusicWP is directly from the Live Store. Click the button below to download the app seamlessly to your device:

<p>
  <a href="https://store.live.net.co/app/447"><img src="https://edge.live.net.co/images/store/2025_GetButton_SmallBlack.png" alt="Get YTMusicWP from Live Store"></a>
</p>

### Method 2: Manual Sideloading

#### Option A: Windows Phone 8.1
1. Download the latest `YTMusicWP.appx` and certificate `YTMusicWP.cer` from [Releases](https://github.com/Yasukoisreal/YTMusicWP/releases).
2. Install the `.cer` certificate on your Lumia device first (open via email or file manager).
3. Sideload the `.appx` using **Windows Phone Application Deployment (WPAD)**, **WPV Xap Deployer**, or **Windows Phone Power Tools**.

#### Option B: Windows 10 Mobile
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

### v2.1.3.1 BETA (Latest)
- 🛠️ **Hotfix:** Fixed an issue where the "Liked Songs" playlist would not sync or was missing information (titles, covers).
- 🛠️ **Hotfix:** Fixed an issue where the app would crash when loading the Library tab.

### v2.1.3 BETA
- 🛠️ **Hotfix:** Restored music playback after YouTube server changes caused songs to load indefinitely.

### v2.1.2 BETA
- 🛠️ **Hotfix:** Fixed a critical bug causing "No stream available" errors.

### v2.1.1 BETA
- 🛠️ **Hotfix:** Fixed a bug in the Library tab that caused the app to crash on older phones (like Lumia 520).

### v2.1 BETA
- 🔐 **QR Code Login:** Added QR Code to make the login process more convenient.
- 🖼️ **Live Tiles:** Added Live Tile support for the Start Screen.
- ⚡ **Performance:** Bug fixes and memory optimizations.

---

## 🤝 Contributing

Contributions, bug reports, and pull requests are warmly welcome!
- Found a bug? Open an [Issue](https://github.com/Yasukoisreal/YTMusicWP/issues).
- Want to add a feature? Fork the repository and submit a PR.

---

## 💖 Support & Donate

If you enjoy using YTMusicWP and want to support the development, consider buying me a coffee! Your support helps keep this project alive for the Windows Phone & Lumia community.

**International (PayPal)**
- **PayPal.me:** [paypal.me/yasukoisreal](https://paypal.me/yasukoisreal)

**Vietnam (MB Bank)**
- **Account Number:** `700652007`
- **Account Name:** NGUYEN TRUONG AN

<img src="Pictures/donate_qr.jpg" width="300" alt="Donate QR Code">

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).

---

<p align="center">
  Crafted with ❤️ for the Windows Phone & Lumia community by <strong>Yasuko (An)</strong>.
</p>
