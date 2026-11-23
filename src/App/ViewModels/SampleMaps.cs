using System.Globalization;
using MindBuzz.App.Commands;
using MindBuzz.App.Localization;
using MindBuzz.Core.Model;

namespace MindBuzz.App.ViewModels;

/// <summary>
/// File > New Sample Mind Maps: two small maps for someone who has never seen a mind map. The first
/// explains mind maps with its own structure and shows this platform's keys, and what else there is to try (formatting,
/// several topics at once, full screen, the map as a picture); the second is an everyday example, a
/// weekend trip, with markers, notes, a link and two relationships in different colours and line types. Every word is
/// in <c>Strings.resx</c>, like any other text the user sees.
/// </summary>
internal static class SampleMaps
{
    /// <summary>The articles the sample links to, per Wikipedia language; English for any other language.</summary>
    private static readonly Dictionary<string, string> MindMapArticles = new()
    {
        ["en"] = "Mind_map", ["de"] = "Mindmap", ["fr"] = "Carte_heuristique", ["es"] = "Mapa_mental",
    };

    private static readonly Dictionary<string, string> BelemArticles = new()
    {
        ["en"] = "Bel%C3%A9m_Tower", ["de"] = "Torre_de_Bel%C3%A9m", ["fr"] = "Tour_de_Bel%C3%A9m", ["es"] = "Torre_de_Bel%C3%A9m",
    };

    /// <summary>
    /// The article in the language the sample is written in. In code, not in <c>Strings.resx</c>: a language file
    /// must not be able to point a link elsewhere (<see cref="AppInfo.RepositoryUrl"/>).
    /// </summary>
    internal static string Wikipedia(IReadOnlyDictionary<string, string> articles)
    {
        string language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (!articles.ContainsKey(language))
        {
            language = "en";
        }

        return $"https://{language}.wikipedia.org/wiki/{articles[language]}";
    }

    internal static Document Create(IIdSource ids, CommandRegistry commands, HostPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(commands);

        return new Document([WhatIsAMindMap(ids, commands, platform), WeekendTrip(ids)]);
    }

    private static Map WhatIsAMindMap(IIdSource ids, CommandRegistry commands, HostPlatform platform)
    {
        string Key(CommandId id) =>
            commands[id].Gestures.KeyGestures(platform) is { Count: > 0 } gestures ? GestureText.Format(gestures[0], platform) : string.Empty;

        Topic shortWords = T(ids, Strings.Sample_ShortWords);
        Topic notes = T(ids, Strings.Sample_Notes) with { Note = Strings.Sample_NotesNote };
        Topic links = T(ids, Strings.Sample_Links) with { Links = [new InternetLink(Wikipedia(MindMapArticles), Strings.Sample_LinksTitle)] };
        Topic markers = T(ids, Strings.Sample_Markers) with { Markers = [new Marker(MarkerGroups.Priority, "1")] };
        Topic progress = T(ids, Strings.Sample_Progress) with { Markers = [new Marker(MarkerGroups.Progress, "50")] };
        Topic detailView = T(ids, Format(Strings.Sample_KeyDetailView, Key(CommandId.OpenNotes)));
        Topic lines = T(ids, Strings.Sample_Lines);

        Topic root = T(ids, Strings.Sample_IntroCentral) with
        {
            Note = Strings.Sample_IntroNote,
            Links = [new InternetLink(Wikipedia(MindMapArticles), Strings.Sample_LinksTitle)],
            Children =
            [
                Main(ids, Strings.Sample_StartMiddle, MainSide.Right, T(ids, Strings.Sample_OneIdea), shortWords),
                Main(ids, Strings.Sample_BranchOut, MainSide.Right, T(ids, Strings.Sample_MainTopics), T(ids, Strings.Sample_Subtopics)),
                Main(ids, Strings.Sample_AddDetails, MainSide.Right, notes, links, markers, progress, detailView),
                Main(
                    ids,
                    Strings.Sample_TryIt,
                    MainSide.Left,
                    T(ids, Format(Strings.Sample_KeyNewTopic, Key(CommandId.NewSiblingAfter))),
                    T(ids, Format(Strings.Sample_KeySubtopic, Key(CommandId.NewSubtopic))),
                    T(ids, Format(Strings.Sample_KeyEdit, Key(CommandId.EditText))),
                    T(ids, Strings.Sample_Drag),
                    T(ids, Strings.Sample_RightClick),
                    T(ids, Format(Strings.Sample_KeySearch, Key(CommandId.Find))),
                    T(ids, Format(Strings.Sample_KeyboardHelp, Key(CommandId.KeyboardShortcuts)))) with
                {
                    Markers = [Icon("idea")],
                },
                Main(ids, Strings.Sample_Connect, MainSide.Left, lines),
                Main(
                    ids,
                    Strings.Sample_MoreToTry,
                    MainSide.Right,

                    // The title shows what it says: bold, italic and underlined words.
                    T(ids, string.Format(CultureInfo.CurrentCulture, Strings.Sample_Formatting, Key(CommandId.Bold), Key(CommandId.Italic), Key(CommandId.Underline))),
                    T(ids, Format(Strings.Sample_MultiSelect, platform == HostPlatform.MacOS ? "⌘" : Strings.Key_Ctrl)),
                    T(ids, Format(Strings.Sample_FullScreen, Key(CommandId.ToggleFullScreen))),
                    T(ids, string.Format(CultureInfo.CurrentCulture, Strings.Sample_CopyImage, MenuText.WithoutAccessKey(Strings.Menu_Edit), MenuText.WithoutAccessKey(Strings.Command_CopyMapImage)))),
            ],
        };

        // "Lines connect ideas across branches" is itself connected: to "Keep it short", across the map.
        var relationship = new Relationship(ids.NextRelationship(), lines.Id, shortWords.Id, RelationshipDirection.OneWay);
        return new Map(ids.NextMap(), root) { Relationships = [relationship] };
    }

