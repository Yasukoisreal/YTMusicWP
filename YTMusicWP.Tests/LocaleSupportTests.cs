using Microsoft.VisualStudio.TestTools.UnitTesting;
using YTMusicWP.Services;

namespace YTMusicWP.Tests
{
    [TestClass]
    public class LocaleSupportTests
    {
        [TestMethod]
        public void PrimarySubtag_KeepsOnlyTheLanguage()
        {
            Assert.AreEqual("it", LocaleSupport.PrimarySubtag("it-IT"));
            Assert.AreEqual("it", LocaleSupport.PrimarySubtag("it"));
            Assert.AreEqual("it", LocaleSupport.PrimarySubtag("IT-it"), "the device can report the tag in any case");
            Assert.AreEqual("it", LocaleSupport.PrimarySubtag("it_it"));
            Assert.AreEqual("zh", LocaleSupport.PrimarySubtag("zh-Hans-CN"));
            Assert.AreEqual("", LocaleSupport.PrimarySubtag(""));
            Assert.AreEqual("", LocaleSupport.PrimarySubtag(null));
            Assert.AreEqual("", LocaleSupport.PrimarySubtag("-IT"), "a tag starting with the separator has no language");
        }

        [TestMethod]
        public void ResolveUiLanguage_MapsTheThreeTranslatedLanguages()
        {
            Assert.AreEqual("it", LocaleSupport.ResolveUiLanguage("it-IT"));
            Assert.AreEqual("it", LocaleSupport.ResolveUiLanguage("it"));
            Assert.AreEqual("vi", LocaleSupport.ResolveUiLanguage("vi-VN"));
            Assert.AreEqual("en", LocaleSupport.ResolveUiLanguage("en-US"));
            Assert.AreEqual("en", LocaleSupport.ResolveUiLanguage("en-GB"));
            Assert.AreEqual("en", LocaleSupport.ResolveUiLanguage("en"));
        }

        [TestMethod]
        public void ResolveUiLanguage_FallsBackToEnglish()
        {
            Assert.AreEqual("en", LocaleSupport.ResolveUiLanguage("fr-FR"), "the app is not translated into French");
            Assert.AreEqual("en", LocaleSupport.ResolveUiLanguage("de-DE"));
            Assert.AreEqual("en", LocaleSupport.ResolveUiLanguage("zh-CN"));
            Assert.AreEqual("en", LocaleSupport.ResolveUiLanguage(""));
            Assert.AreEqual("en", LocaleSupport.ResolveUiLanguage(null));
        }

        [TestMethod]
        public void IsUiLanguage_OnlyForTheTranslatedLanguages()
        {
            Assert.IsTrue(LocaleSupport.IsUiLanguage("it-IT"));
            Assert.IsTrue(LocaleSupport.IsUiLanguage("vi"));
            Assert.IsTrue(LocaleSupport.IsUiLanguage("en-US"));
            Assert.IsFalse(LocaleSupport.IsUiLanguage("fr-FR"));
            Assert.IsFalse(LocaleSupport.IsUiLanguage(""));
            Assert.IsFalse(LocaleSupport.IsUiLanguage(null));
        }

        [TestMethod]
        public void ResourceTag_MatchesTheStringsFolderNames()
        {
            // The tags have to agree with Strings\<tag>\Resources.resw and with the package manifest
            Assert.AreEqual("it-IT", LocaleSupport.ResourceTag("it"));
            Assert.AreEqual("vi-VN", LocaleSupport.ResourceTag("vi"));
            Assert.AreEqual("en-US", LocaleSupport.ResourceTag("en"));
            Assert.AreEqual("en-US", LocaleSupport.ResourceTag("fr"), "unsupported picks fall back to the default language");
            Assert.AreEqual("en-US", LocaleSupport.ResourceTag(null));
        }

        [TestMethod]
        public void AutoContentLanguage_PrefersTheDeviceLanguageWhenTranslated()
        {
            // An Italian phone gets Italian music labels even when the trending region says otherwise
            Assert.AreEqual("it", LocaleSupport.AutoContentLanguage("it-IT", "en"));
            Assert.AreEqual("vi", LocaleSupport.AutoContentLanguage("vi-VN", "en"));
            Assert.AreEqual("en", LocaleSupport.AutoContentLanguage("en-US", "vi"));
        }

        [TestMethod]
        public void AutoContentLanguage_KeepsTheRegionForOtherLanguages()
        {
            Assert.AreEqual("vi", LocaleSupport.AutoContentLanguage("fr-FR", "vi"), "a French phone in Vietnam keeps the region language");
            Assert.AreEqual("ja", LocaleSupport.AutoContentLanguage("de-DE", "ja"));
            Assert.AreEqual("en", LocaleSupport.AutoContentLanguage("", ""), "no device language and no region: English");
            Assert.AreEqual("en", LocaleSupport.AutoContentLanguage(null, null));
        }
    }
}
