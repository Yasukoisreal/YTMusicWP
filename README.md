# 🎵 YTMusicWP

![Windows Phone 8.1](https://img.shields.io/badge/Platform-Windows%20Phone%208.1-blue)
![Windows 10 Mobile](https://img.shields.io/badge/Platform-Windows%2010%20Mobile-blue)
![Language](https://img.shields.io/badge/Language-C%23%20%7C%20Python-green)

A modern, fully functional YouTube Music client designed specifically to breathe new life into legacy Windows Phone 8.1 devices (like the Lumia 530) and Windows 10 Mobile.

## ✨ Key Features
* **Background Audio Playback:** Listen to your favorite tracks seamlessly in the background with lock-screen controls.
* **Offline Downloads:** Download tracks directly to your device storage (`.m4a` format) for offline listening.
* **Real-time Synchronized Lyrics:** Fetches and displays perfectly synced lyrics, complete with UI highlighting and time-shift adjustments.
* **Google OAuth 2.0 Sync:** Login with your Google account to sync your "Liked Songs" directly from YouTube.
* **Playlist Management:** Create, edit, and manage custom offline playlists.
* **Modern UI:** A clean, pivot-based dark theme interface inspired by the official YouTube Music app.

## 🏗️ Architecture
This project consists of two main components to bypass legacy TLS limitations on WP8.1:
1. **Frontend (C# / WinRT):** The native Windows Phone 8.1 application handling the UI, background media player, and local storage.
2. **Backend API (Python / Flask):** A lightweight server script (`app.py`) that utilizes `yt-dlp` to cache YouTube audio files securely and stream them to the phone. It includes an auto-cleanup LRU cache mechanism to prevent server disk overflow.

## 🚀 Installation Guide

### For Windows Phone 8.1
1. Download the `YTMusicWP_..._AnyCPU.appxbundle` from the **Releases** page.
2. Connect your phone to your PC and deploy the bundle using **Windows Phone Application Deployment (WPAD)**.

### For Windows 10 Mobile (W10M)
*W10M users do not need a `.cer` certificate if installed correctly.*
1. On your phone, go to **Settings** > **Update & Security** > **For developers** and enable **Developer mode**.
2. Download the single **`YTMusicWP_..._ARM.appx`** file (Do not use the `.appxbundle`).
3. Open **Device Portal** via your browser or use tools like **WUT / Interop Tools**.
4. Navigate to the App Deployment section, select the `.appx` file, and tap **Install**.

## ⚙️ Backend Setup (For Developers)
If you want to host your own API server:
1. Upload the `app.py` and `requirements.txt` to a hosting service (e.g., Railway, Heroku).
2. Set your custom `APP_SECRET_KEY` in the environment variables.
3. In the WP8.1 app settings, ensure the API endpoints point to your new server URL.

## 👨‍💻 Developed By
**An (Yasuko)** - A passion project dedicated to the Windows Phone modding community.
