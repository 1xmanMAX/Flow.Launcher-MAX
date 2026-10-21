using System.Reflection;
using Flow.Launcher.Resources.Controls;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Flow.Launcher.Test
{
    [TestFixture]
    internal class CodeHighlightThemeTest
    {
        private static readonly FieldInfo ActiveThemeBackingField =
            typeof(CodeHighlightTheme).GetField("_active",
                BindingFlags.Static | BindingFlags.NonPublic);

        private object _savedTheme;

        [SetUp]
        public void SetUp() => _savedTheme = ActiveThemeBackingField.GetValue(null);

        [TearDown]
        public void TearDown() => ActiveThemeBackingField.SetValue(null, _savedTheme);

        [Test]
        public void GivenAutoSetting_WhenAppIsDark_ThenActiveThemeIsADarkTheme()
        {
            CodeHighlightTheme.Apply("Auto", isDark: true);

            ClassicAssert.AreEqual("VS Code Dark+", CodeHighlightTheme.Active.Name);
        }

        [Test]
        public void GivenAutoSetting_WhenAppIsLight_ThenActiveThemeIsALightTheme()
        {
            CodeHighlightTheme.Apply("Auto", isDark: false);

            ClassicAssert.AreEqual("VS Code Light", CodeHighlightTheme.Active.Name);
        }

        [Test]
        public void GivenExplicitTheme_WhenApplied_ThenActiveThemeMatchesRegardlessOfAppScheme()
        {
            CodeHighlightTheme.Apply("OneDark", isDark: false);

            ClassicAssert.AreEqual("One Dark", CodeHighlightTheme.Active.Name);
        }

        [Test]
        public void GivenEmptySetting_WhenApplied_ThenFallsBackToAutoBehaviour()
        {
            CodeHighlightTheme.Apply("", isDark: false);

            ClassicAssert.AreEqual("VS Code Light", CodeHighlightTheme.Active.Name);
        }

        [Test]
        public void GivenUnrecognisedSetting_WhenApplied_ThenFallsBackToAutoBehaviour()
        {
            CodeHighlightTheme.Apply("BogusTheme", isDark: true);

            ClassicAssert.AreEqual("VS Code Dark+", CodeHighlightTheme.Active.Name);
        }
    }
}
