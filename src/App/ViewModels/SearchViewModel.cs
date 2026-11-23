using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MindBuzz.App.Localization;
using MindBuzz.Core.Model;
using MindBuzz.Core.Search;

namespace MindBuzz.App.ViewModels;

/// <summary>A row of the results list: a map's heading, or a hit.</summary>
public abstract record SearchRow;

/// <summary>The heading above a map's hits, shown when searching all maps.</summary>
public sealed record SearchMapRow(string MapName, string CountText) : SearchRow;

/// <summary>
/// One hit in the results list: the topic, where the text matched, and an excerpt split around the match so the view
/// can highlight it.
/// </summary>
public sealed record SearchHitRow(
    SearchHit Hit,
    string TopicTitle,
    string FieldLabel,
    string Before,
    string Match,
    string After,
    string? Url) : SearchRow
{
    public bool IsLink => Url is not null;
}

/// <summary>
/// The search panel: an incremental, case-insensitive, culture-aware search through topic text,
/// notes and links, in the current map or all maps, with a results list to go to. Searching never changes the
/// document. The panel sits beside the detail view on the right.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    /// <summary>
    /// The most rows the list shows. More hits are counted but not listed: a list of thousands helps nobody, and
    /// building it would cost more than the search itself.
    /// </summary>
    internal const int MaxRows = 500;

    private readonly MainViewModel _owner;

    /// <summary>Every hit of the current query, in document order; the list shows the first <see
    /// cref="MaxRows"/>.</summary>
    private IReadOnlyList<SearchHit> _hits = [];

    internal SearchViewModel(MainViewModel owner)
    {
        _owner = owner;
    }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    /// <summary>All maps, or only the map on screen (the default).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrentMapOnly))]
    public partial bool AllMaps { get; set; }

    public bool IsCurrentMapOnly => !AllMaps;

    public ObservableCollection<SearchRow> Rows { get; } = [];

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    [ObservableProperty]
    public partial string CountText { get; private set; } = string.Empty;

    public bool HasHits => _hits.Count > 0;

    /// <summary>The hits, for the canvas highlight and tests.</summary>
    internal IReadOnlyList<SearchHit> Hits => _hits;

    /// <summary>The hit the list's selection is on, or null.</summary>
    internal SearchHit? CurrentHit => SelectedIndex >= 0 && SelectedIndex < Rows.Count && Rows[SelectedIndex] is SearchHitRow row ? row.Hit : null;

    public static string CloseTooltip => Strings.Search_Close;

    [RelayCommand]
    private void Close() => _owner.CloseSearch();

    [RelayCommand]
    private void CurrentMap() => AllMaps = false;

    /// <summary>"All Maps" is greyed out while the document has one map: it would find the same.</summary>
    public bool CanSearchAllMaps => _owner.Document.Maps.Length > 1;

    [RelayCommand(CanExecute = nameof(CanSearchAllMaps))]
    private void EveryMap() => AllMaps = true;

    /// <summary>The document changed: with a single map left, the scope falls back to the map on screen.</summary>
    internal void DocumentChanged()
    {
        OnPropertyChanged(nameof(CanSearchAllMaps));
        EveryMapCommand.NotifyCanExecuteChanged();
        if (AllMaps && !CanSearchAllMaps)
        {
            AllMaps = false;
        }
    }

    /// <summary>Opens a link hit's address through the one guarded gate.</summary>
    [RelayCommand]
    private void OpenLink(SearchHitRow? row)
    {
        if (row?.Url is { } url && LinkUrl.IsAllowed(url))
        {
            _owner.OpenUrl(url);
        }
    }

    partial void OnQueryChanged(string value) => Run(keepPosition: false);

    partial void OnAllMapsChanged(bool value) => Run(keepPosition: true);

    /// <summary>Searches again after the document or the map on screen changed, keeping the hit the list was
    /// on.</summary>
    internal void Refresh()
    {
        if (IsOpen)
        {
            Run(keepPosition: true);
        }
    }

    internal void SearchResultsChangedIfOpen()
    {
        if (IsOpen)
        {
            _owner.SearchResultsChanged();
        }
    }

    /// <summary>The list's row was chosen (click or Enter): go there.</summary>
    internal void GoToSelected()
    {
        if (CurrentHit is { } hit)
        {
            _owner.GoToHit(hit);
        }
    }

    /// <summary>F3 and Shift+F3, Enter and Shift+Enter in the box: the next or previous hit, wrapping around.</summary>
    internal void Step(bool forward)
    {
        List<int> hitRows = [.. Enumerable.Range(0, Rows.Count).Where(i => Rows[i] is SearchHitRow)];
        if (hitRows.Count == 0)
        {
            return;
        }

        int position = hitRows.IndexOf(SelectedIndex);
        int next = position < 0
            ? (forward ? 0 : hitRows.Count - 1)
            : (position + (forward ? 1 : -1) + hitRows.Count) % hitRows.Count;

        SelectedIndex = hitRows[next];
        GoToSelected();
    }

    private void Run(bool keepPosition)
    {
        SearchHit? kept = keepPosition ? CurrentHit : null;

        IEnumerable<Map> maps = AllMaps ? _owner.Document.Maps : [_owner.ActiveMap];
        _hits = MapSearch.Find(maps, Query, CultureInfo.CurrentCulture);

        var rows = new List<SearchRow>();
        int listed = 0;
        foreach (IGrouping<MapId, SearchHit> group in _hits.GroupBy(hit => hit.Map))
        {
            if (listed >= MaxRows)
            {
                break;
            }

            Map? map = _owner.Document.FindMap(group.Key);
            if (map is null)
            {
                continue;
            }

            if (AllMaps)
            {
                rows.Add(new SearchMapRow(map.Name, Count(group.Count())));
            }

            foreach (SearchHit hit in group)
            {
                if (listed++ >= MaxRows)
                {
                    break;
                }

                rows.Add(RowFor(map, hit));
            }
        }

        Sync(rows);

        CountText = Query.Trim().Length == 0 ? string.Empty
            : _hits.Count > MaxRows ? string.Format(CultureInfo.CurrentCulture, Strings.Search_CountCappedFormat, _hits.Count, MaxRows)
            : Count(_hits.Count);

        int index = kept is { } previous ? IndexOfRow(previous) : -1;
        SelectedIndex = index;
        OnPropertyChanged(nameof(HasHits));
        _owner.SearchResultsChanged();
    }

    /// <summary>
    /// Brings <see cref="Rows"/> to <paramref name="rows"/> by replacing only what changed between the rows equal at
    /// the
    /// start and at the end. Rows are records, so equal rows compare equal. Results follow every edit:
    /// rebuilding all 500 rows each keystroke cost about 35 ms on a 5,000-topic map, while an edit usually changes one
    /// row or none.
    /// </summary>
    private void Sync(List<SearchRow> rows)
    {
        int prefix = 0;
        while (prefix < rows.Count && prefix < Rows.Count && Rows[prefix].Equals(rows[prefix]))
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < rows.Count - prefix && suffix < Rows.Count - prefix && Rows[Rows.Count - 1 - suffix].Equals(rows[rows.Count - 1 - suffix]))
        {
            suffix++;
        }

        int oldMiddle = Rows.Count - prefix - suffix;
        int newMiddle = rows.Count - prefix - suffix;
        int common = Math.Min(oldMiddle, newMiddle);
        for (int i = 0; i < common; i++)
        {
            Rows[prefix + i] = rows[prefix + i];
        }

        for (int i = oldMiddle - 1; i >= common; i--)
        {
            Rows.RemoveAt(prefix + i);
        }

        for (int i = common; i < newMiddle; i++)
        {
            Rows.Insert(prefix + i, rows[prefix + i]);
        }
    }

    private int IndexOfRow(SearchHit hit)
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            if (Rows[i] is SearchHitRow row && row.Hit.Topic == hit.Topic && row.Hit.Field == hit.Field && row.Hit.LinkIndex == hit.LinkIndex)
            {
                return i;
            }
        }

        return -1;
    }

    private static string Count(int count) => count switch
    {
        0 => Strings.Search_NoHits,
        1 => Strings.Search_OneHit,
        _ => string.Format(CultureInfo.CurrentCulture, Strings.Search_CountFormat, count),
    };

    private static SearchHitRow RowFor(Map map, SearchHit hit)
    {
        Topic topic = map.Find(hit.Topic)!;
        string text = MapSearch.FieldText(topic, hit.Field, hit.LinkIndex);
        (string excerpt, int start, int length) = MapSearch.Excerpt(text, hit.Start, hit.Length);

        string label = hit.Field switch
        {
            HitField.Note => Strings.Search_InNote,
            HitField.LinkTitle or HitField.LinkUrl => Strings.Search_InLink,
            _ => Strings.Search_InTopic,
        };

        return new SearchHitRow(
            hit,
            MapName.For(topic.Text),
            label,
            excerpt[..start],
            excerpt.Substring(start, length),
            excerpt[(start + length)..],
            hit.Field is HitField.LinkTitle or HitField.LinkUrl ? topic.Links[hit.LinkIndex].Url : null);
    }
}
