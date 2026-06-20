# 🎵 YTMusicWP

![Platform](https://img.shields.io/badge/Platform-Windows%20Phone%208.1-blue)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%20Mobile-blue)
![Language](https://img.shields.io/badge/Language-C%23-green)
![Version](https://img.shields.io/badge/Version-2.0%20BETA-orange)

A modern, fully functional YouTube Music client designed specifically to breathe new life into legacy Windows Phone 8.1 devices (like the Lumia 530) and Windows 10 Mobile.

## ✨ Key Features

- 🔍 **Discover Millions of Songs** — Search for any song, artist, or album, or explore 28 global category cards
- ⬇️ **Offline Downloads** — Download tracks directly to your device (`.m4a` format) for offline listening
- 🎤 **Synced Karaoke Lyrics** — Real-time lyrics that highlight word-by-word with adjustable sync delay
- 🎧 **Music Shorts** — Swipe through quick music previews organized by mood and genre
- 🔊 **Background Playback** — Listen with lock-screen controls, even when using other apps
- 🔗 **Google Account Sync** — Login via OAuth 2.0 Device Code flow to sync your Liked Songs
- 📋 **Playlist Management** — Create, edit, and manage custom playlists
- 🎛️ **Audio Quality Options** — Choose between 48kbps, 128kbps, or 256kbps
- 🔀 **Crossfade & Gapless Playback** — Smooth transitions between tracks
- 🌍 **Trending by Region** — Explore trending music from 80+ countries
- 🎭 **Your Top Mixes** — Chill, Focus, Energy & Sad mood mixes curated for you
- 👤 **Artist Pages** — Browse any artist's discography, albums & related artists
- 🎨 **Modern Dark UI** — A gorgeous dark theme interface inspired by Spotify

## 🏗️ Architecture

This is a fully native Windows Phone 8.1 application — **no external server required**.

- **Frontend:** C# / WinRT with XAML-based UI
- **API:** Direct InnerTube API calls (same API used by the official YouTube Music app)
- **Audio:** Background audio task with `MediaPlayer` for lock-screen playback
- **Storage:** Local settings + file-based caching for offline tracks and playlists

## 🚀 Installation

### Windows Phone 8.1
1. Download `YTMusicWP_2.0_BETA.appx` and `YTMusicWP_2.0_BETA.cer` from [Releases](https://github.com/Yasukoisreal/YTMusicWP/releases).
2. Install the `.cer` certificate on your phone first.
3. Deploy the `.appx` using **Windows Phone Application Deployment (WPAD)**.

### Windows 10 Mobile
1. Go to **Settings** > **Update & Security** > **For developers** and enable **Developer mode**.
2. Download the `.appx` file (do not use `.appxbundle`).
3. Install via **Device Portal** or **Interop Tools**.

## 📋 Changelog

### v2.0 BETA
- 🔄 Switched to InnerTube API — faster, more reliable, no quota limits
- 🎨 Brand new UI — modern dark theme, animations, floating mini-player
- 🔐 New easy login — Device Code flow, just enter a code at google.com/device
- ⚡ Faster playback & reduced API calls
- 🐛 Fixed Play All, search suggestions, and memory leaks
- 📱 Better stability on 512MB RAM devices

### v1.2.1
- Initial public release with Python backend

## ⚡ Optimized for Low-End Devices

Built and tested on devices with only 512MB RAM:
- Lumia 520, 530, 535
- Lumia 630, 730
- Any Windows Phone 8.1 or Windows 10 Mobile device

## 👨‍💻 Developed By

**An (Yasuko)** — A passion project dedicated to the Windows Phone modding community.
