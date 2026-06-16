# Báo cáo chi tiết: Cơ chế đăng nhập Google của MetroTube Patched

> **Ứng dụng**: MetroTube Patched (LazyTube) v1.3.195.0  
> **Ngày phân tích**: 2026-06-16  
> **Phương pháp**: Reverse-engineering IL Assembly (ildasm) từ Shared.dll + LazyTube.dll  
> **Mục đích**: Hiểu cách app đăng nhập Google Account và sử dụng token cho các tính năng YouTube

---

## Mục lục

1. [Tổng quan](#1-tổng-quan)
2. [Các class liên quan](#2-các-class-liên-quan)
3. [Credentials & OAuth Configuration](#3-credentials--oauth-configuration)
4. [Flow đăng nhập chi tiết](#4-flow-đăng-nhập-chi-tiết)
5. [Quản lý Token](#5-quản-lý-token)
6. [Lưu trữ bảo mật (AES Encryption)](#6-lưu-trữ-bảo-mật-aes-encryption)
7. [Đăng nhập để làm gì? (19 tính năng)](#7-đăng-nhập-để-làm-gì-19-tính-năng)
8. [So sánh chế độ có / không đăng nhập](#8-so-sánh-chế-độ-có--không-đăng-nhập)
9. [Đăng xuất](#9-đăng-xuất)
10. [Hướng dẫn áp dụng cho YTMusicWP](#10-hướng-dẫn-áp-dụng-cho-ytmusicwp)

---

## 1. Tổng quan

MetroTube sử dụng **Google OAuth 2.0 Authorization Code Flow** để đăng nhập tài khoản Google/YouTube. Đây là cách tiêu chuẩn mà các ứng dụng third-party dùng để truy cập YouTube API thay mặt người dùng.

### Tại sao cần đăng nhập?

- **Không đăng nhập**: App vẫn phát video, tìm kiếm, xem kênh bình thường (dùng API key công khai)
- **Có đăng nhập**: Mở khóa các tính năng **cá nhân** — subscriptions, like/dislike, comment, playlist, favourites, watch later — giống YouTube chính thức

### Flow tổng quát

```mermaid
sequenceDiagram
    participant U as 👤 Người dùng
    participant L as 📱 LoginPage<br/>(WebBrowser)
    participant G as 🔒 Google OAuth
    participant H as ⚙️ YouTubeOauthHelper<br/>(Shared.dll)
    participant A as 👥 AuthUserV3<br/>(LazyTube.dll)
    participant S as 💾 IsolatedStorage<br/>(Encrypted AES)

    U->>L: Nhấn "Sign In"
    L->>L: Kiểm tra clientID ≠ "by"
    L->>G: Navigate WebBrowser →<br/>accounts.google.com/o/oauth2/auth
    G->>U: Hiển thị trang đăng nhập Google
    U->>G: Nhập email + password + consent
    G->>L: Redirect → localhost/oauth2callback?code={AUTH_CODE}
    L->>L: Bắt redirect, parse "code=" từ URL
    L->>H: RequestAccessToken(authCode)
    H->>G: POST https://oauth2.googleapis.com/token
    G->>H: {access_token, refresh_token, expires_in}
    H->>A: LogInUser(tokenResponse)
    A->>A: Lấy thông tin user (username, userId, playlists...)
    A->>S: Lưu tokens + user info (mã hóa AES)
    A->>L: LoginComplete event
    L->>U: Quay lại trang trước
```

---

## 2. Các class liên quan

### Trong Shared.dll

| Class | Dòng IL | Vai trò |
|-------|---------|---------|
| [YouTubeOauthHelper](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L10353) | ~10353-10501 | Tạo URL login, exchange code → token, refresh token |

**Methods:**
- `CreateGoogleLoginUri()` → Tạo URL đăng nhập Google
- `RequestAccessToken(string authorizationCode)` → Đổi auth code thành access token
- `RefreshAccessToken(string refreshToken)` → Làm mới token khi hết hạn
- `get/set_clientID` — OAuth Client ID (có thể thay đổi runtime)
- `get/set_clientSecret` — OAuth Client Secret (có thể thay đổi runtime)

### Trong LazyTube.dll

| Class | Dòng IL | Vai trò |
|-------|---------|---------|
| [LoginPage](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L11595) | ~11595-11879 | UI đăng nhập — WebBrowser control nhúng |
| [AuthUserV3](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L68472) | ~62353-69799 | Quản lý user đã đăng nhập — 80+ methods |
| [Encryption](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L113258) | ~113180-113461 | Mã hóa/giải mã AES cho local storage |
| [SettingsPage](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L32105) | ~30870-32765 | Cho phép user thay đổi API keys |

---

## 3. Credentials & OAuth Configuration

### Hardcoded trong [YouTubeConstants](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L121699)

```csharp
// LazyTube.Utils.YouTubeConstants
public const string ClientId = "170729868717-epodfoimm3bte91rpg06fbf79t92l5hj.apps.googleusercontent.com";
public const string ClientSecret = "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";
public const string UserActionsApiKey = "AIzaSyCYmowDVBO6n0_m-r2uhPGtGt2acT16B04";
public const string LogoutUrl = "https://accounts.google.com/o/oauth2/revoke?token={0}";
```

### Default values trong [YouTubeOauthHelper](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L10449)

```csharp
// Static constructor (.cctor)
static YouTubeOauthHelper() {
    clientID = "by";           // Placeholder — buộc user nhập key riêng
    clientSecret = "NLogDEV";  // Placeholder
}
```

> [!IMPORTANT]
> **Giá trị mặc định của `clientID` là `"by"` (placeholder)**, không phải Client ID thật. Khi user mở LoginPage, app kiểm tra:
> ```csharp
> if (YouTubeOauthHelper.clientID == "by") {
>     MessageBox.Show("You need to enter the OAuth keys in settings -> keys to log in to the app.");
>     NavigationService.GoBack();
>     return;
> }
> ```
> User phải vào **Settings → Keys** nhập `ClientID` và `ClientSecret` riêng trước khi đăng nhập được. Hoặc app lấy giá trị từ `YouTubeConstants` khi khởi động (nếu IsolatedStorage chưa có giá trị custom).

### Configurable từ Settings

App cho phép user thay đổi credentials qua [SettingsPage](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L32105):

| Setting key | UI Label | Mục đích |
|-------------|----------|----------|
| `ConfigYTAPIkey` | "YouTube API Key" | API key cho Data API v3 |
| `ConfigClientSecret` | "Client Secret" | OAuth2 Client Secret |
| `PreferredInvidiousInstance` | "Invidious Instance" | Invidious server ưa thích |
| `ConfigYT2009Ins` | "YT2009 Instance" | YT2009 proxy server |

---

## 4. Flow đăng nhập chi tiết

### Bước 1: Mở trang login

**Trigger**: User nhấn nút "Sign In" → gọi `AuthUserV3.OpenLoginPage(page)`

```csharp
// AuthUserV3.OpenLoginPage()
page.NavigationService.Navigate(new Uri("/LoginPage.xaml", UriKind.Relative));
```

### Bước 2: LoginPage khởi tạo WebBrowser

**Method**: [LoginPage.OnNavigatedTo](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L11612)

```csharp
// Pseudo-code từ IL
void OnNavigatedTo() {
    // 1. Tạo WebBrowser control nhúng
    _browser = new WebBrowser();
    _browser.IsScriptEnabled = true;
    _browser.Navigating += _browser_Navigating;  // Bắt redirect
    _browser.Navigated += _browser_Navigated;     // Hiện browser khi load xong
    ContentPanel.Children.Add(_browser);
    
    // 2. Kiểm tra credentials
    if (YouTubeOauthHelper.clientID == "by") {
        MessageBox.Show("You need to enter the OAuth keys in settings -> keys to log in.");
        NavigationService.GoBack();
        return;
    }
    
    // 3. Navigate tới Google OAuth
    Uri loginUri = YouTubeOauthHelper.CreateGoogleLoginUri();
    _browser.Navigate(loginUri, null, "User-Agent: googlebot");
}
```

> [!NOTE]
> **User-Agent giả lập `"googlebot"`** — Điều này cần thiết vì WebBrowser control của WP8 dùng engine cũ (IE Mobile). Google sẽ chặn trang đăng nhập nếu nhận diện là "mobile browser cũ". Giả lập googlebot giúp bypass kiểm tra user-agent.

### Bước 3: Tạo URL OAuth

**Method**: [YouTubeOauthHelper.CreateGoogleLoginUri()](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L10353)

```csharp
// Pseudo-code từ IL
Uri CreateGoogleLoginUri() {
    string url = string.Format(
        "{0}?response_type=code&client_id={1}&redirect_uri={2}&scope={3}&prompt=consent&access_type=offline",
        "https://accounts.google.com/o/oauth2/auth",                    // Auth endpoint
        YouTubeOauthHelper.clientID,                                     // Client ID
        "http://localhost/oauth2callback",                               // Redirect URI
        YouTubeService.Scope.Youtube + "%20" + YouTubeService.Scope.YoutubeForceSsl  // Scopes
    );
    return new Uri(url, UriKind.Absolute);
}
```

**URL thực tế được tạo:**
```
https://accounts.google.com/o/oauth2/auth
  ?response_type=code
  &client_id=170729868717-epodfoimm3bte91rpg06fbf79t92l5hj.apps.googleusercontent.com
  &redirect_uri=http://localhost/oauth2callback
  &scope=https://www.googleapis.com/auth/youtube%20https://www.googleapis.com/auth/youtube.force-ssl
  &prompt=consent
  &access_type=offline
```

**Giải thích các tham số:**

| Tham số | Giá trị | Ý nghĩa |
|---------|---------|---------|
| `response_type` | `code` | Authorization Code Flow (không phải implicit) |
| `client_id` | `170729868717-...` | ID ứng dụng đăng ký trên Google Cloud |
| `redirect_uri` | `http://localhost/oauth2callback` | URL redirect sau khi user consent — WebBrowser bắt redirect này |
| `scope` | `youtube` + `youtube.force-ssl` | Quyền truy cập YouTube đầy đủ (read + write) |
| `prompt` | `consent` | Luôn hiện màn hình consent, kể cả khi đã consent trước đó |
| `access_type` | `offline` | Yêu cầu `refresh_token` để làm mới token khi hết hạn |

### Bước 4: User đăng nhập Google

Người dùng thấy trang đăng nhập Google tiêu chuẩn trong WebBrowser control:
1. Nhập email
2. Nhập password
3. Xác nhận consent (cho phép MetroTube truy cập YouTube)

### Bước 5: Bắt Authorization Code

**Method**: [LoginPage._browser_Navigating](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L11300) (async state machine)

Khi Google redirect về `http://localhost/oauth2callback?code=4/0Axxxxxxxxxx`:

```csharp
// Pseudo-code từ IL
async void _browser_Navigating(NavigatingEventArgs e) {
    // 1. Kiểm tra host = "localhost" (redirect callback)
    if (e.Uri.Host.Equals("localhost")) {
        _browser.Visibility = Collapsed;  // Ẩn browser
        e.Cancel = true;                  // Hủy navigation (không load localhost)
        
        // 2. Parse authorization code từ query string
        string query = e.Uri.Query;
        int codeIndex = query.IndexOf("code=");
        if (codeIndex > -1) {
            string authCode = query.Substring(codeIndex + 5);  // Lấy phần sau "code="
            
            try {
                // 3. Exchange code → token
                TokenResponse token = await YouTubeOauthHelper.RequestAccessToken(authCode);
                
                // 4. Đăng nhập user
                await AuthUserV3.LogInUser(token);
                
            } catch (WebException ex) {
                // 5. Xử lý lỗi
                var errorResponse = (HttpWebResponse)ex.Response;
                string errorBody = await new StreamReader(errorResponse.GetResponseStream()).ReadToEndAsync();
                MessageBox.Show(
                    "An error occurred while logging in, make sure your client secret is correct: " 
                    + errorBody
                    + "\n\nClient information:\nClientSecret:" 
                    + YouTubeOauthHelper.clientSecret
                );
            }
        }
        
        // 6. Quay lại trang trước
        NavigationService.GoBack();
    }
}
```

### Bước 6: Exchange Code → Token

**Method**: [YouTubeOauthHelper.RequestAccessToken()](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L10391)

```
POST https://oauth2.googleapis.com/token
Content-Type: application/x-www-form-urlencoded

code={auth_code}
&client_id={clientID}
&client_secret={clientSecret}
&redirect_uri=http://localhost/oauth2callback
&grant_type=authorization_code
```

**Response:**
```json
{
  "access_token": "ya29.a0AW...",
  "expires_in": 3600,
  "refresh_token": "1//0e...",
  "scope": "https://www.googleapis.com/auth/youtube ...",
  "token_type": "Bearer"
}
```

Response được parse thành `Google.Apis.Auth.OAuth2.Responses.TokenResponse` object.

### Bước 7: Đăng nhập user

**Method**: [AuthUserV3.LogInUser()](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L68785) (async)

Sau khi có `TokenResponse`:
1. Tạo `YouTubeService` (authenticated) dùng `access_token`
2. Gọi YouTube Data API v3 để lấy thông tin user channel
3. Lưu tất cả vào `IsolatedStorage`
4. Fire event `LoginComplete`

---

## 5. Quản lý Token

### Token Response object

```csharp
// Google.Apis.Auth.OAuth2.Responses.TokenResponse
class TokenResponse {
    string AccessToken;     // Token truy cập API (hết hạn sau 1 giờ)
    long ExpiresInSeconds;  // 3600 (seconds)
    string RefreshToken;    // Token làm mới (không hết hạn trừ khi user revoke)
    string Scope;           // Quyền được cấp
    string TokenType;       // "Bearer"
}
```

### Refresh Token tự động

**Method**: [YouTubeOauthHelper.RefreshAccessToken()](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/Shared_decompiled_utf8.il#L10420)

Khi `access_token` hết hạn (sau ~1 giờ), app tự động gọi:

```
POST https://oauth2.googleapis.com/token
Content-Type: application/x-www-form-urlencoded

refresh_token={refresh_token}
&client_id={clientID}
&client_secret={clientSecret}
&grant_type=refresh_token
```

→ Nhận `access_token` mới mà không cần user đăng nhập lại.

### Auth Header cho API calls

**Method**: [AuthUserV3.FillRequestHeaderWithAuth()](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L69534) (async)

```csharp
// Pseudo-code
async Task FillRequestHeaderWithAuth(WebRequest request) {
    // 1. Kiểm tra token còn hạn không
    // 2. Nếu hết hạn → RefreshAccessToken()
    // 3. Thêm header
    request.Headers["Authorization"] = "Bearer " + tokenResponse.AccessToken;
}
```

### Token truyền sang Background Agent

Khi phát nhạc nền, `access_token` và `refresh_token` được truyền qua `IsolatedStorage` dùng [AudioAgentContainer](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/AudioPlayback_decompiled_utf8.il#L125):

```csharp
class AudioAgentContainer {
    TokenResponse Token;      // Access token cho InnerTube API
    string RefreshToken;      // Refresh token
    // ...các fields khác
}
```

→ `AudioPlaybackAgent` dùng token này để gọi InnerTube API (`SendVideoRequestAuthYTi`) khi cần lấy stream URL cho bài tiếp theo trong playlist.

---

## 6. Lưu trữ bảo mật (AES Encryption)

### Class [Encryption](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L113258)

Tokens được mã hóa trước khi lưu vào `IsolatedStorage`:

```csharp
// Pseudo-code từ IL
static string Encrypt(string dataToEncrypt, string password, string salt) {
    Rfc2898DeriveBytes keyDerive = new(password, Encoding.UTF8.GetBytes(salt));
    AesManaged aes = new();
    aes.Key = keyDerive.GetBytes(aes.KeySize / 8);    // 256-bit key
    aes.IV = keyDerive.GetBytes(aes.BlockSize / 8);    // 128-bit IV
    
    using MemoryStream ms = new();
    using CryptoStream cs = new(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
    byte[] data = Encoding.UTF8.GetBytes(dataToEncrypt);
    cs.Write(data, 0, data.Length);
    cs.FlushFinalBlock();
    return Convert.ToBase64String(ms.ToArray());
}

static string Decrypt(string dataToDecrypt, string password, string salt) {
    // Reverse process: Base64 → AES Decrypt → UTF8 string
}
```

**Thuật toán**: AES-256 (AesManaged) + PBKDF2 key derivation (Rfc2898DeriveBytes)

### Dữ liệu lưu trong IsolatedStorage

Từ [AuthUserV3.ClearLoginDetails()](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L68892), ta biết các key được sử dụng:

| IsolatedStorage Key | Dữ liệu | Encrypted? |
|---------------------|----------|------------|
| `LogInUsername` | Tên kênh YouTube | Có thể |
| `LogInUserID` | YouTube Channel ID | Có thể |
| `LogInTimestamp` | Thời gian đăng nhập | Không |
| `LogInUserAge` | Tuổi user (age-restrict) | Không |
| `LogInWatchLater` | Watch Later Playlist ID | Không |
| `LogInUploadsKey` | Uploads Playlist ID | Không |
| `LogInFavouritesKey` | Favourites Playlist ID | Không |
| `LogInExpiryKey` | Token expiry time | Không |
| `LogInTokenResponseKey` | **TokenResponse (JSON serialized)** | **Có** |
| `LogInRefreshTokenKey` | **Refresh Token** | **Có** |

---

## 7. Đăng nhập để làm gì? (19 tính năng)

Tất cả các tính năng sau đều nằm trong class [AuthUserV3](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L68472) và yêu cầu OAuth token:

### Quản lý Subscriptions

| # | Tính năng | Method | Mô tả |
|---|-----------|--------|-------|
| 1 | Xem danh sách subscriptions | `RetrieveSubscriptions()` | Lấy tất cả kênh đã subscribe |
| 2 | Kiểm tra đã subscribe chưa | `IsSubscribedToUser()` | Kiểm tra 1 kênh cụ thể |
| 3 | Subscribe kênh | `SubscribeToUser()` | Theo dõi kênh mới |
| 4 | Unsubscribe kênh | `Unsubscribe()` | Hủy theo dõi |
| 5 | Đọc thông tin subscription | `ReadSubscriptionEntry()` | Chi tiết 1 subscription |

### Tương tác Video

| # | Tính năng | Method | Mô tả |
|---|-----------|--------|-------|
| 6 | Like / Dislike video | `AddRating()` | Rate 1 video |
| 7 | Bình luận video | `AddComment()` | Thêm comment |
| 8 | Yêu thích video | `FavouriteVideo()` | Thêm vào Favourites |
| 9 | Kiểm tra đã yêu thích chưa | `IsFavourite()` | Check favourite status |
| 10 | Xóa yêu thích | `RemoveFavourite()` | Bỏ khỏi Favourites |

### Quản lý Playlists

| # | Tính năng | Method | Mô tả |
|---|-----------|--------|-------|
| 11 | Tạo playlist mới | `CreatePlaylist()` | Tạo playlist YouTube |
| 12 | Thêm video vào playlist | `AddToPlaylist()` | Add video to playlist |
| 13 | Xóa video khỏi playlist | `RemovePlaylistEntry()` | Remove from playlist |
| 14 | Xóa playlist | `DeletePlaylist()` | Delete entire playlist |

### Watch Later

| # | Tính năng | Method | Mô tả |
|---|-----------|--------|-------|
| 15 | Thêm vào Watch Later | `AddToWatchLater()` | Add to WL |
| 16 | Xóa khỏi Watch Later | `RemoveWatchLaterEntry()` | Remove from WL |

### Hệ thống

| # | Tính năng | Method | Mô tả |
|---|-----------|--------|-------|
| 17 | Tạo YouTubeService authenticated | `GetYouTubeService()` | Service với bearer token |
| 18 | Thêm auth header cho request | `FillRequestHeaderWithAuth()` | Authorization: Bearer |
| 19 | Lấy TokenResponse (cho background agent) | `GetTokenResponse()` | Truyền token sang AudioPlaybackAgent |

---

## 8. So sánh chế độ có / không đăng nhập

| Tính năng | Không đăng nhập | Có đăng nhập |
|-----------|:---------------:|:------------:|
| Tìm kiếm video | ✅ (API key công khai) | ✅ |
| Xem video / Phát nhạc | ✅ (Invidious/InnerTube) | ✅ |
| Xem comments | ✅ | ✅ |
| Xem kênh / channel info | ✅ | ✅ |
| Xem trending / categories | ✅ | ✅ |
| Phát nhạc nền | ✅ | ✅ |
| Download video offline | ✅ | ✅ |
| **Xem subscriptions** | ❌ | ✅ |
| **Subscribe / Unsubscribe** | ❌ | ✅ |
| **Like / Dislike** | ❌ | ✅ |
| **Bình luận** | ❌ | ✅ |
| **Playlist cá nhân** | ❌ | ✅ |
| **Favourites** | ❌ | ✅ |
| **Watch Later** | ❌ | ✅ |
| **Xem uploads** | ❌ | ✅ |
| **Xem watch history** | ⚠️ Local only | ✅ Sync |

---

## 9. Đăng xuất

**Method**: [AuthUserV3.Logout()](file:///d:/Downloads/941f6817fc96fa7ae33fe31031c816dc/LazyTube_decompiled_utf8.il#L68814)

```csharp
// Pseudo-code từ IL
void Logout() {
    LoggedInState = false;
    Username = null;
    _userAge = 0;
    _userID = null;
    _initialised = false;
    _subscriptions.Clear();
    _expiryTime = DateTime.MinValue;
    _tokenResponse = null;
    _refreshToken = null;
    
    ClearLoginDetails();   // Xóa toàn bộ keys trong IsolatedStorage
    _subscriptionsLoaded = false;
    TileImageHelper.ResetSubscriptionsTile();
    
    LogoutComplete?.Invoke(this);
}
```

**Revoke token trên Google**: App cũng hỗ trợ revoke token qua URL:
```
https://accounts.google.com/o/oauth2/revoke?token={access_token}
```
(Định nghĩa trong `YouTubeConstants.LogoutUrl`)

---

## 10. Hướng dẫn áp dụng cho YTMusicWP

### Mức 1: Không cần đăng nhập (MVP)

Nếu app nghe nhạc chỉ cần phát nhạc, **không cần implement OAuth**:
- Tìm kiếm: Dùng YouTube Data API v3 + API key công khai
- Phát nhạc: Dùng Invidious API / InnerTube (không cần auth)
- History: Lưu local trong IsolatedStorage

### Mức 2: Đăng nhập cơ bản

Nếu muốn subscriptions, playlists, likes:

```csharp
// 1. Đăng ký OAuth credentials tại Google Cloud Console
// https://console.cloud.google.com/apis/credentials

// 2. Tạo LoginPage với WebBrowser control
public partial class LoginPage : PhoneApplicationPage {
    WebBrowser _browser;
    
    void OnNavigatedTo() {
        _browser = new WebBrowser { IsScriptEnabled = true };
        _browser.Navigating += OnNavigating;
        ContentPanel.Children.Add(_browser);
        
        string loginUrl = string.Format(
            "https://accounts.google.com/o/oauth2/auth" +
            "?response_type=code" +
            "&client_id={0}" +
            "&redirect_uri=http://localhost/oauth2callback" +
            "&scope=https://www.googleapis.com/auth/youtube" +
            "&access_type=offline&prompt=consent",
            YOUR_CLIENT_ID);
        
        _browser.Navigate(new Uri(loginUrl), null, "User-Agent: googlebot");
    }
    
    async void OnNavigating(object s, NavigatingEventArgs e) {
        if (e.Uri.Host == "localhost") {
            e.Cancel = true;
            string code = e.Uri.Query.Split(new[]{"code="}, StringSplitOptions.None)[1];
            
            // Exchange code → token
            TokenResponse token = await ExchangeCodeForToken(code);
            
            // Lưu token (mã hóa!)
            SaveToken(token);
            NavigationService.GoBack();
        }
    }
}
```

```csharp
// 3. Exchange code → token
async Task<TokenResponse> ExchangeCodeForToken(string code) {
    var request = WebRequest.Create("https://oauth2.googleapis.com/token");
    request.Method = "POST";
    request.ContentType = "application/x-www-form-urlencoded";
    
    string body = string.Format(
        "code={0}&client_id={1}&client_secret={2}" +
        "&redirect_uri=http://localhost/oauth2callback" +
        "&grant_type=authorization_code",
        code, YOUR_CLIENT_ID, YOUR_CLIENT_SECRET);
    
    byte[] data = Encoding.UTF8.GetBytes(body);
    using (var stream = await request.GetRequestStreamAsync())
        stream.Write(data, 0, data.Length);
    
    var response = await request.GetResponseAsync();
    using (var reader = new StreamReader(response.GetResponseStream()))
        return JsonConvert.DeserializeObject<TokenResponse>(await reader.ReadToEndAsync());
}
```

```csharp
// 4. Refresh token khi hết hạn
async Task<TokenResponse> RefreshToken(string refreshToken) {
    // POST https://oauth2.googleapis.com/token
    // refresh_token={refreshToken}&client_id=...&client_secret=...&grant_type=refresh_token
}
```

> [!WARNING]
> **Quan trọng**: 
> - Tạo credentials **riêng** cho app của bạn — KHÔNG dùng credentials của MetroTube
> - Lưu token phải **mã hóa** (AES hoặc DPAPI) — không lưu plaintext
> - `User-Agent: googlebot` có thể bị Google chặn trong tương lai — cần cách khác
> - OAuth scope `youtube` + `youtube.force-ssl` cho phép đọc + ghi — chỉ yêu cầu scope cần thiết

---

## Tóm tắt

```
┌─────────────────────────────────────────────────────────────┐
│                   METROTUBE OAuth FLOW                       │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  1. LoginPage tạo WebBrowser control                        │
│  2. Navigate → accounts.google.com/o/oauth2/auth            │
│     • client_id = 170729868717-...                          │
│     • redirect_uri = http://localhost/oauth2callback         │
│     • scope = youtube + youtube.force-ssl                    │
│     • access_type = offline (lấy refresh_token)             │
│     • User-Agent: googlebot (bypass browser check)          │
│  3. User đăng nhập + consent                                │
│  4. Google redirect → localhost/oauth2callback?code=xxx      │
│  5. App bắt redirect, parse code=                           │
│  6. POST code → oauth2.googleapis.com/token                 │
│  7. Nhận access_token + refresh_token                       │
│  8. AuthUserV3.LogInUser() lưu tokens (AES encrypted)       │
│  9. Dùng token cho 19 tính năng cá nhân                     │
│ 10. Auto refresh khi token hết hạn (1 giờ)                  │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```
