---
name: mainpage-xaml-split
description: State of splitting MainPage.xaml into UserControls (done Sep 2026) and why the remaining screens were left in place
metadata:
  node_type: memory
  type: project
  originSessionId: 41ddc17e-1f6a-4c60-814b-c985e3c2a866
  modified: 2026-09-27T15:49:20.813Z
---

On 2026-09-27 MainPage.xaml was cut from 4,661 to ~2,507 lines: keyed styles → Themes/Styles.xaml; fonts + converters → App.xaml; screens/dialogs → YTMusicWP/Controls/* (SongCreditsDialog, CreateSheet, TrackActionsSheet, LiveDebugDialog, NowPlayingMenu, SettingsView, LibraryView, ListenTogetherPanel, ShortsPanel, LoginWebPanel).

Pattern: keep the original x:Name on the control element; inner named elements use x:FieldModifier="internal" and code refers to them as e.g. SettingsPanel.LoginStatusText; button handlers are forwarded as control events wired in MainPage XAML (original sender kept); each control repeats the implicit Montserrat TextBlock style.

The user chose to stop there. Home, Search, NowPlaying, PlaylistDetails, ArtistProfile and MoodCategory still live in MainPage because they use page DataTemplates (SongItemTemplate, SearchResultItemTemplate, QueueItemTemplate, HomeSectionSelector…) that carry ~91 MainPage event handlers. Only revisit if VS 2015 keeps crashing on MainPage.xaml; start with PlaylistDetailsView.
