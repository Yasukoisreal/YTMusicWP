using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace YTMusicWP.Tests
{
    /// <summary>
    /// The app's own text lives in Strings/&lt;language&gt;/Resources.resw. A string is reached either by an x:Uid on
    /// a XAML element or by a key given to Loc.T/Loc.F in code, and nothing in the build notices when one of those is
    /// missing from a language: the label just stays English. These tests compare the translated files with each other
    /// and with every key the app actually asks for, so a forgotten translation fails the build instead of shipping.
    /// </summary>
    [TestClass]
    public class LocalizationResourcesTests
    {
        /// <summary>Guards the scans below: if a pattern stops matching, these tests must fail rather than pass.</summary>
        private const int MinimumUids = 150;
        private const int MinimumCodeKeys = 100;

        private static readonly Regex TagPattern = new Regex("<([A-Za-z:]+)((?:[^\">]|\"[^\"]*\")*)>", RegexOptions.Compiled);
        private static readonly Regex UidPattern = new Regex("x:Uid=\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex CodeKeyPattern = new Regex("Loc\\.[TF]\\(\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex ArrayKeyUsePattern = new Regex("Loc\\.[TF]\\(\\s*([A-Za-z_][A-Za-z0-9_]*)\\s*\\[", RegexOptions.Compiled);
        private static readonly Regex ArrayLiteralPattern = new Regex("\"([^\"]+)\"", RegexOptions.Compiled);

        [TestMethod]
        public void AllThreeLanguagesCarryTheSameKeys()
        {
            var english = Keys(ResourcePath("en-US"));
            var italian = Keys(ResourcePath("it-IT"));
            var vietnamese = Keys(ResourcePath("vi-VN"));

            AssertSameKeys(english, italian, "English", "Italian");
            AssertSameKeys(english, vietnamese, "English", "Vietnamese");
            AssertSameKeys(italian, vietnamese, "Italian", "Vietnamese");
            Assert.IsTrue(english.Count >= MinimumUids, "the resource scan found only " + english.Count + " keys");
        }

        private static void AssertSameKeys(HashSet<string> expected, HashSet<string> actual, string expectedName, string actualName)
        {
            var missing = expected.Except(actual).OrderBy(k => k).ToList();
            var extra = actual.Except(expected).OrderBy(k => k).ToList();

            Assert.AreEqual(0, missing.Count,
                "missing from " + actualName + ": " + string.Join(", ", missing));
            Assert.AreEqual(0, extra.Count,
                "only in " + actualName + ": " + string.Join(", ", extra));
        }

        [TestMethod]
        public void EveryXamlUidIsTranslated()
        {
            var expected = XamlKeys();
            Assert.IsTrue(expected.Count >= MinimumUids,
                "the XAML scan found only " + expected.Count + " x:Uid values: has the markup changed shape?");

            var italian = Keys(ResourcePath("it-IT"));
            var vietnamese = Keys(ResourcePath("vi-VN"));

            var untranslated = expected.Where(k => !italian.Contains(k.Key) || !vietnamese.Contains(k.Key))
                .Select(k => k.Key + "  <- " + k.Value)
                .OrderBy(k => k)
                .ToList();

            Assert.AreEqual(0, untranslated.Count, "x:Uid without a matching resource:" + Environment.NewLine + string.Join(Environment.NewLine, untranslated));
        }

        [TestMethod]
        public void EveryCodeKeyIsTranslated()
        {
            var expected = CodeKeys();
            Assert.IsTrue(expected.Count >= MinimumCodeKeys,
                "the code scan found only " + expected.Count + " Loc keys: has Loc.T/Loc.F been renamed?");

            var italian = Keys(ResourcePath("it-IT"));
            var vietnamese = Keys(ResourcePath("vi-VN"));

            var untranslated = expected.Where(k => !italian.Contains(k.Key) || !vietnamese.Contains(k.Key))
                .Select(k => k.Key + "  <- " + k.Value)
                .OrderBy(k => k)
                .ToList();

            Assert.AreEqual(0, untranslated.Count, "Loc key without a matching resource:" + Environment.NewLine + string.Join(Environment.NewLine, untranslated));
        }

        [TestMethod]
        public void EveryCodeKeyHasANeutralEnglishValue()
        {
            // MakePRI warns (0xdef01051) about a resource that exists only in a translated language: the neutral value
            // every phone in another language falls back to is missing. Loc.T's English literal also hides it at
            // runtime, so without this test the warning is the only symptom.
            var english = Keys(ResourcePath("en-US"));
            var missing = CodeKeys().Where(k => !english.Contains(k.Key))
                .Select(k => k.Key + "  <- " + k.Value)
                .OrderBy(k => k)
                .ToList();

            Assert.AreEqual(0, missing.Count,
                "code string without an English value in Strings/en-US/Resources.resw:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
        }

        [TestMethod]
        public void EveryTranslatedKeyHasANeutralEnglishValue()
        {
            // en-US is what a phone in a language the app is not translated into (and the neutral resource MakePRI
            // builds the package around) reads: a key that exists only in it-IT/vi-VN makes the build warn
            // (0xdef01051 "no default or neutral resource") and can fail at run time on such a phone.
            var english = Keys(ResourcePath("en-US"));
            var italian = Keys(ResourcePath("it-IT"));
            var vietnamese = Keys(ResourcePath("vi-VN"));

            Assert.IsTrue(english.Count > 0, "Strings/en-US/Resources.resw is empty: the package would lose its default language");

            var withoutNeutral = italian.Union(vietnamese).Where(k => !english.Contains(k)).OrderBy(k => k).ToList();
            Assert.AreEqual(0, withoutNeutral.Count,
                "translated but with no English value in Strings/en-US/Resources.resw: " + string.Join(", ", withoutNeutral));
        }

        [TestMethod]
        public void EveryResourceHasAValue()
        {
            foreach (string language in new[] { "en-US", "it-IT", "vi-VN" })
            {
                XDocument document = XDocument.Load(ResourcePath(language));
                foreach (XElement data in document.Root.Elements("data"))
                {
                    string key = (string)data.Attribute("name");
                    XElement value = data.Element("value");
                    Assert.IsNotNull(value, key + " has no <value> in " + language);
                    Assert.IsTrue(!string.IsNullOrEmpty(value.Value), key + " is empty in " + language);
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        private static string ResourcePath(string language)
        {
            return Path.Combine(ProjectRoot(), "YTMusicWP", "Strings", language, "Resources.resw");
        }

        private static string ProjectRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "YTMusicWP", "Strings", "it-IT", "Resources.resw")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            Assert.Fail("Could not find the project root above " + AppDomain.CurrentDomain.BaseDirectory);
            return null;
        }

        private static HashSet<string> Keys(string path)
        {
            Assert.IsTrue(File.Exists(path), "missing resource file: " + path);
            XDocument document = XDocument.Load(path);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (XElement data in document.Root.Elements("data"))
            {
                string name = (string)data.Attribute("name");
                Assert.IsNotNull(name, "a <data> entry without a name in " + path);
                Assert.IsTrue(keys.Add(name), "duplicate key " + name + " in " + path);
            }
            return keys;
        }

        /// <summary>The resource key every x:Uid needs, mapped to where it is used (for the failure message).</summary>
        private static Dictionary<string, string> XamlKeys()
        {
            var expected = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in ProjectFiles("YTMusicWP", "*.xaml"))
            {
                foreach (Match tag in TagPattern.Matches(File.ReadAllText(file)))
                {
                    Match uid = UidPattern.Match(tag.Groups[2].Value);
                    if (!uid.Success) continue;

                    string element = tag.Groups[1].Value;
                    string property = tag.Groups[2].Value.Contains("ToolTipService.ToolTip=")
                        ? "[using:Windows.UI.Xaml.Controls]ToolTipService.ToolTip"
                        : XamlProperty(element);
                    expected[uid.Groups[1].Value + "." + property] = Path.GetFileName(file) + "  (x:Uid=" + uid.Groups[1].Value + ")";
                }
            }
            return expected;
        }

        private static string XamlProperty(string element)
        {
            switch (element)
            {
                case "TextBlock": return "Text";
                case "TextBox": return "PlaceholderText";
                case "Button": return "Content";
                case "ComboBoxItem": return "Content";
                case "MenuFlyoutItem": return "Text";
                default:
                    Assert.Fail("x:Uid on a <" + element + ">: add the property it should set to XamlProperty");
                    return "";
            }
        }

        private static Dictionary<string, string> CodeKeys()
        {
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in ProjectFiles("YTMusicWP", "*.cs"))
            {
                string text = File.ReadAllText(file);
                foreach (Match match in CodeKeyPattern.Matches(text))
                {
                    if (!keys.ContainsKey(match.Groups[1].Value)) keys[match.Groups[1].Value] = Path.GetFileName(file);
                }

                // Keys handed to Loc through an array, e.g. Loc.T(chipKeys[i], defaultTitlesEn[i]) on the home chips:
                // the literal is not in the call, so read the array it points at.
                foreach (Match use in ArrayKeyUsePattern.Matches(text))
                {
                    string array = use.Groups[1].Value;
                    Match list = Regex.Match(text, Regex.Escape("string[] " + array) + "\\s*=\\s*new\\[\\]\\s*\\{([^}]*)\\}");
                    if (!list.Success) continue;
                    foreach (Match literal in ArrayLiteralPattern.Matches(list.Groups[1].Value))
                    {
                        string key = literal.Groups[1].Value;
                        if (!keys.ContainsKey(key)) keys[key] = Path.GetFileName(file) + " (" + array + ")";
                    }
                }
            }
            return keys;
        }

        private static IEnumerable<string> ProjectFiles(string folder, string pattern)
        {
            string root = Path.Combine(ProjectRoot(), folder);
            Assert.IsTrue(Directory.Exists(root), "missing source folder: " + root);
            return Directory.GetFiles(root, pattern, SearchOption.AllDirectories)
                .Where(f => f.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) < 0
                            && f.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) < 0);
        }
    }
}