    private static Map WeekendTrip(IIdSource ids)
    {
        Topic bookFlights = T(ids, Strings.Sample_BookFlights) with
        {
            Markers = [new Marker(MarkerGroups.Priority, "1"), new Marker(MarkerGroups.Progress, "100")],
        };
        Topic flightCost = T(ids, Strings.Sample_FlightCost);
        Topic sintra = T(ids, Strings.Sample_Sintra) with { Markers = [new Marker(MarkerGroups.Priority, "2"), Icon("pin")] };
        Topic shoes = T(ids, Strings.Sample_Shoes);

        // Right: getting there, staying, doing, top to bottom. Left: the budget at the top, packing at the bottom (the
        // left side's list runs bottom to top). The two relationships then arc across the top and the bottom of the
        // map.
        Topic root = T(ids, Strings.Sample_TripCentral) with
        {
            Note = Strings.Sample_TripNote,
            Links = [new InternetLink("https://www.visitlisboa.com", Strings.Sample_LisbonLink)],
            Children =
            [
                Main(ids, Strings.Sample_GettingThere, MainSide.Right, bookFlights, T(ids, Strings.Sample_Metro)),
                Main(ids, Strings.Sample_Stay, MainSide.Right, T(ids, Strings.Sample_Hotel), T(ids, Strings.Sample_CheckIn)),
                Main(
                    ids,
                    Strings.Sample_ThingsToDo,
                    MainSide.Right,
                    T(ids, Strings.Sample_Tram),
                    T(ids, Strings.Sample_Belem) with { Links = [new InternetLink(Wikipedia(BelemArticles), Strings.Sample_BelemLink)] },
                    sintra,
                    T(ids, Strings.Sample_Fado) with { Markers = [Icon("heart")] }),
                Main(
                    ids,
                    Strings.Sample_Packing,
                    MainSide.Left,
                    T(ids, Strings.Sample_Passport) with { Markers = [new Marker(MarkerGroups.Priority, "1"), Icon("alert")] },
                    shoes,
                    T(ids, Strings.Sample_Sunscreen)) with
                {
                    Markers = [new Marker(MarkerGroups.Progress, "25")],
                },
                Main(ids, Strings.Sample_Budget, MainSide.Left, flightCost, T(ids, Strings.Sample_HotelCost), T(ids, Strings.Sample_FoodCost)) with
                {
                    Markers = [Icon("money")],
                },
            ],
        };

        // Two relationships across branches, in two colours and line types.
        Relationship[] relationships =
        [
            new(ids.NextRelationship(), bookFlights.Id, flightCost.Id, RelationshipDirection.OneWay) { Color = RelationshipColor.Purple, Line = RelationshipLine.Dotted },
            new(ids.NextRelationship(), sintra.Id, shoes.Id, RelationshipDirection.OneWay) { Color = RelationshipColor.Teal, Line = RelationshipLine.Dashed },
        ];
        return new Map(ids.NextMap(), root) { Relationships = [.. relationships] };
    }

    private static Topic T(IIdSource ids, string text) => new(ids.NextTopic(), text);

    /// <summary>A topic icon on the left border.</summary>
    private static Marker Icon(string value) => new(MarkerGroups.Icon, value);

    private static Topic Main(IIdSource ids, string text, MainSide side, params Topic[] children) =>
        T(ids, text) with { Side = side, Children = [.. children] };

    private static string Format(string format, string key) => string.Format(CultureInfo.CurrentCulture, format, key);
}
