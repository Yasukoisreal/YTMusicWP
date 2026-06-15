# Phân tích TOÀN DIỆN MetroTube Patched (LazyTube)

> **Phiên bản**: 1.3.195.0 (patched 2026-05-03 bởi NLogDEV)  
> **Quét**: 4 DLL chính, 176,000+ dòng IL code, 60+ classes, 47 files + 3 thư mục

---

## 1. Cấu trúc gói ứng dụng

### Các DLL & vai trò

| DLL | Size | Dòng IL | Vai trò |
|-----|------|---------|---------|
| **LazyTube.dll** | 1.2 MB | 176,060 | App chính - UI, ViewModels, Settings, Player, OAuth Login |
| **Shared.dll** | 107 KB | 24,259 | Logic trích xuất stream URL (`PlayUrlRetriever`), data models |
| **AudioPlaybackAgent.dll** | 29 KB | 5,319 | Background audio agent - phát nhạc khi minimize |
| **TileUpdateAgent.dll** | 35 KB | — | Cập nhật Live Tile |

### Dependencies

| Library | Vai trò |
|---------|---------|
| `Google.Apis.YouTube.v3.dll` | YouTube Data API v3 (tìm kiếm, playlist, video info) |
| `Google.Apis.Auth.dll` | OAuth2 authentication cho Google Account |
| `Newtonsoft.Json.dll` | Parse JSON response |
| `HtmlAgilityPack.dll` | Parse HTML (Invidious embed pages) |
| `RestSharp.dll` | HTTP client |
| `Zlib.Portable.dll` | Giải nén gzip response |
| `System.Net.Http.dll` | HTTP requests |

### Class Map chính (LazyTube.dll)

```
LazyTube/
├── App                          # Application lifecycle, background audio switching
├── MainPage                     # Trang chủ
├── VideoPage                    # Trang phát video (~26K dòng IL - lớn nhất)
├── PlayerControl                # Media player control
├── SearchPage                   # Tìm kiếm video
├── PlaylistPage                 # Quản lý playlist
├── AuthorPage                   # Trang kênh
├── LoginPage                    # OAuth2 login
├── SettingsPage                 # Cấu hình (data source, API keys, proxy, quality)
├── RiverPage                    # Feed/Home
├── ViewModels/
│   ├── VideoItemViewModel       # Video item data
│   ├── PlaylistItemViewModel    # Playlist item
│   ├── AuthUserV3               # Authenticated user
│   ├── SearchVideosFeed         # Search results
│   ├── StartFeedViewModel       # Home feed
│   ├── SubscriptionsFeedViewModel  # Subscriptions
│   ├── HistoryViewModel         # Watch history
│   ├── SavedVideoViewModel      # Downloaded videos
│   └── CommentFeedViewModel     # Comments
└── Utils/
    ├── YouTubeConstants         # ⭐ API keys, OAuth credentials
    ├── Encryption               # AES encrypt/decrypt cho local storage
    ├── SaveVideoHelper          # Download video offline
    ├── BackgroundAgentHelper    # Quản lý background audio agent
    ├── IsolatedStorageHelper    # Save/load settings
    └── WebRequestUtils          # HTTP request utilities
```

---

## 2. Credentials & API Keys (Hardcoded)

> [!IMPORTANT]
> Các credentials này được hardcoded trong [YouTubeConstants](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L121699):

| Key | Giá trị | Mục đích |
|-----|---------|----------|
| **OAuth2 Client ID** | `170729868717-epodfoimm3bte91rpg06fbf79t92l5hj.apps.googleusercontent.com` | Google OAuth login |
| **OAuth2 Client Secret** | `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` | Google OAuth |
| **YouTube Data API Key** | `AIzaSyCYmowDVBO6n0_m-r2uhPGtGt2acT16B04` | Dùng cho search, channels, playlists, videos |
| **InnerTube Player Key** | `AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI` | Dùng trong `/youtubei/v1/player` |
| **Logout URL** | `https://accounts.google.com/o/oauth2/revoke?token={0}` | Revoke OAuth token |

> [!WARNING]
> Người dùng có thể **thay đổi** API key và Client Secret từ Settings → Keys. App lưu vào `IsolatedStorage` với key `ConfigYTAPIkey` và `ConfigClientSecret`.

---

## 3. Hệ thống Data Source Selection (5 chế độ)

Từ [SettingsPage](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L32530), người dùng chọn nguồn dữ liệu phát video, lưu vào `IsolatedStorage["PlayerVidData"]`:

| Chế độ | Key | Mô tả |
|--------|-----|-------|
| **Invidious** | `inv` | Dùng Invidious API + embed proxy |
| **YT2009** | `yt2009` | Dùng YT2009 proxy servers |
| **InnerTube** | `innertube` | Dùng YouTube InnerTube API (ANDROID_VR) |
| **Combined** | `all` | Kết hợp tất cả (Invidious + YT2009 + InnerTube song song) |
| **LegacyProjects** | `legacyprojects` | Dùng yt.legacyprojects.ru proxy |

### Proxy Options (cho Invidious)
- **Disabled** — Không proxy, stream trực tiếp từ googlevideo
- **VEVO Only** — Chỉ proxy video VEVO (thường bị geo-restrict)
- **All** — Proxy tất cả video qua Invidious

### Stream Quality Options
- **HD** (720p+)
- **HQ** (360p)
- **Low** (thấp nhất)

---

## 4. Chi tiết 5 Strategies trích xuất URL stream

### Strategy 1: Invidious API (Primary)

**Method**: [SendVideoRequest](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L5917) → [VideoRequestCallback](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L6026)

```
GET https://{instance}/api/v1/videos/{videoID}
```

**Instances (round-robin theo retryCount)**:
- `invidious.jing.rocks`
- `invidious.schenkel.eti.br`

**Parse response**:
```
json = JObject.Parse(response)
streams = json["formatStreams"].Merge(json["adaptiveFormats"])
foreach stream in streams:
    itag = stream["itag"]
    url = stream["url"]
    if itag == "18":  AddPlayUrl(HQDefault, url)   // 360p MP4
    if itag == "140": AddPlayUrl(Audio, url)         // M4A audio-only
```

Cũng parse `json["captions"]` để lấy phụ đề.

---

### Strategy 2: Invidious Embed Proxy

**Method**: [SendInvVideoRequest](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L6800) → [HandleVideoResponse](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L6944) → [HandleVideoUrlParsing](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L7109)

```
GET https://{instance}/embed/{videoID}?local=1
```

**5 Invidious instances** (method [getInvidiousInstance](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L6765)):
1. `yewtu.be`
2. `iv.duti.dev`
3. `invidious.schenkel.eti.br`
4. `lekker.gay`
5. `inv.nadeko.net`

**Parse HTML bằng regex**:
```regex
# Video (itag 18):
<source\s+src="([^"]*\?itag=18[^"]*\?local=true[^"]*\?)"[^>]*\?type=['"]video/mp4[^>]*>

# Audio (itag 140):
<source\s+src="([^"]*\?itag=140[^"]*\?local=true[^"]*\?)"[^>]*>
```

> `?local=true` buộc Invidious proxy stream qua server của họ.

---

### Strategy 3: YouTube InnerTube API (ANDROID_VR)

**Method**: [SendVideoRequestAuthYTi](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L8281)

**Bước 1** — Lấy `visitorData` ([ResolveVisitorData](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L9147)):
```
GET https://www.youtube.com/sw.js_data
User-Agent: Mozilla/5.0 (Linux; Andr0id 9; BRAVIA 8K UR2) AppleWebKit/537.36 ...
```
Parse: `JArray.Parse(response)[0][2][0][0][13]` → visitorData string

**Bước 2** — Gọi InnerTube Player:
```
POST https://www.youtube.com/youtubei/v1/player?key=AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI&prettyPrint=false&fields=captions,playabilityStatus,streamingData,playbackTracking
Content-Type: application/json
```

**Body**:
```json
{
  "contentCheckOk": true,
  "context": {
    "client": {
      "clientName": "ANDROID_VR",
      "clientVersion": "1.60.19",
      "deviceMake": "Oculus",
      "deviceModel": "Quest 3",
      "osName": "ANDROID",
      "osVersion": "12L",
      "platform": "Mobile",
      "visitorData": "{visitorData}",
      "hl": "en",
      "gl": "US",
      "clientScreen": 0
    }
  },
  "videoId": "{videoID}"
}
```

**Response**: Parse `streamingData.formats` + `streamingData.adaptiveFormats`

> [!NOTE]
> ANDROID_VR (Oculus Quest 3) client không yêu cầu DRM/signature verification → trả về URL stream trực tiếp.

---

### Strategy 4: YT2009 Proxy

**Method**: [SendYT2009VideoRequest](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L8150) → [YT2009Callback](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L8082)

```
GET http://{ip}/get_video_info?video_id={videoID}
```

**Server IPs**:
- `89.168.117.130` (default)
- `34.41.145.180` (retryCount == 2)

