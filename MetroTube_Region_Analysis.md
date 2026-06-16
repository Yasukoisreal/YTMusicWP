# Báo cáo: Cơ chế load trang chủ theo quốc gia — MetroTube Patched

> **Ứng dụng**: MetroTube Patched (LazyTube) v1.3.195.0  
> **Ngày phân tích**: 2026-06-16  
> **Phương pháp**: Reverse-engineering IL Assembly (ildasm) từ LazyTube.dll

---

## 1. Tổng quan

MetroTube sử dụng YouTube Data API v3 với tham số **`regionCode`** để hiển thị nội dung trang chủ theo từng quốc gia. Region code ảnh hưởng đến:
- Video **trending/popular** trên trang chủ
- Video **trending theo category** (Music, Gaming, News...)
- Kết quả **tìm kiếm** ưu tiên nội dung local
- **Ngôn ngữ giao diện** app

```mermaid
graph TD
    A["App khởi động"] --> B{"IsolatedStorage\ncó key 'Region'?"}
    B -->|Có| C["Lấy region từ storage"]
    B -->|Không| D["Auto-detect từ OS\nRegionInfo.TwoLetterISORegionName"]
    D --> E{"Region có trong\nSupportedRegions?"}
    E -->|Có| F["Dùng detected region\n& lưu vào storage"]
    E -->|Không| G["Region = empty\n→ Worldwide"]
    C --> H["App.CurrentRegion = region code"]
    F --> H
    G --> H
    H --> I["YouTube API calls\nRegionCode = CurrentRegion"]
    
    I --> J["StartFeedViewModel\nTrending videos"]
    I --> K["CategoryFeedViewModel\nTrending theo category"]
    I --> L["SearchVideosFeed\nKết quả tìm kiếm"]
```

---

## 2. Danh sách 75 quốc gia hỗ trợ

Dictionary [App.SupportedRegions](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L4659) (khởi tạo trong `App..cctor`):

| Code | Quốc gia | Code | Quốc gia | Code | Quốc gia |
|------|----------|------|----------|------|----------|
| DZ | Algeria | HU | Hungary | PH | Philippines |
| AR | Argentina | IS | Iceland | PL | Poland |
| AU | Australia | IN | India | PT | Portugal |
| AT | Austria | ID | Indonesia | PR | Puerto Rico |
| AZ | Azerbaijan | IQ | Iraq | QA | Qatar |
| BH | Bahrain | IE | Ireland | RO | Romania |
| BD | Bangladesh | IL | Israel | RU | Russia |
| BY | Belarus | IT | Italy | SA | Saudi Arabia |
| BE | Belgium | JP | Japan | SN | Senegal |
| BO | Bolivia | JO | Jordan | RS | Serbia |
| BA | Bosnia | KE | Kenya | SG | Singapore |
| BR | Brazil | KW | Kuwait | SK | Slovakia |
| BG | Bulgaria | LV | Latvia | SI | Slovenia |
| CA | Canada | LB | Lebanon | ZA | South Africa |
| CL | Chile | LT | Lithuania | KR | South Korea |
| CO | Colombia | MK | Macedonia | ES | Spain |
| CR | Costa Rica | MY | Malaysia | SE | Sweden |
| HR | Croatia | MX | Mexico | CH | Switzerland |
| CZ | Czech Republic | ME | Montenegro | TW | Taiwan |
| DK | Denmark | MA | Morocco | TH | Thailand |
| DO | Dominican Republic | NL | Netherlands | TN | Tunisia |
| EC | Ecuador | NZ | New Zealand | TR | Turkey |
| EG | Egypt | NG | Nigeria | UG | Uganda |
| SV | El Salvador | NO | Norway | UA | Ukraine |
| EE | Estonia | OM | Oman | AE | UAE |
| FI | Finland | PE | Peru | GB | United Kingdom |
| FR | France | — | — | US | United States |
| GE | Georgia | — | — | **VN** | **Vietnam** ✅ |
| DE | Germany | — | — | YE | Yemen |
| GH | Ghana | — | — | — | — |
| GR | Greece | — | — | — | — |
| GT | Guatemala | — | — | — | — |
| HN | Honduras | — | — | — | — |
| HK | Hong Kong | — | — | — | — |

