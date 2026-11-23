using System.Globalization;
using MindBuzz.Core.Model;
using MindBuzz.Core.Search;
using Xunit;

namespace MindBuzz.Core.Tests;

/// <summary>Search: what matches, in which order, and the excerpt the results list shows.</summary>
public sealed class MapSearchTests
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private static Map MapOf(string id, Topic root) => new(new MapId(id), root);

    private static Topic T(string id, string text, params Topic[] children) =>
        new(new TopicId(id), text) { Children = [.. children] };

    [Fact]
    public void Hits_come_in_document_order_with_text_note_and_links_per_topic()
    {
        Topic linked = T("B", "Beta") with
        {
            Note = "about plans",
            Links = [new InternetLink("https://plans.example", "Plans")],
        };
        Map first = MapOf("M1", T("A", "Plan", linked, T("C", "Gamma plan")));
        Map second = MapOf("M2", T("D", "Other plan"));

        IReadOnlyList<SearchHit> hits = MapSearch.Find([first, second], "plan", German);

        Assert.Equal(
            [("A", HitField.Text), ("B", HitField.Note), ("B", HitField.LinkTitle), ("B", HitField.LinkUrl), ("C", HitField.Text), ("D", HitField.Text)],
            hits.Select(hit => (hit.Topic.Value, hit.Field)));
        Assert.Equal(new MapId("M2"), hits[^1].Map);
    }

    [Fact]
    public void Matching_ignores_case_and_reports_where_the_match_is()
    {
        Map map = MapOf("M", T("A", "The ROADMAP for 2027"));

        SearchHit hit = Assert.Single(MapSearch.Find([map], "roadmap", German));

        Assert.Equal((4, 7), (hit.Start, hit.Length));
    }

    [Fact]
    public void Collapsed_branches_are_searched_too()
    {
        Map map = MapOf("M", T("A", "Root", T("B", "Hidden parent", T("C", "needle")) with { IsCollapsed = true }));

        Assert.Equal("C", Assert.Single(MapSearch.Find([map], "needle", German)).Topic.Value);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void A_blank_query_finds_nothing(string query)
    {
        Assert.Empty(MapSearch.Find([MapOf("M", T("A", "anything"))], query, German));
    }

    /// <summary>
    /// The culture's own rules with case and accents ignored, observed with .NET 10 and ICU: "ß" then
    /// equals "ss" too. The Turkish dotted İ matches "i" only under Turkish rules.
    /// </summary>
    [Theory]
    [InlineData("de-DE", "Straße", "STRASSE", true)]
    [InlineData("de-DE", "Größe", "GRÖSSE", true)]
    [InlineData("de-DE", "résumé", "RÉSUMÉ", true)]
    [InlineData("fr-FR", "Café", "cafe", true)]
    [InlineData("de-DE", "Größe", "grosse", true)]
    [InlineData("de-DE", "Größe", "GROẞE", true)]
    [InlineData("de-DE", "Größe", "große", true)]
    [InlineData("de-DE", "Größe", "grose", false)]
    [InlineData("tr-TR", "İstanbul", "istanbul", true)]
    [InlineData("tr-TR", "Istanbul", "istanbul", false)]
    [InlineData("en-US", "Istanbul", "istanbul", true)]
    public void Matching_follows_the_culture(string culture, string text, string query, bool found)
    {
        Map map = MapOf("M", T("A", text));

        Assert.Equal(found, MapSearch.Find([map], query, CultureInfo.GetCultureInfo(culture)).Count == 1);
    }

    [Fact]
    public void A_large_map_keeps_document_order_when_searched_in_parallel()
    {
        Topic[] children = [.. Enumerable.Range(0, 2000).Select(i => T($"T{i:D4}", $"item {i}"))];
        Map map = MapOf("M", T("R", "root", children));

        IReadOnlyList<SearchHit> hits = MapSearch.Find([map], "item", German);

        Assert.Equal(children.Select(c => c.Id), hits.Select(hit => hit.Topic));
    }

    [Fact]
    public void The_excerpt_keeps_the_match_with_context_on_one_line()
    {
        const string text = "A long note with several words before\nthe needle and some more words after it";
        int start = text.IndexOf("needle", StringComparison.Ordinal);

        (string excerpt, int at, int length) = MapSearch.Excerpt(text, start, 6, context: 12, width: 30);

        Assert.StartsWith("…", excerpt, StringComparison.Ordinal);
        Assert.EndsWith("…", excerpt, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", excerpt, StringComparison.Ordinal);
        Assert.Equal("needle", excerpt.Substring(at, length));
    }
}