Người dùng có thể tùy chỉnh qua Settings → `ConfigYT2009Ins` / `PreferredYT2009Instance`.

**Parse**: Query string → `fmt_stream_map` → extract stream URLs

---

### Strategy 5: LegacyProjects Proxy

**Method**: [LegacyProjects_getYTLinks](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L9031)

```
1. HEAD https://yt.legacyprojects.ru          → Ping check
2. GET  https://yt.legacyprojects.ru/direct_url?video_id={videoID}   → 360p redirect
```

Chỉ hỗ trợ quality `360p`.

---

## 5. VideoQuality Enum & itag Mapping

```csharp
enum VideoQuality {
    Ask       = 0,     // Hỏi người dùng
    Low       = 1,     // Chất lượng thấp
    HQDefault = 2,     // itag 18 — 360p MP4 (video+audio)
    HQDesktop = 18,    // Desktop HQ
    HD        = 22,    // 720p
    FullHD    = 37,    // 1080p
    Audio     = 140    // itag 140 — 128kbps M4A (audio-only) ⭐
}
```

> [!TIP]
> Cho ứng dụng **nghe nhạc**, chỉ cần focus vào **itag=140** (M4A 128kbps audio-only). Tiết kiệm bandwidth, không cần video stream.

---

## 6. Background Audio Playback

### Kiến trúc

```mermaid
graph TD
    A["LazyTube App<br/>(Foreground)"] -->|"IsolatedStorage<br/>(encrypted AES)"| B["AudioPlaybackAgent<br/>(Background Process)"]
    B -->|"PlayUrlRetriever<br/>(Shared.dll)"| C["Lấy stream URL"]
    C --> D["BackgroundAudioPlayer<br/>(WP8 System API)"]
    D -->|"AudioTrack(url)"| E["Stream URL<br/>(googlevideo/invidious)"]
    
    F["User actions<br/>Next/Prev/Play/Pause"] -->|"BackgroundAudioPlayer<br/>UserAction"| B
```

### Data truyền qua IsolatedStorage ([AudioAgentContainer](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/AudioPlayback_decompiled_utf8.il#L125))

```csharp
class AudioAgentContainer {
    string VideoID;              // YouTube video ID hiện tại
    double Position;             // Vị trí phát (seconds)
    bool IsVideoPlaying;         // Đang phát video hay audio mode
    bool IsPlaylistMode;         // Chế độ playlist
    string PlaylistID;           // YouTube playlist ID
    long? PlaylistVideoCount;    // Tổng video trong playlist
    int InitialVideoPosition;    // Vị trí bắt đầu trong playlist
    string UserTimestamp;         // Timestamp
    TokenResponse Token;         // OAuth2 token (cho InnerTube)
    string RefreshToken;         // OAuth2 refresh token
    bool GetNextVideo;           // Flag: lấy bài tiếp
    bool GetPreviousVideo;       // Flag: lấy bài trước
    bool IsLastVideo;            // Flag: bài cuối cùng
}
```

### [AudioPlayer](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/AudioPlayback_decompiled_utf8.il#L511) class flow:
1. `OnPlayStateChanged` → Khi track kết thúc → `GetNextTrack()`
2. `GetNextTrack()` → `currentPlaylist.GetNextVideo()` → `PlayUrlRetriever.RetrieveMediaURL()`
3. Khi URL sẵn sàng → `UrlRetriever_RetrievingUrlComplete` → Set `BackgroundAudioPlayer.Track`

---

## 7. Encryption (Local Storage)

[Encryption class](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L113258) dùng **AES (AesManaged)** với **Rfc2898DeriveBytes** (PBKDF2):

```csharp
// Encrypt
Rfc2898DeriveBytes keyDerive = new(password, Encoding.UTF8.GetBytes(salt));
AesManaged aes = new();
aes.Key = keyDerive.GetBytes(aes.KeySize / 8);
aes.IV = keyDerive.GetBytes(aes.BlockSize / 8);
// CryptoStream → MemoryStream → Base64 string

// Decrypt: reverse process
```

Dùng để mã hóa OAuth tokens khi lưu vào IsolatedStorage.

---

## 8. Tính năng khác đáng chú ý

### Save Video Offline
- Class [SaveVideoHelper](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L118800) (~5K dòng)
- Download stream URL về IsolatedStorage
- Cho phép phát offline

### Auto Search Suggestions
```
GET http://suggestqueries.google.com/complete/search?client=youtube&q={query}
```