> [!NOTE]
> **Việt Nam (VN) được hỗ trợ** → Nếu điện thoại đặt region VN, app sẽ tự động hiển thị video trending Việt Nam.

---

## 3. Cơ chế khởi tạo Region

### 3.1. Khi app khởi động ([App.ctor](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L2858))

```csharp
// Pseudo-code từ IL (dòng 2869-2913)
App() {
    // 1. Kiểm tra ngôn ngữ đã lưu
    if (IsolatedStorageHelper.ContainsKey("CurrentLanguage")) {
        currentLanguage = IsolatedStorageHelper.GetPrimitive<string>("CurrentLanguage");
        SetLanguage(currentLanguage);
    } else {
        // 2. Auto-detect ngôn ngữ theo quốc gia OS
        string osRegion = RegionInfo.CurrentRegion.TwoLetterISORegionName;
        
        if (osRegion == "JP") {
            currentLanguage = "ja-JP";
            SetLanguage("ja-JP");
            IsolatedStorageHelper.SavePrimitive("CurrentLanguage", "ja-JP");
        }
        else if (osRegion == "DE") {
            currentLanguage = "de-DE";
            SetLanguage("de-DE");
            IsolatedStorageHelper.SavePrimitive("CurrentLanguage", "de-DE");
        }
        // Mặc định: English (không set)
    }
}
```

### 3.2. Khi MainPage load ([MainPage.OnNavigatedTo](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L20326))

```csharp
// Pseudo-code từ IL (dòng 20375-20412)
void OnNavigatedTo() {
    string regionCode = "";
    
    // 1. Kiểm tra region đã lưu trong Settings
    if (IsolatedStorageHelper.ContainsKey("Region")) {
        regionCode = IsolatedStorageHelper.GetPrimitive<string>("Region");
    } else {
        // 2. Auto-detect từ OS
        string osRegion = RegionInfo.CurrentRegion
            .TwoLetterISORegionName
            .ToUpper(CultureInfo.InvariantCulture);
        
        // 3. Kiểm tra có trong danh sách hỗ trợ không
        if (App.SupportedRegions.ContainsKey(osRegion)) {
            regionCode = osRegion;
            IsolatedStorageHelper.SavePrimitive("Region", regionCode);
        }
        // Không hỗ trợ → regionCode = "" (Worldwide)
    }
    
    // 4. Set region nếu khác hiện tại (tránh reload lại)
    if (!regionCode.Equals(App.CurrentRegion, StringComparison.OrdinalIgnoreCase)) {
        SetRegion(regionCode);
    }
    
    // 5. Load data nếu lần đầu
    if (NavigationMode == New && !showRecommended) {
        LoadFeaturedData();
    }
}
```

### 3.3. SetRegion method ([MainPage.SetRegion](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L20229))

```csharp
// Pseudo-code từ IL (dòng 20229-20261)
void SetRegion(string regionCode) {
    // 1. Validate region code
    if (!string.IsNullOrEmpty(regionCode) && App.SupportedRegions.ContainsKey(regionCode)) {
        // Valid → giữ nguyên
    } else {
        regionCode = "";  // Worldwide
    }
    
    // 2. Clear data cũ (buộc reload)
    FeaturedViewModel.ClearData();   // Xóa video trending
    TopRatedViewModel.ClearData();   // Xóa video top rated
    
    // 3. Set region mới
    App.CurrentRegion = regionCode;
}
```

---

## 4. Cách YouTube API sử dụng Region Code

### 4.1. Load Trending (Trang chủ)

**Class**: [StartFeedViewModel.SendRequest](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L94500)

