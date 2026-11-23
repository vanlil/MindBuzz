using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MindBuzz.App.Localization;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;

namespace MindBuzz.App.ViewModels;

/// <summary>One row of the link list: the title when there is one, and the address.</summary>
public sealed record LinkRow(string Title, string Url)
{
    public string Heading => Title.Length > 0 ? Title : Url;

    public bool HasTitle => Title.Length > 0;

    /// <summary>" (address)" after the title, for the one-line rows in the topic panel; empty without a
    /// title.</summary>
    public string UrlSuffix => HasTitle ? $" ({Url})" : string.Empty;
}

/// <summary>
/// The links of one topic in the detail view: arrows select, Enter opens, F2 edits, Delete removes,
/// Alt+Up/Down reorders; the buttons do the same. Every change is a document command, so each one is an undo step and
/// the list always shows what the document holds. The keyboard model is tested without a view.
/// </summary>
public sealed partial class LinkListViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly MapId _map;
    private readonly TopicId _topic;

    internal LinkListViewModel(MainViewModel owner, MapId map, TopicId topic)
    {
        _owner = owner;
        _map = map;
        _topic = topic;
        Refresh(selectIndex: 0);
    }

    public ObservableCollection<LinkRow> Links { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(EditCommand), nameof(RemoveCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    public partial int SelectedIndex { get; set; } = -1;

    public bool HasSelection => SelectedIndex >= 0 && SelectedIndex < Links.Count;

    public bool IsEmpty => Links.Count == 0;

    private InternetLink? Selected =>
        HasSelection && CurrentLinks() is { } links && SelectedIndex < links.Length ? links[SelectedIndex] : null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Open()
    {
        if (Selected is { } link)
        {
            _owner.OpenUrl(link.Url);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditAsync()
    {
        if (Selected is not { } link)
        {
            return;
        }

        int index = SelectedIndex;
        InternetLink? edited = await _owner.AskForLinkAsync(link, adding: false).ConfigureAwait(true);
        if (edited is not null)
        {
            _owner.ApplyFromList(new EditLink(_map, _topic, index, edited));
        }

        Refresh(index);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        // Worked out before the change: the detail view reloads this list while the change is shown.
        int index = SelectedIndex;
        int next = Math.Min(index, Links.Count - 2);
        _owner.ApplyFromList(new RemoveLink(_map, _topic, index));
        Refresh(next);
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Move(1);

    [RelayCommand]
    private async Task AddAsync()
    {
        InternetLink? link = await _owner.AskForLinkAsync(null, adding: true).ConfigureAwait(true);
        if (link is not null)
        {
            _owner.ApplyFromList(new AddLink(_map, _topic, link));
            Refresh(int.MaxValue);
        }
    }

    private bool CanMoveUp() => HasSelection && SelectedIndex > 0;

    private bool CanMoveDown() => HasSelection && SelectedIndex < Links.Count - 1;

    private void Move(int delta)
    {
        int index = SelectedIndex;
        if (!HasSelection || index + delta < 0 || index + delta >= Links.Count)
        {
            return;
        }

        _owner.ApplyFromList(new MoveLink(_map, _topic, index, index + delta));
        Refresh(index + delta);
    }

    private System.Collections.Immutable.ImmutableArray<InternetLink>? CurrentLinks() =>
        _owner.Document.FindMap(_map)?.Find(_topic)?.Links;

    /// <summary>The topic the list shows.</summary>
    internal TopicId Topic => _topic;

    /// <summary>Reloads the rows after the document changed elsewhere (undo, the topic panel), keeping the
    /// selection.</summary>
    internal void Reload() => Refresh(SelectedIndex);

    /// <summary>Reloads the rows from the document and selects <paramref name="selectIndex"/>, clamped to the
    /// list.</summary>
    private void Refresh(int selectIndex)
    {
        Topic? topic = _owner.Document.FindMap(_map)?.Find(_topic);

        Links.Clear();
        foreach (InternetLink link in topic?.Links ?? [])
        {
            Links.Add(new LinkRow(link.Title, link.Url));
        }

        SelectedIndex = Links.Count == 0 ? -1 : Math.Clamp(selectIndex, 0, Links.Count - 1);
        OnPropertyChanged(nameof(IsEmpty));
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        OpenCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }
}
