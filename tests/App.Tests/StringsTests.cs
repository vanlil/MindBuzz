using System.Globalization;
using System.Reflection;
using System.Resources;
using MindBuzz.App.Localization;
using Xunit;

namespace MindBuzz.App.Tests;

public sealed class StringsTests
{
    [Fact]
    public void Every_string_resource_has_a_value()
    {
        PropertyInfo[] properties = [.. typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(string))];

        Assert.NotEmpty(properties);
        Assert.All(properties, property => Assert.False(
            string.IsNullOrWhiteSpace((string?)property.GetValue(null)),
            $"{property.Name} is empty"));
    }

    [Fact]
    public void Every_format_string_formats_with_one_argument_per_placeholder()
    {
        ResourceSet set = Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)!;

        foreach (System.Collections.DictionaryEntry entry in set)
        {
            var key = (string)entry.Key;
            if (!key.EndsWith("Format", StringComparison.Ordinal))
            {
                continue;
            }

            // Fails loudly when a translation drops or renumbers a placeholder.
            _ = string.Format(CultureInfo.InvariantCulture, (string)entry.Value!, "a", "b", "c", "d");
        }
    }
}
