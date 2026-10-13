using System;
using System.Globalization;
using Windows.ApplicationModel.Resources;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Strings shown from code (toasts, status labels, dialogs). Text declared in XAML is translated by its x:Uid
    /// instead; both read the same Strings/&lt;language&gt;/Resources.resw files, so a translation is written once.
    /// Every lookup carries its English text as a fallback: a key that is missing in the current language (or a call
    /// from a thread without a CoreWindow, like the background audio task) returns English instead of throwing or
    /// showing an empty label.
    /// </summary>
    internal static class Loc
    {
        private static ResourceLoader _loader;

        /// <summary>The saved value that means "follow the phone's language".</summary>
        public const string AutoTag = "AUTO";

        private const string SettingsKey = "AppUiLanguage";

        /// <summary>The language the app is running in: "en", "it" or "vi".</summary>
        public static string Language
        {
            get
            {
                try
                {
                    string tag = Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride;
                    if (string.IsNullOrEmpty(tag) && Windows.Globalization.ApplicationLanguages.Languages.Count > 0)
                        tag = Windows.Globalization.ApplicationLanguages.Languages[0];
                    return LocaleSupport.ResolveUiLanguage(tag);
                }
                catch { return LocaleSupport.English; }
            }
        }

        /// <summary>The translated string for <paramref name="key"/>, or <paramref name="english"/> when there is none.</summary>
        public static string T(string key, string english)
        {
            if (string.IsNullOrEmpty(key)) return english ?? "";
            var loader = Loader();
            if (loader == null) return english ?? "";
            try
            {
                string value = loader.GetString(key);
                return string.IsNullOrEmpty(value) ? (english ?? "") : value;
            }
            catch { return english ?? ""; }
        }

        /// <summary>
        /// A translated format string filled in, e.g. F("Lib_CleanedCache", "Cleaned {0} cache items", count).
        /// </summary>
        public static string F(string key, string englishFormat, params object[] args)
        {
            string format = T(key, englishFormat);
            try
            {
                return string.Format(CultureInfo.CurrentCulture, format, args);
            }
            catch (FormatException)
            {
                // A translator broke a placeholder: show the English sentence rather than crashing a toast
                return string.Format(CultureInfo.CurrentCulture, englishFormat, args);
            }
        }

        /// <summary>Drops the cached ResourceLoader, so strings picked after this follow the new language.</summary>
        public static void Reset()
        {
            _loader = null;
        }

        /// <summary>The language picked in Settings: a resource tag ("it-IT") or <see cref="AutoTag"/>.</summary>
        public static string SavedLanguage()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                if (settings.ContainsKey(SettingsKey))
                {
                    object value = settings[SettingsKey];
                    if (value != null && !string.IsNullOrEmpty(value.ToString())) return value.ToString();
                }
            }
            catch { }
            return AutoTag;
        }

        /// <summary>Saves the pick and applies it to the running process.</summary>
        public static void SaveLanguage(string tag)
        {
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values[SettingsKey] =
                    string.IsNullOrEmpty(tag) ? AutoTag : tag;
            }
            catch { }
            ApplyLanguage(tag);
        }

        /// <summary>
        /// Points the platform at the picked language. On WP8.1 an app cannot re-read its own resources while it is
        /// running, so the new language shows up the next time the app starts; calling this from the App constructor
        /// makes that restart path work even if the override were ever dropped.
        /// </summary>
        public static void ApplyLanguage(string tag)
        {
            try
            {
                bool auto = string.IsNullOrEmpty(tag) || tag == AutoTag;
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride =
                    auto ? "" : LocaleSupport.ResourceTag(tag);
            }
            catch { }
            Reset();
        }

        /// <summary>Applies whatever was saved in Settings. Call it before any XAML is loaded.</summary>
        public static void ApplySavedLanguage()
        {
            ApplyLanguage(SavedLanguage());
        }

        private static ResourceLoader Loader()
        {
            if (_loader != null) return _loader;
            // GetForCurrentView throws on a thread without a CoreWindow (background audio task, worker thread)
            if (Windows.UI.Core.CoreWindow.GetForCurrentThread() == null) return null;
            try
            {
                _loader = ResourceLoader.GetForCurrentView();
            }
            catch { return null; }
            return _loader;
        }
    }
}
