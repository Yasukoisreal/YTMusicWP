using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
        private const int MinimumVisibleLiterals = 50;

        private static readonly Regex TagPattern = new Regex("<([A-Za-z:]+)((?:[^\">]|\"[^\"]*\")*)>", RegexOptions.Compiled);
        private static readonly Regex UidPattern = new Regex("x:Uid=\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex CodeKeyPattern = new Regex("Loc\\.[TF]\\(\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex ArrayKeyUsePattern = new Regex("Loc\\.[TF]\\(\\s*([A-Za-z_][A-Za-z0-9_]*)\\s*\\[", RegexOptions.Compiled);
        private static readonly Regex ArrayLiteralPattern = new Regex("\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>Attributes whose value the user reads on screen.</summary>
        private static readonly Regex VisibleAttributePattern = new Regex(
            "(?:^|\\s)(Text|Content|Header|PlaceholderText|ToolTipService\\.ToolTip)=\"([^\"]*)\"", RegexOptions.Compiled);

        private static readonly Regex XNamePattern = new Regex("x:Name=\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>An assignment of the form <c>SomeText.Text =</c> (or Content/PlaceholderText).</summary>
        private static readonly Regex AssignedNamePattern = new Regex(
            "([A-Za-z_][A-Za-z0-9_]*)\\s*\\.\\s*(Text|Content|PlaceholderText)\\s*=", RegexOptions.Compiled);

        private static readonly Regex AsciiWordPattern = new Regex("[A-Za-z]", RegexOptions.Compiled);

        private static readonly Regex IdentifierPattern = new Regex("[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

        /// <summary>A <c>string[] name = new[] { "a", "b" }</c> declaration, captured as name + body.</summary>
        private static readonly Regex StringArrayPattern = new Regex(
            "string\\[\\]\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*new\\[\\]\\s*\\{([^}]*)\\}", RegexOptions.Compiled);

        /// <summary>
        /// The few literals that stay as they are, keyed by file and text, with the reason. Everything else that has
        /// no x:Uid is either a binding, a glyph, a value the code writes when the screen opens, or a bug.
        /// </summary>
        private static readonly Dictionary<string, string> NotTranslatedLiterals = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { Key("MainPage.xaml", "@2026 YTMusicWP v2.4.0 Yasukoisreal"), "product name and version in the about footer" },
            { Key("MainPage.xaml", "A+"), "lyric size control: a symbol, not a word" },
            { Key("MainPage.xaml", "A-"), "lyric size control: a symbol, not a word" },
            { Key("MainPage.xaml", "OPUS"), "audio codec badge" },
            { Key("ListenTogetherPanel.xaml", "The Meowery · Poland"), "the default room server's own name" },
            { Key("ListenTogetherPanel.xaml", "wss://..."), "an example URL" },
            { Key("SettingsView.xaml", "fm"), "the Last.fm mark" },
            { Key("SettingsView.xaml", "Last.fm"), "a brand name" },
            { Key("SettingsView.xaml", "YTMusicWP"), "the app's own name" },
        };

        private static string Key(string file, string literal)
        {
            return file + "\u0000" + literal;
        }

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
        public void EveryVisibleXamlLiteralHasAUid()
        {
            // An x:Uid is what makes markup text translatable, and a label written straight into the markup looks
            // fine in English and stays English on an Italian or Vietnamese phone: only reading every file by hand
            // caught those. This walks the markup instead, and accepts a literal only when something else explains
            // why it is not translated: it is a binding or a glyph, the code fills that element in when the screen
            // opens, a Loc array in the code already carries it, or it is one of the few names in
            // NotTranslatedLiterals.
            var assignedNames = CodeAssignedNames();
            var codeLiterals = CodeManagedLiterals();
            var offenders = new List<string>();
            int scanned = 0;

            foreach (string file in ProjectFiles("YTMusicWP", "*.xaml"))
            {
                string name = Path.GetFileName(file);
                string text = File.ReadAllText(file);
                foreach (Match tag in TagPattern.Matches(text))
                {
                    string attributes = tag.Groups[2].Value;
                    if (attributes.Contains("x:Uid=\"")) continue;
                    // A Tag marks a data item (the region and content-language pickers): its text follows the content
                    // language the user picked, not the interface one.
                    if (attributes.Contains("Tag=\"")) continue;

                    Match named = XNamePattern.Match(attributes);
                    bool filledFromCode = named.Success && assignedNames.Contains(named.Groups[1].Value);

                    foreach (Match visible in VisibleAttributePattern.Matches(attributes))
                    {
                        string value = visible.Groups[2].Value;
                        if (value.StartsWith("{")) continue;                    // a binding or a resource reference
                        if (!AsciiWordPattern.IsMatch(WebUtility.HtmlDecode(value))) continue; // an entity, a glyph or empty
                        scanned++;
                        if (filledFromCode) continue;
                        if (codeLiterals.Contains(value)) continue;
                        if (NotTranslatedLiterals.ContainsKey(Key(name, value))) continue;

                        int line = text.Substring(0, tag.Index).Count(c => c == '\n') + 1;
                        offenders.Add(name + "(" + line + "): <" + tag.Groups[1].Value + "> "
                                      + visible.Groups[1].Value + "=\"" + value + "\"");
                    }
                }
            }

            Assert.IsTrue(scanned >= MinimumVisibleLiterals,
                "the visibility scan read only " + scanned + " literals: has the markup changed shape?");
            Assert.AreEqual(0, offenders.Count,
                "visible XAML text with no x:Uid - give it one, or list it in NotTranslatedLiterals with the reason:"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders.OrderBy(o => o)));
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

        /// <summary>
        /// The element names the code writes to ("SomeLabel.Text = ..."). A literal in the markup of such an
        /// element is the value shown before the screen loads, not the text the user ends up reading.
        /// </summary>
        private static HashSet<string> CodeAssignedNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in ProjectFiles("YTMusicWP", "*.cs"))
            {
                foreach (Match match in AssignedNamePattern.Matches(File.ReadAllText(file)))
                {
                    names.Add(match.Groups[1].Value);
                }
            }
            return names;
        }

        /// <summary>
        /// The literals that sit in a string array feeding Loc.T/Loc.F (the home and search chip titles). The chip
        /// panels are built in the markup with one literal each and their text is then replaced from such an array
        /// (Loc.T(chipKeys[i], defaultTitlesEn[i])), so the array is what the test should be reading.
        /// </summary>
        private static HashSet<string> CodeManagedLiterals()
        {
            var literals = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in ProjectFiles("YTMusicWP", "*.cs"))
            {
                string text = File.ReadAllText(file);

                // The arrays feeding Loc are the ones named on a line that calls Loc.T/Loc.F: both the key list and
                // the English titles next to it sit in that argument list.
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (string line in text.Split('\n'))
                {
                    if (line.IndexOf("Loc.T(", StringComparison.Ordinal) < 0
                        && line.IndexOf("Loc.F(", StringComparison.Ordinal) < 0) continue;
                    foreach (Match identifier in IdentifierPattern.Matches(line)) names.Add(identifier.Value);
                }
                if (names.Count == 0) continue;

                foreach (Match declaration in StringArrayPattern.Matches(text))
                {
                    if (!names.Contains(declaration.Groups[1].Value)) continue;
                    foreach (Match literal in ArrayLiteralPattern.Matches(declaration.Groups[2].Value))
                    {
                        literals.Add(literal.Groups[1].Value);
                    }
                }
            }
            return literals;
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
