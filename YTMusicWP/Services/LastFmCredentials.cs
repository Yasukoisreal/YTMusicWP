namespace YTMusicWP.Services
{
    /// <summary>
    /// Last.fm API credentials (https://www.last.fm/api/account/create). They are kept out of the repository: create
    /// Services\LastFmCredentials.Local.cs (ignored by git, compiled only when it exists) containing
    /// <code>
    /// namespace YTMusicWP.Services
    /// {
    ///     internal static partial class LastFmCredentials
    ///     {
    ///         static partial void Load(ref string apiKey, ref string secret)
    ///         {
    ///             apiKey = "your API key";
    ///             secret = "your shared secret";
    ///         }
    ///     }
    /// }
    /// </code>
    /// A build without it hides the Last.fm settings.
    /// </summary>
    internal static partial class LastFmCredentials
    {
        public static readonly string ApiKey;
        public static readonly string Secret;

        static LastFmCredentials()
        {
            string apiKey = null, secret = null;
            Load(ref apiKey, ref secret);
            ApiKey = apiKey;
            Secret = secret;
        }

        public static bool IsAvailable
        {
            get { return !string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(Secret); }
        }

        static partial void Load(ref string apiKey, ref string secret);
    }
}