```csharp
// Pseudo-code từ IL (dòng 94549-94579)
async void SendRequest() {
    var youTubeService = YouTubeServiceManager.GetYouTubeVideoChannelService();
    
    // 1. Tạo request Videos.List
    var request = youTubeService.Videos.List("snippet, statistics, contentDetails");
    request.MaxResults = 10;
    request.Chart = ChartEnum.MostPopular;     // ⭐ Video trending
    request.VideoCategoryId = "0";             // Tất cả categories
    
    // 2. Set RegionCode ← App.CurrentRegion
    request.RegionCode = string.IsNullOrWhiteSpace(App.CurrentRegion) 
        ? null                                  // null = worldwide
        : App.CurrentRegion;                    // "VN", "US", "JP"...
    
    // 3. Execute
    var videoResponse = await request.ExecuteAsync();
    
    // 4. Lấy channel info cho mỗi video
    var channelRequest = youTubeService.Channels.List("snippet");
    foreach (var video in videoResponse.Items) {
        channelRequest.Id += video.Snippet.ChannelId + ",";
    }
    var channelResponse = await channelRequest.ExecuteAsync();
    
    // 5. Convert → ViewModels hiển thị UI
    ConvertToViewModels(videoResponse, channelResponse);
}
```

**API call thực tế**:
```
GET https://www.googleapis.com/youtube/v3/videos
  ?part=snippet,statistics,contentDetails
  &chart=mostPopular
  &videoCategoryId=0
  &regionCode=VN          ← Region code
  &maxResults=10
  &key=AIzaSyCYmowDVBO6n0_m-r2uhPGtGt2acT16B04
```

### 4.2. Layout trang chủ

`ConvertToViewModels` chuyển kết quả thành layout 5 ô:

```
┌─────────────────────────────────┐
│                                 │
│           MainItem              │  ← Video #1 (lớn, nổi bật)
│        (Video trending #1)      │
│                                 │
├────────────────┬────────────────┤
│   SubItem1     │   SubItem2     │  ← Video #2, #3
│   (Video #2)   │   (Video #3)   │
├────────────────┼────────────────┤
│   SubItem3     │   SubItem4     │  ← Video #4, #5
│   (Video #4)   │   (Video #5)   │
└────────────────┴────────────────┘
```

### 4.3. Trending theo Category

Nhiều nơi trong app gọi `App.CurrentRegion` cho các feed khác nhau — tổng cộng **20+ chỗ** sử dụng:

| Component | Dòng IL | API Parameter |
|-----------|---------|---------------|
| [StartFeedViewModel](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L94570) | 94570 | `Videos.List → RegionCode` |
| [CategoryFeed #1](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L56967) | 56967 | `Videos.List → RegionCode` |
| [CategoryFeed #2](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L57364) | 57364 | `Videos.List → RegionCode` |
| [SearchFeed](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L89702) | 89702 | `Search.List → RegionCode` |
| [HistoryFeed](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L70415) | 70415 | `Videos.List → RegionCode` |
| [SubscriptionFeed](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L92489) | 92489 | `Videos.List → RegionCode` |
| [StartFeed extended](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L95698) | 95698 | `Videos.List → RegionCode` |

---

## 5. Categories hỗ trợ

Từ [App.AvailableCategories](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L2004) — 15 categories:

| Category ID | Tên | String Resource |
|-------------|-----|-----------------|
| 0 | All | `Category_All` |
| 15 | Animals | `Category_Animals` |
| 2 | Autos & Vehicles | `Category_Autos` |
| 23 | Comedy | `Category_Comedy` |
| 27 | Education | `Category_Education` |
| 24 | Entertainment | `Category_Entertainment` |
| 1 | Film & Animation | `Category_Film` |
| 20 | Gaming | `Category_Games` |
| 26 | How-to & Style | `Category_Howto` |
| **10** | **Music** | `Category_Music` ⭐ |
| 25 | News & Politics | `Category_News` |
| 22 | People & Blogs | `Category_People` |
| 17 | Sports | `Category_Sports` |
| 28 | Science & Tech | `Category_Tech` |
| 19 | Travel & Events | `Category_Travel` |

Khi user chọn category (ví dụ Music), API call trở thành:
```
GET https://www.googleapis.com/youtube/v3/videos
  ?part=snippet,statistics,contentDetails
  &chart=mostPopular
  &videoCategoryId=10           ← Music
  &regionCode=VN                ← Region
  &maxResults=10
  &key=...
```

→ Trả về **video Music trending tại Việt Nam**.

---

## 6. Ngôn ngữ giao diện

### Auto-detect ngôn ngữ

Từ [App.ctor](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L2869):

| OS Region | Ngôn ngữ app | Resource file |
|-----------|-------------|---------------|
| JP | `ja-JP` | `de-DE/` folder |
| DE | `de-DE` | `ja-JP/` folder |
| *Tất cả khác* | English (default) | Trong DLL chính |

### Resources đi kèm

```
d:\Downloads\941f6817fc96fa7ae33fe31031c816dc\
├── de-DE/                    # German resources
│   └── LazyTube.resources.dll
└── ja-JP/                    # Japanese resources
    └── LazyTube.resources.dll
```

### SetLanguage method

Thay đổi `Thread.CurrentThread.CurrentCulture` và `CurrentUICulture` → WP8 tự động load resource DLL tương ứng → toàn bộ UI string (category names, button labels, messages) hiển thị đúng ngôn ngữ.

---

## 7. Settings: Thay đổi Region thủ công

### UI trong SettingsPage

[SettingsPage](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L30660) hiển thị ListPicker `LstPkrRegion`:

```
┌─────────────────────────────────┐
│ Settings                        │
├─────────────────────────────────┤
│                                 │
│ Region           [Vietnam    ▼] │  ← LstPkrRegion (75 quốc gia + Worldwide)
│                                 │
│ Language          [English   ▼] │  ← LstPkrLanguage
│                                 │
│ ⚠ Restart app to apply changes │  ← GrdRestartReminderLanguage
│                                 │
└─────────────────────────────────┘
```

### Handler

[LstPkrRegion_SelectionChanged](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L31463):

```csharp
// Pseudo-code từ IL (dòng 31463-31489)
void LstPkrRegion_SelectionChanged() {
    if (!hasLoaded) return;  // Tránh trigger khi khởi tạo
    
    var selected = LstPkrRegion.SelectedItem as RegionInfo;
    if (selected == null) return;
    
    // 1. Lưu region code vào IsolatedStorage
    IsolatedStorageHelper.SavePrimitive("Region", selected.code);
    
    // 2. Reset Live Tile (vì trending thay đổi)
    TileImageHelper.ResetMainTile();
}
```

### RegionInfo struct

```csharp
struct SettingsPage.RegionInfo {
    string code;    // "VN", "US", "JP"...
    // display name lấy từ SupportedRegions dictionary
}
```

Khi user chọn region mới:
1. Lưu vào `IsolatedStorage["Region"]`
2. Reset Live Tile
3. Lần tiếp theo `MainPage.OnNavigatedTo` sẽ đọc region mới → `SetRegion()` → Clear cached data → Reload trending

---

## 8. Flow hoàn chỉnh: Ví dụ Việt Nam

```mermaid
sequenceDiagram
    participant P as 📱 Phone<br/>(Region: VN)
    participant A as 🏠 App
    participant S as ⚙️ Settings
    participant Y as 📺 YouTube API

    Note over P,A: Lần đầu mở app
    P->>A: App.ctor()
    A->>A: RegionInfo.CurrentRegion = "VN"
    A->>A: Language auto-detect: VN → English (default)
    
    A->>A: MainPage.OnNavigatedTo()
    A->>A: IsolatedStorage["Region"] not found
    A->>A: OS region "VN" ∈ SupportedRegions ✓
    A->>A: Save "Region" = "VN"
    A->>A: App.CurrentRegion = "VN"
    
    A->>Y: Videos.List(chart=mostPopular, regionCode=VN, categoryId=0)
    Y->>A: Top 10 trending videos Việt Nam
    A->>P: Hiển thị: Sơn Tùng, Đen Vâu, etc.
    
    Note over P,A: User chọn category Music
    A->>Y: Videos.List(chart=mostPopular, regionCode=VN, categoryId=10)
    Y->>A: Top trending Music VN
    A->>P: Hiển thị nhạc trending VN
    
    Note over P,S: User đổi sang US trong Settings
    P->>S: LstPkrRegion → "United States"
    S->>S: IsolatedStorage["Region"] = "US"
    S->>A: MainPage reload
    A->>A: App.CurrentRegion = "US"
    A->>Y: Videos.List(chart=mostPopular, regionCode=US, categoryId=0)
    Y->>A: Top trending US
    A->>P: Hiển thị MrBeast, Taylor Swift, etc.
```

---

## 9. So sánh: Region vs Worldwide

| Tham số | Region = "VN" | Region = "" (Worldwide) |
|---------|:-------------:|:-----------------------:|
| `RegionCode` gửi API | `"VN"` | `null` (không gửi) |
| Trending videos | Trending Việt Nam | Trending toàn cầu (US-biased) |
| Category trending | Music VN, News VN... | Music global, News global... |
| Search results | Ưu tiên nội dung VN | Không ưu tiên |
| Ngôn ngữ API | Không ảnh hưởng | Không ảnh hưởng |
| Ngôn ngữ UI | Theo `CurrentLanguage` | Theo `CurrentLanguage` |

---

## 10. Áp dụng cho YTMusicWP

### Code mẫu — Lấy Music Trending theo quốc gia

```csharp
// Sử dụng Google.Apis.YouTube.v3 NuGet package
async Task<List<Video>> GetTrendingMusic(string regionCode = "VN") {
    var youtubeService = new YouTubeService(new BaseClientService.Initializer {
        ApiKey = "YOUR_API_KEY",
        ApplicationName = "YTMusicWP"
    });
    
    var request = youtubeService.Videos.List("snippet,statistics,contentDetails");
    request.Chart = VideosResource.ListRequest.ChartEnum.MostPopular;
    request.VideoCategoryId = "10";  // Music ⭐
    request.MaxResults = 25;
    
    // Region code — ảnh hưởng kết quả trending
    if (!string.IsNullOrEmpty(regionCode)) {
        request.RegionCode = regionCode;
    }
    
    var response = await request.ExecuteAsync();
    return response.Items.ToList();
}
```

### Auto-detect region

```csharp
string DetectRegion() {
    string osRegion = RegionInfo.CurrentRegion
        .TwoLetterISORegionName
        .ToUpper(CultureInfo.InvariantCulture);
    
    // Kiểm tra YouTube hỗ trợ region này không
    // (YouTube hỗ trợ ~100 regions, copy danh sách từ MetroTube)
    if (SupportedRegions.ContainsKey(osRegion)) {
        return osRegion;
    }
    return "";  // Worldwide
}
```

### Các endpoint hữu ích cho app nghe nhạc

| Mục đích | API Call | Tham số Region |
|----------|---------|----------------|
| Nhạc trending VN | `Videos.List(chart=mostPopular, categoryId=10, regionCode=VN)` | ✅ |
| Tìm bài hát | `Search.List(q="...", type=video, regionCode=VN)` | ✅ |
| Video của kênh | `Search.List(channelId="...", type=video)` | ❌ Không cần |
| Playlist items | `PlaylistItems.List(playlistId="...")` | ❌ Không cần |
| Thông tin video | `Videos.List(id="...", part=snippet,statistics)` | ❌ Không cần |

> [!TIP]
> Cho app nghe nhạc, tập trung vào `categoryId=10` (Music) + `regionCode` để hiển thị nhạc trending theo quốc gia người dùng. Kết hợp với auto-detect region từ OS để trải nghiệm "out of the box" tốt nhất.