### Remote Config / Update Check
- `http://www.lazywormapps.com/parser.txt?lzc=` — Parser configuration
- `http://www.lazywormapps.com/recommended.txt?lzc=` — Recommended videos
- `http://www.lazywormapps.com/updates.xml` — Update check
- `http://www.metrotubeapp.com/ServerMessage.xml` — Server messages

### Video Thumbnails
```
http://i.ytimg.com/vi/{videoID}/default.jpg
```

### URI Association
App đăng ký xử lý:
- `metrotube://` — Deep link protocol
- `vnd.youtube://` — YouTube video links

---

## 9. Áp dụng cho dự án YTMusicWP

### Kiến trúc đề xuất

```mermaid
graph TD
    A["YTMusicWP"] --> B["YouTube Data API v3<br/>(AIzaSyCYmowDVBO6n0_...)"]
    A --> C["Stream Resolver<br/>(copy logic từ PlayUrlRetriever)"]
    
    C --> D["Primary: Invidious API<br/>/api/v1/videos/{id}"]
    C --> E["Fallback 1: InnerTube<br/>ANDROID_VR client"]  
    C --> F["Fallback 2: YT2009 proxy"]
    C --> G["Fallback 3: LegacyProjects"]
    
    D --> H["itag=140<br/>M4A 128kbps URL"]
    E --> H
    F --> H
    G --> H
    
    H --> I["AudioPlayerAgent<br/>(Background)"]
    I --> J["BackgroundAudioPlayer"]
```

### Checklist implement

- [ ] **Project structure**: Tạo 3 projects — App chính, Shared library, AudioPlaybackAgent
- [ ] **YouTube Data API**: Dùng `Google.Apis.YouTube.v3` cho search/browse
- [ ] **Stream resolver**: Port `PlayUrlRetriever` logic từ Shared.dll
  - [ ] Invidious API (`/api/v1/videos/{id}`)
  - [ ] InnerTube API (ANDROID_VR, Oculus Quest 3)
  - [ ] YT2009 fallback
  - [ ] Multi-instance retry system
- [ ] **Audio playback**: `AudioPlayerAgent` + `BackgroundAudioPlayer`
- [ ] **Data passing**: `IsolatedStorage` + `AudioAgentContainer` pattern
- [ ] **Settings**: Cho phép đổi data source, API key, Invidious instance
- [ ] **Offline**: Download itag=140 stream về local storage

### Code tham khảo nhanh — Lấy audio URL qua Invidious

```csharp
// Pseudo-code dựa trên phân tích IL
async Task<string> GetAudioUrl(string videoId) {
    string[] instances = { "yewtu.be", "iv.duti.dev", "invidious.schenkel.eti.br" };
    
    foreach (var instance in instances) {
        try {
            var url = $"https://{instance}/api/v1/videos/{videoId}";
            var response = await httpClient.GetStringAsync(url);
            var json = JObject.Parse(response);
            
            var formats = json["formatStreams"] as JArray;
            var adaptive = json["adaptiveFormats"] as JArray;
            formats.Merge(adaptive);
            
            foreach (var stream in formats) {
                if (stream["itag"].ToString() == "140") {
                    return stream["url"].ToString(); // M4A 128kbps
                }
            }
        } catch { continue; } // Try next instance
    }
    return null;
}
```

> [!CAUTION]
> - Invidious instances thường xuyên down/thay đổi — cần cơ chế tự động cập nhật danh sách
> - YouTube có thể thay đổi InnerTube API bất cứ lúc nào
> - OAuth2 Client ID/Secret ở trên là của MetroTube — **tạo credentials riêng** cho app của bạn tại [Google Cloud Console](https://console.cloud.google.com/)
> - Xem xét pháp lý: Trích xuất stream URL vi phạm YouTube ToS

---

## 10. Tóm tắt toàn bộ

MetroTube Patched phát YouTube video/audio bằng cách:

1. **Tìm kiếm** qua YouTube Data API v3 (Google Apis)
2. **Trích xuất stream URL** qua 5 nguồn (Invidious API → Invidious Embed → InnerTube ANDROID_VR → YT2009 → LegacyProjects) với retry/fallback tự động
3. **Lọc stream** theo itag: `18` (360p video) hoặc `140` (audio M4A 128kbps)
4. **Phát nền** qua `BackgroundAudioPlayer` API của WP8 thông qua `AudioPlaybackAgent`
5. **Lưu offline** qua `SaveVideoHelper` download stream về IsolatedStorage
6. **Bảo mật** OAuth tokens bằng AES encryption
7. **Cấu hình linh hoạt** — user có thể đổi data source, API keys, proxy settings, instance servers
