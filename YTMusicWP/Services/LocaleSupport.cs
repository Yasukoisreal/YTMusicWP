using System;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Which language the app's own interface runs in. The translation itself lives in
    /// Strings/&lt;language&gt;/Resources.resw (XAML via x:Uid, code via Loc); this is the part that decides which of
    /// the supported languages a device (or a manually picked) language tag maps to. Pure logic, no WinRT, so the
    /// unit tests can run it.
    /// </summary>
    /// <remarks>
    /// Internal, not public: the background audio task links this file, and a public type in a namespace outside
    /// AudioPlayerTask is rejected by the Windows Metadata exporter (WME1023/WME1024/WME1044).
    /// </remarks>
    internal static class LocaleSupport
    {
        public const string English = "en";
        public const string Italian = "it";
        public const string Vietnamese = "vi";

        /// <summary>The languages the app is translated into. Every other language shows English.</summary>
        public static readonly string[] UiLanguages = { English, Italian, Vietnamese };

        /// <summary>
        /// The part of a BCP-47 tag that decides the language: "it", "it-IT" and "IT_it" all give "it". Returns "" for
        /// null/empty and for tags that start with a separator.
        /// </summary>
        public static string PrimarySubtag(string languageTag)
        {
            if (string.IsNullOrEmpty(languageTag)) return "";
            string tag = languageTag.Trim();
            int cut = tag.IndexOfAny(new[] { '-', '_' });
            if (cut >= 0) tag = tag.Substring(0, cut);
            return tag.ToLowerInvariant();
        }

        /// <summary>Is the app itself translated into this language? ("it-IT" -> true, "fr-FR" -> false).</summary>
        public static bool IsUiLanguage(string languageTag)
        {
            string primary = PrimarySubtag(languageTag);
            for (int i = 0; i < UiLanguages.Length; i++)
            {
                if (UiLanguages[i] == primary) return true;
            }
            return false;
        }

        /// <summary>
        /// Device or user language -> one of <see cref="UiLanguages"/>. Anything unsupported (French, Chinese, an
        /// empty tag) becomes English, which is also the language declared as the package default.
        /// </summary>
        public static string ResolveUiLanguage(string languageTag)
        {
            string primary = PrimarySubtag(languageTag);
            switch (primary)
            {
                case Italian: return Italian;
                case Vietnamese: return Vietnamese;
                default: return English;
            }
        }

        /// <summary>
        /// The full tag for <see cref="Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride"/> and for
        /// the saved setting, so the picker in Settings can round-trip it.
        /// </summary>
        public static string ResourceTag(string uiLanguage)
        {
            switch (ResolveUiLanguage(uiLanguage))
            {
                case Italian: return "it-IT";
                case Vietnamese: return "vi-VN";
                default: return "en-US";
            }
        }

        /// <summary>
        /// Content language for the "Auto" language setting: the device language when the app supports it, otherwise
        /// whatever the trending region implies (the previous behaviour, kept for every other language).
        /// </summary>
        public static string AutoContentLanguage(string deviceLanguageTag, string regionLanguage)
        {
            if (IsUiLanguage(deviceLanguageTag)) return ResolveUiLanguage(deviceLanguageTag);
            return string.IsNullOrEmpty(regionLanguage) ? English : regionLanguage;
        }
    }
}
