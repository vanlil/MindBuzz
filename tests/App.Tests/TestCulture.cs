using System.Globalization;
using System.Runtime.CompilerServices;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>
/// The language of the strings under test. English by default, whatever the machine's language, because tests compare
/// texts; a test that checks a translation switches with <see cref="Use"/>.
/// </summary>
internal static class TestCulture
{
    /// <summary>The languages MindBuzz ships besides English (Strings.*.resx).</summary>
    internal static readonly string[] Translations = ["de", "fr", "es"];

    /// <summary>English and every translation, for theories.</summary>
    public static TheoryData<string> AllLanguages => new("en", "de", "fr", "es");

    [ModuleInitializer]
    internal static void UseEnglish()
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    /// <summary>Switches the strings to <paramref name="language"/> until disposed ("en" is the neutral English).</summary>
    internal static IDisposable Use(string language)
    {
        CultureInfo before = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = language == "en" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(language);
        return new Restore(before);
    }

    private sealed class Restore(CultureInfo before) : IDisposable
    {
        public void Dispose() => CultureInfo.CurrentUICulture = before;
    }
}
