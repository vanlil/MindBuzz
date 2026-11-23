using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using MindBuzz.App.Commands;
using MindBuzz.App.ViewModels;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Canvas;

/// <summary>
/// The mind-map canvas: one custom-drawn control. It lays the map out with Core's
/// <see cref="LayoutEngine"/>, draws only what is visible through a <see cref="SpatialGrid"/>, caches shaped
/// text, and draws text as simple bars when zoomed far out (level of detail).
/// <para>
/// Every colour is resolved from the semantic tokens at draw time, never cached, so a theme switch shows at once. The
/// selection is owned by the view model; the canvas shows it and reports clicks through the
/// two-way <see cref="SelectedTopicProperty"/>.
/// </para>
/// <para>
/// In-place editing uses a real <see cref="TextBox"/> laid over the topic, never custom text input, so IME,
/// dead keys and emoji work. The canvas only places and styles it; the session's
/// rules live in the view model.
/// </para>
/// </summary>
internal sealed partial class MapCanvas : Control, ViewModels.IMapView
{
    public static readonly StyledProperty<Map?> MapProperty =
        AvaloniaProperty.Register<MapCanvas, Map?>(nameof(Map));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<MapCanvas, double>(nameof(Zoom), 1, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<TopicId?> SelectedTopicProperty =
        AvaloniaProperty.Register<MapCanvas, TopicId?>(nameof(SelectedTopic), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<MenuEntry>?> TopicMenuProperty =
        AvaloniaProperty.Register<MapCanvas, IReadOnlyList<MenuEntry>?>(nameof(TopicMenu));

    public static readonly StyledProperty<RelationshipId?> SelectedRelationshipProperty =
        AvaloniaProperty.Register<MapCanvas, RelationshipId?>(nameof(SelectedRelationship), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<TopicId?> LinkSourceProperty =
        AvaloniaProperty.Register<MapCanvas, TopicId?>(nameof(LinkSource));

    /// <summary>Topics with search hits, highlighted while the search panel is open.</summary>
    public static readonly StyledProperty<IReadOnlySet<TopicId>?> SearchHitsProperty =
        AvaloniaProperty.Register<MapCanvas, IReadOnlySet<TopicId>?>(nameof(SearchHits));

    public static readonly StyledProperty<TopicId?> LinkTargetProperty =
        AvaloniaProperty.Register<MapCanvas, TopicId?>(nameof(LinkTarget));

    public static readonly StyledProperty<IReadOnlyList<MenuEntry>?> RelationshipMenuProperty =
        AvaloniaProperty.Register<MapCanvas, IReadOnlyList<MenuEntry>?>(nameof(RelationshipMenu));

    public static readonly StyledProperty<IReadOnlyList<MenuEntry>?> CanvasMenuProperty =
        AvaloniaProperty.Register<MapCanvas, IReadOnlyList<MenuEntry>?>(nameof(CanvasMenu));

    /// <summary>Space kept between a selected topic and the edge when it is scrolled into view.</summary>
    private const double VisibleMargin = 32;

    /// <summary>Below this zoom, text is drawn as bars: glyphs that small cannot be read and cost the most to
    /// draw.</summary>
    internal const double TextZoomThreshold = 0.4;

    /// <summary>
    /// Zoom and scroll steps and structure changes animate this long (120–200 ms, eased). Settable
    /// as a test seam: headless tests wait for every glide in real time, which cost more than half of the suite's
    /// run time, so the tests shorten it to a millisecond. Animations still start, so tests can check them.
    /// </summary>
    internal static TimeSpan AnimationTime { get; set; } = TimeSpan.FromMilliseconds(160);

    private readonly TopicText _text = new();
    private readonly LayoutEngine _engine;
    private readonly Dictionary<int, StreamGeometry> _connectorGeometry = [];
    private readonly Dictionary<int, TextLayout> _countText = [];

    private MapLayout? _layout;

    /// <summary>How far below a connector or relationship its shadow line runs, in map units.</summary>
    private const double LineShadowOffset = 1.5;
    private SpatialGrid? _grid;
    private Viewport _viewport = new(1, default);
    private bool _fitPending = true;
    private bool _settingZoom;

    private TopicId? _hover;

    /// <summary>The zoom and scroll of the maps not on screen.</summary>
    private readonly Dictionary<MapId, Viewport> _views = [];

    private readonly TextBox _editor;
    private TopicId? _editing;

    /// <summary>Keeps the in-place editor within the topic line limit.</summary>
    private readonly TitleLineGuard _titleGuard;
    private bool _closingEditor;

    private Point? _panFrom;

    /// <summary>How far a click on the empty canvas may move and still count as a click rather than a pan.</summary>
    private const double ClickSlop = 4;

    /// <summary>Where a left click on the empty canvas went down, until it moves far enough to be a pan.</summary>
    private Point? _clearOnRelease;

    private Viewport _animationFrom;
    private Viewport _animationTo;
    private TimeSpan? _animationStart;
    private bool _animating;

    public MapCanvas()
    {
        _engine = new LayoutEngine(_text);
        ClipToBounds = true;
        Focusable = true;
        ActualThemeVariantChanged += (_, _) =>
        {
            StyleEditor();
            InvalidateVisual();
        };

        _editor = new TextBox
        {
            Classes = { "topicEditor" },
            IsVisible = false,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontFamily = new FontFamily("fonts:Inter#Inter"),
        };
        _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _titleGuard = new TitleLineGuard(() => _editing is { } id ? _layout?.Find(id)?.Level : null);
        _editor.TextChanged += (_, _) =>
        {
            // An edit beyond the line limit is put back; putting it back raises this event again with the old text.
            if (_editing is not null && !_closingEditor && _titleGuard.Check(_editor))
            {
                EditTextChanged?.Invoke(this, _editor.Text ?? string.Empty);
                InvalidateMeasure();
            }
        };
        _editor.LostFocus += (_, _) => CloseEditor(commit: true);

        VisualChildren.Add(_editor);
        LogicalChildren.Add(_editor);

        // After the line guard's handler, so the formatter sees the text the guard let through.
        CreateFormatBar();

        AddHandler(ContextRequestedEvent, OnContextRequested);
        InstallRelationshipKeys();
    }

    /// <summary>The editor's text changed while typing.</summary>
    public event EventHandler<string>? EditTextChanged;

    /// <summary>Enter, or focus leaving the editor: keep the text.</summary>
    public event EventHandler<string>? EditCommitted;

    /// <summary>Esc: put the text back.</summary>
    public event EventHandler? EditCancelled;

    /// <summary>A printable character was typed on the selected topic: it starts editing and replaces the
    /// text.</summary>
    public event EventHandler<string>? TextTyped;

    /// <summary>A topic was double-clicked: start editing it.</summary>
    public event EventHandler<TopicId>? TopicDoubleClicked;

    public Map? Map
    {
        get => GetValue(MapProperty);
        set => SetValue(MapProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>Every selected topic (multi-select), <see cref="SelectedTopic"/> among them.</summary>
    public static readonly StyledProperty<IReadOnlyList<TopicId>> SelectedTopicsProperty =
        AvaloniaProperty.Register<MapCanvas, IReadOnlyList<TopicId>>(nameof(SelectedTopics), []);

    public IReadOnlyList<TopicId> SelectedTopics
    {
        get => GetValue(SelectedTopicsProperty);
        set => SetValue(SelectedTopicsProperty, value);
    }

    /// <summary>The selected topics as a set, for drawing and hit tests.</summary>
    private HashSet<TopicId> _selectedSet = [];

    /// <summary>A plain press on one of several selected topics: unless it becomes a drag, that one alone.</summary>
    private TopicId? _selectOnlyOnRelease;

    /// <summary>⌘-click (Ctrl-click on Windows and Linux) on a topic: into the selection or out of it.</summary>
    public event EventHandler<TopicId>? TopicToggled;

    /// <summary>The selection should be this one topic alone (a click on one of several, Esc).</summary>
    public event EventHandler<TopicId>? SelectOnlyRequested;

    public TopicId? SelectedTopic
    {
        get => GetValue(SelectedTopicProperty);
        set => SetValue(SelectedTopicProperty, value);
    }

    public IReadOnlyList<MenuEntry>? TopicMenu
    {
        get => GetValue(TopicMenuProperty);
        set => SetValue(TopicMenuProperty, value);
    }

    public IReadOnlyList<MenuEntry>? CanvasMenu
    {
        get => GetValue(CanvasMenuProperty);
        set => SetValue(CanvasMenuProperty, value);
    }

    /// <inheritdoc cref="IMapView.Layout"/>
    public MapLayout? Layout => _layout;

    /// <summary>True while the in-place editor is open: a test seam.</summary>
    internal bool IsEditorOpen => _editing is not null;

    internal TextBox Editor => _editor;

    /// <summary>Draw the frame statistics overlay (Debug builds, or <c>--stats</c>).</summary>
    public bool ShowStats { get; set; }

    public FrameStats Stats { get; } = new();

    /// <summary>The current layout; a test seam and the benchmark's way in.</summary>
    internal MapLayout? CurrentLayout => _layout;

    internal Viewport Viewport => _viewport;

    /// <summary>How long the last layout took, for the overlay and the benchmark.</summary>
    internal double LastLayoutMs { get; private set; }

    /// <summary>How many topics the last frame drew.</summary>
    internal int LastVisibleTopics { get; private set; }

    public void FitToWindow() => FitToWindow(animate: true);

    public void FitToWindow(bool animate) => SetViewport(Viewport.Fit(_layout?.Bounds ?? default, Bounds.Size), animate);

    public void ZoomStep(bool zoomIn) => SetZoomAroundCentre(Viewport.NextStep(_viewport.Zoom, zoomIn), animate: true);

    public void ZoomTo(double zoom) => SetZoomAroundCentre(zoom, animate: true);

    /// <inheritdoc cref="IMapView.ForgetViews"/>
    public void ForgetViews()
    {
        _views.Clear();
        _fitPending = true;
    }

    internal void SetViewport(Viewport viewport, bool animate)
    {
        if (!animate || TopLevel.GetTopLevel(this) is not { } top)
        {
            _animating = false;
            Apply(viewport);
            return;
        }

        _animationFrom = _viewport;
        _animationTo = KeepInReach(viewport);
        _animationStart = null;

        if (!_animating)
        {
            _animating = true;
            top.RequestAnimationFrame(Animate);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // What a screen reader hears follows the map and the selection (MapCanvas.Automation.cs).
        if (change.Property == MapProperty || change.Property == SelectedTopicProperty || change.Property == SelectedRelationshipProperty)
        {
            AnnounceSelection();
        }

        if (change.Property == MapProperty)
        {
            Map? previous = change.GetOldValue<Map?>();
            Map? map = change.GetNewValue<Map?>();
            Relayout(map);

            // The zoom is one for all maps. Each map keeps its
            // scroll position while the document is open: switching tabs stores the view of the map
            // being left and brings back the one being shown at the current zoom, around the same middle; a map shown
            // for the first time is centred on its central topic. The first map of a document just opened is fitted,
            // which sets the zoom. An edit to the same map keeps the view as it is.
            if (map is not null && previous?.Id != map.Id)
            {
                bool zoomKnown = previous is not null && !_fitPending;
                double zoom = _viewport.Zoom;
                if (previous is not null)
                {
                    _views[previous.Id] = _viewport;
                }

                _animating = false;
                var middle = new Point(Bounds.Width / 2, Bounds.Height / 2);
                if (_views.TryGetValue(map.Id, out Viewport kept))
                {
                    _fitPending = false;
                    _viewport = zoomKnown ? kept.ZoomAround(middle, zoom) : kept;
                    SyncZoomProperty();
                }
                else if (zoomKnown && Bounds.Width > 0)
                {
                    // The map's origin is its central topic's centre: put it in the middle of the view.
                    _fitPending = false;
                    _viewport = new Viewport(zoom, middle);
                    SyncZoomProperty();
                }
                else
                {
                    _fitPending = true;
                    if (Bounds.Width > 0 && _layout is not null)
                    {
                        _fitPending = false;
                        _viewport = Viewport.Fit(_layout.Bounds, Bounds.Size);
                        SyncZoomProperty();
                    }
                }
            }

            InvalidateVisual();
        }
        else if (change.Property == ZoomProperty && !_settingZoom)
        {
            SetZoomAroundCentre(change.GetNewValue<double>(), animate: false);
        }
        else if (change.Property == SelectedRelationshipProperty || change.Property == LinkTargetProperty || change.Property == LinkSourceProperty
            || change.Property == SearchHitsProperty)
        {
            if (change.Property == LinkTargetProperty && change.GetNewValue<TopicId?>() is { } target)
            {
                EnsureVisible(target);
            }

            InvalidateVisual();
        }
        else if (change.Property == SelectedTopicsProperty)
        {
            _selectedSet = [.. change.GetNewValue<IReadOnlyList<TopicId>>() ?? []];
            InvalidateVisual();
        }
        else if (change.Property == SelectedTopicProperty)
        {
            if (change.GetNewValue<TopicId?>() is { } selected)
            {
                EnsureVisible(selected);
            }

            InvalidateVisual();
        }
    }

    /// <summary>
    /// A canvas that renders a map for a PNG export: no fitting, no selection, and instead of the
    /// window's background either <see cref="ExportBackground"/> or nothing (transparent).
    /// </summary>
    internal bool IsExport { get; init; }

    internal IBrush? ExportBackground { get; init; }

    /// <summary>Everything the map draws: its topics and its relationship curves, which can bow out beyond
    /// them.</summary>
    internal LayoutRect? ContentBounds
    {
        get
        {
            if (_layout is null)
            {
                return null;
            }

            LayoutRect bounds = _layout.Bounds;
            foreach (RelationshipCurve curve in Curves)
            {
                LayoutRect c = curve.Bounds;
                double x = Math.Min(bounds.X, c.X), y = Math.Min(bounds.Y, c.Y);
                bounds = new LayoutRect(x, y, Math.Max(bounds.Right, c.Right) - x, Math.Max(bounds.Bottom, c.Bottom) - y);
            }

            return bounds;
        }
    }

    /// <summary>Shows exactly this view, without fitting or animation: for an export.</summary>
    internal void ShowExactly(Viewport viewport)
    {
        _fitPending = false;
        _animating = false;
        _viewport = viewport;
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (IsExport)
        {
            return;
        }

        if (_fitPending && _layout is not null && e.NewSize.Width > 0)
        {
            _fitPending = false;
            Apply(Viewport.Fit(_layout.Bounds, e.NewSize));
        }
        else if (e.NewSize.Width < e.PreviousSize.Width && SelectedTopic is { } selected)
        {
            // A panel opening beside the map narrows it: the selection stays in view.
            EnsureVisible(selected);
        }
    }

    public override void Render(DrawingContext context)
    {
        long started = Stopwatch.GetTimestamp();
        RelationshipsDrawn = 0;
        var size = Bounds.Size;

        if (!IsExport)
        {
            context.DrawRectangle(Brush("CanvasBackgroundBrush"), null, new Rect(size));
        }
        else if (ExportBackground is { } background)
        {
            context.DrawRectangle(background, null, new Rect(size));
        }

        if (_layout is null || _grid is null)
        {
            return;
        }

        if (_fitPending && size.Width > 0 && !IsExport)
        {
            _fitPending = false;
            _viewport = Viewport.Fit(_layout.Bounds, size);
            SyncZoomProperty();
        }

        _text.BeginFrame();
        var palette = new Palette(this);
        _text.CentralForeground.Color = palette.CentralText;
        _text.MainForeground.Color = palette.MainText;
        _text.SubForeground.Color = palette.SubText;

        LayoutRect visible = _viewport.Visible(size).Inflate(16);
        List<int> topics = _grid.TopicsIn(visible);
        LastVisibleTopics = topics.Count;

        using (context.PushTransform(_viewport.Transform))
        {
            var connectorPen = new Pen(palette.Connector, palette.ConnectorThickness, lineCap: PenLineCap.Round);
            bool drawText = _viewport.Zoom >= TextZoomThreshold;

            if (TransitionProgress() is { } progress)
            {
                DrawTransition(context, palette, connectorPen, drawText, progress);
            }
            else
            {
                // Relationships first, so they stay behind the tree's connectors and topics.
                DrawRelationships(context, palette, visible);

                List<int> connectors = _grid.ConnectorsIn(visible);
                if (drawText)
                {
                    // The lines' subtle drop shadow: the same geometry, a little lower and wider, under the line.
                    var shadowPen = new Pen(palette.LineShadow, palette.ConnectorThickness + 1, lineCap: PenLineCap.Round);
                    using (context.PushTransform(Matrix.CreateTranslation(0, LineShadowOffset)))
                    {
                        foreach (int index in connectors)
                        {
                            context.DrawGeometry(null, shadowPen, ConnectorGeometry(index));
                        }
                    }
                }

                foreach (int index in connectors)
                {
                    context.DrawGeometry(null, connectorPen, ConnectorGeometry(index));
                }

                // Search hits get a highlighter mark behind the topic, so the topic's own colours stay as they are.
                if (SearchHits is { Count: > 0 } hits)
                {
                    foreach (int index in topics)
                    {
                        TopicBox box = _layout.Boxes[index];
                        if (hits.Contains(box.Id))
                        {
                            var mark = new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height).Inflate(6);
                            context.DrawRectangle(palette.SearchHit, null, new RoundedRect(mark, 12));
                        }
                    }
                }

                foreach (int index in topics)
                {
                    DrawTopic(context, _layout.Boxes[index], palette, drawText);
                }

                DrawBendHandle(context, palette);
                DrawLinkMode(context, palette);
            }

            DrawDrag(context, palette, drawText);
        }

        Stats.AddRender(Stopwatch.GetElapsedTime(started));

        if (ShowStats)
        {
            DrawStats(context, palette);
        }
    }

    private void DrawTopic(DrawingContext context, TopicBox box, Palette palette, bool drawText)
    {
        (IBrush fill, IBrush stroke, double radius, BoxShadows shadow) = box.Level switch
        {
            TopicLevel.Central => (palette.CentralFill, palette.CentralStroke, 10.0, palette.CentralShadow),
            TopicLevel.Main => (palette.MainFill, palette.MainStroke, 8.0, palette.MainShadow),
            _ => (palette.SubFill, palette.SubStroke, 6.0, palette.SubShadow),
        };

        // Shadows only where they can be seen: below the text threshold they cost frames on big maps for nothing.
        if (!drawText)
        {
            shadow = default;
        }

        var rect = new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height);
        bool selected = box.Id == SelectedTopic || _selectedSet.Contains(box.Id);
        bool hovered = box.Id == _hover;

        IPen pen = selected
            ? new Pen(palette.Selection, palette.SelectionThickness)
            : new Pen(stroke, hovered ? 2.25 : 1.5);

        context.DrawRectangle(fill, pen, new RoundedRect(rect, radius), selected ? palette.SelectionGlow : shadow);

        LayoutMetrics metrics = LayoutMetrics.Default;
        DrawMarkers(context, box, palette);
        DrawIndicators(context, box, palette);
        DrawTopicIcon(context, box, palette);

        if (drawText)
        {
            double wrapWidth = metrics.TextWidthOf(box.Level);
            TextLayout text = _text.Get(box.Topic.Text, box.Level, wrapWidth);

            // A centred layout centres every line within the full wrap width, not within the text's own width
            // (observed with Avalonia 12.1.6), so the wrap width is what gets centred on the text rectangle.
            text.Draw(context, new Point(box.Text.CenterX - (wrapWidth / 2), box.Text.Y));
        }
        else
        {
            // Level of detail: a soft bar where the text would be.
            double barHeight = Math.Min(box.Text.Height * 0.55, 10);
            var bar = new Rect(box.Text.X, box.Text.CenterY - (barHeight / 2), box.Text.Width, barHeight);
            context.DrawRectangle(palette.TextBarOf(box.Level), null, new RoundedRect(bar, barHeight / 2));
        }

        if (box.HiddenChildren > 0)
        {
            DrawCollapseHandle(context, box, stroke, palette);
        }
        else if (box.Topic.HasChildren && box.Parent is not null && (selected || hovered))
        {
            DrawExpandedHandle(context, box, stroke, palette);
        }
    }

    /// <summary>The collapse handle with the number of hidden subtopics, at the topic's outer edge.</summary>
    private void DrawCollapseHandle(DrawingContext context, TopicBox box, IBrush stroke, Palette palette)
    {
        Point centre = HandleCentre(box);

        context.DrawEllipse(palette.Surface, new Pen(stroke, 1.25), centre, HandleRadius, HandleRadius);

        if (!_countText.TryGetValue(box.HiddenChildren, out TextLayout? count))
        {
            count = new TextLayout(
                box.HiddenChildren.ToString(CultureInfo.CurrentCulture),
                new Typeface(new FontFamily("fonts:Inter#Inter"), FontStyle.Normal, FontWeight.SemiBold),
                9,
                _text.SubForeground);
            _countText[box.HiddenChildren] = count;
        }

        count.Draw(context, new Point(centre.X - (count.WidthIncludingTrailingWhitespace / 2), centre.Y - (count.Height / 2)));
    }

    private StreamGeometry ConnectorGeometry(int index)
    {
        if (_connectorGeometry.TryGetValue(index, out StreamGeometry? cached))
        {
            return cached;
        }

        Connector connector = _layout!.Connectors[index];
        var start = new Point(connector.Start.X, connector.Start.Y);
        var end = new Point(connector.End.X, connector.End.Y);
        double half = (end.X - start.X) / 2;

        var geometry = new StreamGeometry();
        using (StreamGeometryContext stream = geometry.Open())
        {
            stream.BeginFigure(start, isFilled: false);
            stream.CubicBezierTo(new Point(start.X + half, start.Y), new Point(end.X - half, end.Y), end);
            stream.EndFigure(isClosed: false);
        }

        _connectorGeometry[index] = geometry;
        return geometry;
    }

    private void DrawStats(DrawingContext context, Palette palette)
    {
        string text = string.Format(
            CultureInfo.InvariantCulture,
            "{0:0} fps  ·  frame {1:0.0} ms p95  ·  render {2:0.00} ms p95  ·  layout {3:0.0} ms  ·  {4} of {5} topics  ·  {6:0} %",
            Stats.Fps,
            Stats.IntervalP95,
            Stats.RenderP95,
            LastLayoutMs,
            LastVisibleTopics,
            _layout?.Boxes.Count ?? 0,
            _viewport.Zoom * 100);

        // Diagnostics: invariant and not in Strings.resx, like the exception reports.
        var layout = new TextLayout(text, new Typeface(new FontFamily("fonts:Inter#Inter")), 11, palette.SubTextBrush);
        var panel = new Rect(8, 8, layout.WidthIncludingTrailingWhitespace + 16, layout.Height + 8);
        context.DrawRectangle(palette.Surface, new Pen(palette.SubStroke, 1), new RoundedRect(panel, 6));
        layout.Draw(context, new Point(16, 12));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        CloseTip();
        base.OnPointerPressed(e);
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        Point position = e.GetPosition(this);

        if (LinkSource is not null)
        {
            // Link mode: a click on a topic finishes the link, one-way or two-way with Shift.
            if (properties.IsLeftButtonPressed && _grid?.TopicAt(_viewport.ToMap(position)) is { } target)
            {
                LinkPicked?.Invoke(this, (target.Id, e.KeyModifiers.HasFlag(KeyModifiers.Shift)));
                e.Handled = true;
            }

            return;
        }

        if (properties.IsLeftButtonPressed && _editing is null && BeginBend(position, e))
        {
            e.Handled = true;
            return;
        }

        if (properties.IsLeftButtonPressed && _editing is null && HandleAt(_viewport.ToMap(position)) is { } handle)
        {
            HandleClicked?.Invoke(this, handle);
            e.Handled = true;
            return;
        }

        TopicBox? hit = _grid?.TopicAt(_viewport.ToMap(position));

        // The note and link indicators inside a topic are buttons of their own.
        if (properties.IsLeftButtonPressed && _editing is null && hit is not null
            && TopicParts.IndicatorAt(hit, _viewport.ToMap(position)) is { } indicator)
        {
            Focus();
            bool direct = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            IndicatorClicked?.Invoke(this, new IndicatorClick(hit.Id, indicator, direct));
            e.Handled = true;
            return;
        }

        // ⌘-click (Ctrl-click elsewhere; on macOS Ctrl-click is the context click) adds or removes a topic.
        KeyModifiers toggleKey = Host.Current == HostPlatform.MacOS ? KeyModifiers.Meta : KeyModifiers.Control;
        if (properties.IsLeftButtonPressed && hit is not null && _editing is null && e.KeyModifiers.HasFlag(toggleKey))
        {
            Focus();
            TopicToggled?.Invoke(this, hit.Id);
            e.Handled = true;
            return;
        }

        // Right-click selects the topic it is on before the menu opens; on one of several selected
        // topics it keeps them, so the menu's Delete takes them all. A press on one of several may start dragging
        // them all; only a click without a drag makes it the one selected.
        if ((properties.IsLeftButtonPressed || properties.IsRightButtonPressed) && hit is not null)
        {
            Focus();
            if (_selectedSet.Count > 1 && _selectedSet.Contains(hit.Id))
            {
                _selectOnlyOnRelease = properties.IsLeftButtonPressed && e.ClickCount == 1 ? hit.Id : null;
            }
            else
            {
                SetCurrentValue(SelectedTopicProperty, hit.Id);
            }

            if (properties.IsLeftButtonPressed && e.ClickCount == 2)
            {
                TopicDoubleClicked?.Invoke(this, hit.Id);
            }
            else if (properties.IsLeftButtonPressed)
            {
                ArmDrag(hit, position, e);
            }

            e.Handled = properties.IsLeftButtonPressed;
            return;
        }

        // Not on a topic: perhaps on a relationship, which clicks and right-clicks select.
        if ((properties.IsLeftButtonPressed || properties.IsRightButtonPressed) && RelationshipAt(_viewport.ToMap(position)) is { } curve)
        {
            Focus();
            SetCurrentValue(SelectedRelationshipProperty, curve.Id);
            InvalidateVisual();
            e.Handled = properties.IsLeftButtonPressed;
            return;
        }

        if (properties.IsRightButtonPressed)
        {
            Focus();
            return;
        }

        // Dragging the empty canvas, or with the middle button anywhere, pans. A left click that does not move
        // clears the selection instead, see OnPointerReleased.
        if (properties.IsLeftButtonPressed || properties.IsMiddleButtonPressed)
        {
            Focus();
            _clearOnRelease = properties.IsLeftButtonPressed ? position : null;
            _panFrom = position;
            _animating = false;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point position = e.GetPosition(this);

        if (_panFrom is { } from)
        {
            if (_clearOnRelease is { } pressed && Point.Distance(pressed, position) > ClickSlop)
            {
                _clearOnRelease = null;
            }

            Apply(_viewport.PanBy(position - from));
            _panFrom = position;
            return;
        }

        if (MoveDrag(position, e) || MoveBend(position))
        {
            return;
        }

        // A topic counts as hovered while the pointer is on its collapse handle too, so the "−" handle does not
        // vanish on the way to it.
        LayoutPoint point = _viewport.ToMap(position);
        TopicId? hover = _grid?.TopicAt(point)?.Id ?? HandleOwnerAt(point);
        RelationshipId? hoverRelationship = hover is null ? RelationshipAt(point)?.Id : null;
        if (hoverRelationship != _hoverRelationship)
        {
            _hoverRelationship = hoverRelationship;
            InvalidateVisual();
        }
        if (hover != _hover)
        {
            _hover = hover;
            OnHoverChangedForTip();
            InvalidateVisual();
        }

        UpdateIndicatorCursor(point);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        TopicId? selectOnly = _selectOnlyOnRelease;
        _selectOnlyOnRelease = null;
        if (EndDrag(e) || EndBend(e))
        {
            return;
        }

        if (selectOnly is { } only)
        {
            SelectOnlyRequested?.Invoke(this, only);
        }

        if (_panFrom is not null)
        {
            _panFrom = null;
            e.Pointer.Capture(null);
        }

        if (_clearOnRelease is not null)
        {
            _clearOnRelease = null;
            SetCurrentValue(SelectedRelationshipProperty, null);
            SetCurrentValue(SelectedTopicProperty, null);
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        CloseTip();
        if (_hover is not null)
        {
            _hover = null;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// The wheel pans; with Ctrl (⌘ on macOS) it zooms around the pointer. A trackpad's two-finger scroll arrives
    /// as small wheel deltas on both axes, so it pans smoothly in any direction. Shift turns a mouse wheel
    /// sideways, as Windows users expect.
    /// </summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        CloseTip();
        _animating = false;

        // Only Ctrl/⌘ zooms. Zooming with the plain wheel needed telling a wheel from a trackpad by its values,
        // which broke on Linux and with smoothed mice on macOS.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            // A wheel notch steps on Viewport.WheelStep's grid, which stops on every quarter (100 %, 125 %…). On macOS
            // a notch arrives as 0.02 (see IsWheelNotch). A trackpad, or a notch not recognised, keeps fine, continuous
            // steps that still stop on each quarter they pass.
            bool notch = IsWheelNotch(e.Delta, Host.Current);
            double zoomTo = notch
                ? Viewport.WheelStep(_viewport.Zoom, (int)Math.Round(Host.Current == HostPlatform.MacOS ? e.Delta.Y * 50 : e.Delta.Y))
                : Viewport.StopAtQuarter(_viewport.Zoom, _viewport.Zoom * Math.Pow(1.1, e.Delta.Y));
            Apply(_viewport.ZoomAround(e.GetPosition(this), zoomTo));
        }
        else
        {
            const double Step = 48;
            Vector delta = e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Delta.X == 0
                ? new Vector(e.Delta.Y * Step, 0)
                : new Vector(e.Delta.X * Step, e.Delta.Y * Step);
            Apply(_viewport.PanBy(delta));
        }

        e.Handled = true;
    }

    /// <summary>
    /// True for a notched mouse wheel rather than a trackpad, from the delta alone (Avalonia 12.1.6 does not say which
    /// device sent it). Nothing sideways either way, and then per platform:
    /// <list type="bullet">
    /// <item>Windows and Linux: whole steps (±1, ±2). Precision touchpads send fractions.</item>
    /// <item>macOS: Avalonia divides both by 50. A trackpad's whole pixels become exact multiples of 0.02; a wheel
    /// notch
    /// arrives as ±1.00006103515625 lines, macOS's fixed-point value, so ×50 it is not a whole number. Observed with a
    /// Bluetooth mouse and the built-in trackpad: ±0.020001220703125 per notch against 0.02, 0.04,
    /// 0.06… from the trackpad.</item>
    /// </list>
    /// </summary>
    internal static bool IsWheelNotch(Vector delta, HostPlatform platform)
    {
        if (delta.X != 0 || delta.Y == 0)
        {
            return false;
        }

        double steps = platform == HostPlatform.MacOS ? delta.Y * 50 : delta.Y;
        bool whole = Math.Abs(steps - Math.Round(steps)) < 1e-9;
        return platform == HostPlatform.MacOS ? !whole : whole;
    }

    /// <summary>Trackpad pinch (macOS): zoom around the pointer.</summary>
    internal void OnMagnify(PointerDeltaEventArgs e)
    {
        _animating = false;
        Apply(_viewport.ZoomAround(e.GetPosition(this), _viewport.Zoom * (1 + e.Delta.X)));
        e.Handled = true;
    }

    /// <summary>Typing a printable character on a selected topic starts editing it.</summary>
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (LinkSource is not null && !string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]))
        {
            LinkTyped?.Invoke(this, e.Text);
            e.Handled = true;
            return;
        }

        // While the editor opens, the text input of the key that opened it (Space) still arrives here: ignore it.
        // A selected relationship takes letters as commands (B, R), never as text. A space never replaces a topic's
        // text with a blank, whatever modifier came with it.
        if (_editing is not null || SelectedRelationship is not null || SelectedTopic is null
            || string.IsNullOrWhiteSpace(e.Text) || char.IsControl(e.Text[0]))
        {
            return;
        }

        TextTyped?.Invoke(this, e.Text);
        e.Handled = true;
    }

    // --- in-place editor ---------------------------------------------------------------------------------------------

    /// <summary>Shows the editor over <paramref name="topic"/>, with <paramref name="text"/> and the caret where
    /// asked.</summary>
    public void OpenEditor(TopicId topic, string text, EditStart start)
    {
        CloseTip();
        _editing = topic;
        _closingEditor = true;
        _editor.Text = text;
        _titleGuard.Reset(text);
        _formatter.Reset();
        _closingEditor = false;
        StyleEditor();
        _editor.IsVisible = true;
        _formatBar.IsVisible = true;
        InvalidateMeasure();

        // Focus after the current input has been delivered, so the key that opened the editor does not land in it.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_editing != topic)
                {
                    return;
                }

                _editor.Focus();
                _editor.CaretIndex = _editor.Text?.Length ?? 0;
                _editor.ClearSelection();
            },
            DispatcherPriority.Background);
    }

    /// <summary>Hides the editor without raising anything: the view model has already ended the session.</summary>
    public void HideEditor()
    {
        if (_editing is null)
        {
            return;
        }

        _closingEditor = true;
        _editing = null;
        bool hadFocus = _editor.IsFocused;
        _editor.IsVisible = false;
        _formatBar.IsVisible = false;
        _closingEditor = false;
        if (hadFocus)
        {
            Focus();
        }

        InvalidateVisual();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseEditor(commit: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            // Enter commits; Shift+Enter falls through and the text box inserts a line break.
            CloseEditor(commit: true);
            e.Handled = true;
        }
    }

    private void CloseEditor(bool commit)
    {
        if (_editing is null || _closingEditor)
        {
            return;
        }

        _closingEditor = true;
        string text = _editor.Text ?? string.Empty;
        if (commit)
        {
            EditCommitted?.Invoke(this, text);
        }
        else
        {
            EditCancelled?.Invoke(this, EventArgs.Empty);
        }

        _closingEditor = false;
    }

    /// <summary>The editor takes the topic's level styling, scaled by the zoom, so editing looks like the
    /// topic.</summary>
    private void StyleEditor()
    {
        if (_editing is not { } id || _layout?.Find(id) is not { } box)
        {
            return;
        }

        double zoom = _viewport.Zoom;
        LayoutSize padding = LayoutMetrics.Default.PaddingOf(box.Level);
        var palette = new Palette(this);

        _editor.FontSize = TopicText.FontSize(box.Level) * zoom;
        _editor.FontWeight = TopicText.Weight(box.Level);
        _editor.Padding = new Thickness(padding.Width * zoom, padding.Height * zoom);
        _editor.CornerRadius = new CornerRadius((box.Level switch { TopicLevel.Central => 10, TopicLevel.Main => 8, _ => 6 }) * zoom);
        (_editor.Background, _editor.Foreground) = box.Level switch
        {
            TopicLevel.Central => (palette.CentralFill, (IBrush)new ImmutableSolidColorBrush(palette.CentralText)),
            TopicLevel.Main => (palette.MainFill, new ImmutableSolidColorBrush(palette.MainText)),
            _ => (palette.SubFill, new ImmutableSolidColorBrush(palette.SubText)),
        };
        _editor.BorderBrush = palette.Selection;
    }

    /// <summary>
    /// The editor's rectangle on screen. The map is laid out again only when the edit ends, so the
    /// editor grows over its neighbours instead: as wide as the typed text needs, up to the level's wrap width, so it
    /// breaks lines where the map will, and as tall as its lines. It grows away from the parent (to the right on the
    /// right side, to the left on the left side, both ways on the central topic) and both ways vertically, and is
    /// never smaller than the topic.
    /// </summary>
    private Rect? EditorBounds()
    {
        if (_editing is not { } id || _layout?.Find(id) is not { } box)
        {
            return null;
        }

        double zoom = _viewport.Zoom;
        LayoutSize padding = LayoutMetrics.Default.PaddingOf(box.Level);
        double wrap = LayoutMetrics.Default.TextWidthOf(box.Level);
        double typed = Math.Min(wrap, TopicText.MeasureAsTyped(_editor.Text ?? string.Empty, box.Level, wrap).Width);
        double border = _editor.BorderThickness.Left + _editor.BorderThickness.Right;
        double width = Math.Max(box.Bounds.Width * zoom, ((typed + (2 * padding.Width)) * zoom) + border + EditorCaretRoom);

        Point topLeft = _viewport.ToScreen(new LayoutPoint(box.Bounds.X, box.Bounds.Y));
        double grow = width - (box.Bounds.Width * zoom);
        double x = box.Side switch
        {
            Side.Left => topLeft.X - grow,
            Side.Center => topLeft.X - (grow / 2),
            _ => topLeft.X,
        };
        return new Rect(x, topLeft.Y, width, box.Bounds.Height * zoom);
    }

    /// <summary>Room for the caret at the end of a line, so typing never wraps a line the map would not wrap.</summary>
    private const double EditorCaretRoom = 2;

    protected override Size MeasureOverride(Size availableSize)
    {
        // The base measures every child at the canvas's whole width, where the typed text fits on one line; the
        // editor is measured again afterwards at its own width, or its height would leave out the wrapped lines.
        Size size = base.MeasureOverride(availableSize);
        if (EditorBounds() is { } bounds)
        {
            _editor.Measure(new Size(bounds.Width, double.PositiveInfinity));
        }

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (EditorBounds() is { } bounds)
        {
            // At least as tall as the topic; taller as the typed text takes more lines.
            double height = Math.Max(bounds.Height, _editor.DesiredSize.Height);
            var editor = new Rect(bounds.X, bounds.Y - ((height - bounds.Height) / 2), bounds.Width, height);
            _editor.Arrange(editor);
            ArrangeFormatBar(editor);
        }

        ArrangeTipAnchor();
        return finalSize;
    }

    // --- context menus -----------------------------------------------------------------------------------------------

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_editing is not null)
        {
            return;
        }

        TopicBox? box;
        if (e.TryGetPosition(this, out Point position))
        {
            box = _grid?.TopicAt(_viewport.ToMap(position));
            if (box is null && RelationshipAt(_viewport.ToMap(position)) is not null && OpenMenu(RelationshipMenu, position))
            {
                e.Handled = true;
                return;
            }
        }
        else if (SelectedRelationship is { } selectedRelationship && _curves.Find(c => c.Id == selectedRelationship) is { } selectedCurve)
        {
            LayoutPoint middle = selectedCurve.At(0.5);
            if (OpenMenu(RelationshipMenu, _viewport.ToScreen(middle)))
            {
                e.Handled = true;
                return;
            }

            box = null;
        }
        else
        {
            // Shift+F10 or the Menu key: the menu for the selection, placed at the selected topic.
            box = SelectedTopic is { } selected ? _layout?.Find(selected) : null;
            if (box is not null)
            {
                Point corner = _viewport.ToScreen(new LayoutPoint(box.Bounds.X, box.Bounds.Bottom));
                position = corner;
            }
        }

        if (OpenMenu(box is not null ? TopicMenu : CanvasMenu, position))
        {
            e.Handled = true;
        }
    }

    private bool OpenMenu(IReadOnlyList<MenuEntry>? entries, Point position)
    {
        if (entries is null || entries.Count == 0)
        {
            return false;
        }

        var menu = new ContextMenu
        {
            ItemsSource = BuildMenuItems(entries),
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = new Rect(position, new Size(1, 1)),
            PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft,
            PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight,
        };
        menu.Closed += (_, _) => Focus();
        menu.Open(this);
        OpenedMenu = menu;
        return true;
    }

    /// <summary>The context menu opened last, for tests: it lives in a popup, outside the window's visual
    /// tree.</summary>
    internal ContextMenu? OpenedMenu { get; private set; }

    internal static List<Control> BuildMenuItems(IEnumerable<MenuEntry> entries) =>
    [
        .. entries.Select(entry => entry.IsSeparator
            ? (Control)new Separator()
            : new MenuItem
            {
                Header = entry.Header,
                Command = entry.Command,
                InputGesture = entry.Gesture,
                Icon = entry.Swatch is { } swatch ? Views.MenuSwatches.Create(swatch) : null,
                ItemsSource = entry.HasChildren ? BuildMenuItems(entry.Children!) : null,
            }),
    ];

    /// <summary>
    /// Scrolls, animated, so <paramref name="topic"/> is fully visible with a margin (the selection
    /// always stays visible).
    /// </summary>
    private void EnsureVisible(TopicId topic)
    {
        if (_layout?.Find(topic) is not { } box || Bounds.Width <= 0)
        {
            return;
        }

        Point topLeft = _viewport.ToScreen(new LayoutPoint(box.Bounds.X, box.Bounds.Y));
        Point bottomRight = _viewport.ToScreen(new LayoutPoint(box.Bounds.Right, box.Bounds.Bottom));

        double dx = topLeft.X < VisibleMargin ? VisibleMargin - topLeft.X
            : bottomRight.X > Bounds.Width - VisibleMargin ? Bounds.Width - VisibleMargin - bottomRight.X
            : 0;
        double dy = topLeft.Y < VisibleMargin ? VisibleMargin - topLeft.Y
            : bottomRight.Y > Bounds.Height - VisibleMargin ? Bounds.Height - VisibleMargin - bottomRight.Y
            : 0;

        if (dx != 0 || dy != 0)
        {
            SetViewport(_viewport.PanBy(new Vector(dx, dy)), animate: true);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AddHandler(PointerTouchPadGestureMagnifyEvent, OnMagnifyEvent);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        RemoveHandler(PointerTouchPadGestureMagnifyEvent, OnMagnifyEvent);
        _text.Release();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnMagnifyEvent(object? sender, PointerDeltaEventArgs e) => OnMagnify(e);

    private void Relayout(Map? map)
    {
        _connectorGeometry.Clear();
        if (map is null)
        {
            _layout = null;
            _grid = null;
            return;
        }

        long started = Stopwatch.GetTimestamp();
        BeginTransitionIfRequested(map);
        _layout = _engine.Layout(map);
        _grid = new SpatialGrid(_layout);
        LayoutRelationships(map);
        LastLayoutMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (_editing is not null)
        {
            StyleEditor();
            InvalidateMeasure();
        }
    }

    private void SetZoomAroundCentre(double zoom, bool animate)
    {
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        SetViewport(_viewport.ZoomAround(centre, zoom), animate);
    }

    /// <summary>
    /// No endless canvas: the view may move only until the map has just left it, so the map is
    /// never more than a nudge away. Measured on everything the map draws, its relationship curves included.
    /// </summary>
    internal Viewport KeepInReach(Viewport viewport)
    {
        if (IsExport || ContentBounds is not { } content || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return viewport;
        }

        double left = (content.X * viewport.Zoom) + viewport.Origin.X;
        double right = (content.Right * viewport.Zoom) + viewport.Origin.X;
        double top = (content.Y * viewport.Zoom) + viewport.Origin.Y;
        double bottom = (content.Bottom * viewport.Zoom) + viewport.Origin.Y;

        double dx = right < 0 ? -right : left > Bounds.Width ? Bounds.Width - left : 0;
        double dy = bottom < 0 ? -bottom : top > Bounds.Height ? Bounds.Height - top : 0;
        return dx == 0 && dy == 0 ? viewport : viewport.PanBy(new Vector(dx, dy));
    }

    private void Apply(Viewport viewport)
    {
        viewport = KeepInReach(viewport);
        bool zoomChanged = viewport.Zoom != _viewport.Zoom;
        _viewport = viewport;
        SyncZoomProperty();
        InvalidateVisual();

        if (_editing is not null)
        {
            if (zoomChanged)
            {
                StyleEditor();
            }

            InvalidateMeasure();
        }
    }

    private void SyncZoomProperty()
    {
        _settingZoom = true;
        SetCurrentValue(ZoomProperty, _viewport.Zoom);
        _settingZoom = false;
    }

    private void Animate(TimeSpan now)
    {
        if (!_animating)
        {
            return;
        }

        _animationStart ??= now;
        double t = Math.Clamp((now - _animationStart.Value) / AnimationTime, 0, 1);
        double eased = 1 - Math.Pow(1 - t, 3);

        Apply(new Viewport(
            _animationFrom.Zoom + ((_animationTo.Zoom - _animationFrom.Zoom) * eased),
            _animationFrom.Origin + ((_animationTo.Origin - _animationFrom.Origin) * eased)));

        if (t < 1)
        {
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Animate);
        }
        else
        {
            _animating = false;
        }
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush ? brush : Brushes.Transparent;

    /// <summary>Every token one frame needs, resolved once per frame from the current theme.</summary>
    private readonly struct Palette
    {
        public Palette(MapCanvas canvas)
        {
            CentralFill = canvas.Brush("TopicCentralFillBrush");
            CentralStroke = canvas.Brush("TopicCentralStrokeBrush");
            MainFill = canvas.Brush("TopicMainFillBrush");
            MainStroke = canvas.Brush("TopicMainStrokeBrush");
            SubFill = canvas.Brush("TopicSubFillBrush");
            SubStroke = canvas.Brush("TopicSubStrokeBrush");
            Connector = canvas.Brush("TopicConnectorBrush");
            Selection = canvas.Brush("TopicSelectionBrush");
            Surface = canvas.Brush("AppSurfaceBrush");
            SubTextBrush = canvas.Brush("TopicSubTextBrush");

            CentralText = ColorOf(canvas.Brush("TopicCentralTextBrush"));
            MainText = ColorOf(canvas.Brush("TopicMainTextBrush"));
            SubText = ColorOf(SubTextBrush);

            Priority = [.. Enumerable.Range(1, 5).Select(n => canvas.Brush($"MarkerPriority{n}Brush"))];
            OnFill = canvas.Brush("MarkerOnFillBrush");
            Progress = canvas.Brush("MarkerProgressBrush");
            ProgressTrack = canvas.Brush("MarkerProgressTrackBrush");
            IBrush glyph = canvas.Brush("TopicIconGlyphBrush"), darkGlyph = canvas.Brush("TopicIconGlyphDarkBrush"), ring = canvas.Brush("TopicIconRingBrush");
            IconRing = ring;
            Icons = TopicIcons.Values.ToDictionary(value => value, value => new TopicIcons.Brushes(canvas.Brush(TopicIcons.BrushKey(value)), glyph, darkGlyph, ring));
            Indicator = canvas.Brush("TopicIndicatorBrush");
            _centralBar = new ImmutableSolidColorBrush(CentralText, 0.35);
            _mainBar = new ImmutableSolidColorBrush(MainText, 0.35);
            _subBar = new ImmutableSolidColorBrush(SubText, 0.35);

            CentralShadow = Shadows(canvas, "TopicCentralShadow");
            MainShadow = Shadows(canvas, "TopicMainShadow");
            SubShadow = Shadows(canvas, "TopicSubShadow");
            LineShadow = canvas.Brush("CanvasLineShadowBrush");
            SelectionGlow = Shadows(canvas, "TopicSelectionGlow");
            ConnectorThickness = Number(canvas, "TopicConnectorThickness", 1.5);
            SelectionThickness = Number(canvas, "TopicSelectionThickness", 3);
            Relationship = canvas.Brush("TopicRelationshipBrush");
            RelationshipColors = [Relationship, .. Enum.GetValues<RelationshipColor>().Skip(1).Select(color => canvas.Brush($"Relationship{color}Brush"))];
            LinkTarget = canvas.Brush("TopicLinkTargetBrush");
            SearchHit = canvas.Brush("CanvasSearchHitBrush");
            RelationshipThickness = Number(canvas, "TopicRelationshipThickness", 1.5);
            SelectionHalo = new ImmutableSolidColorBrush(ColorOf(Selection), 0.25);
            FadedRelationshipColors = [.. RelationshipColors.Select(brush => new ImmutableSolidColorBrush(ColorOf(brush), HiddenEndOpacity))];
            FadedLineShadow = new ImmutableSolidColorBrush(ColorOf(LineShadow), HiddenEndOpacity);
        }

        public IBrush CentralFill { get; }

        public IBrush CentralStroke { get; }

        public IBrush MainFill { get; }

        public IBrush MainStroke { get; }

        public IBrush SubFill { get; }

        public IBrush SubStroke { get; }

        public IBrush Connector { get; }

        public IBrush Selection { get; }

        public IBrush Surface { get; }

        public IBrush SubTextBrush { get; }

        /// <summary>Priority 1 to 5, at index 0 to 4.</summary>
        public IBrush[] Priority { get; }

        public IBrush OnFill { get; }

        public IBrush Progress { get; }

        public IBrush ProgressTrack { get; }

        /// <summary>The ring that sets off what hovers on a topic's border from that border.</summary>
        public IBrush IconRing { get; }

        /// <summary>The topic icons' brushes, by icon value (TopicIcons).</summary>
        public Dictionary<string, TopicIcons.Brushes> Icons { get; }

        public IBrush Indicator { get; }

        public Color CentralText { get; }

        public Color MainText { get; }

        public Color SubText { get; }

        public BoxShadows CentralShadow { get; }

        public BoxShadows MainShadow { get; }

        public BoxShadows SubShadow { get; }

        /// <summary>The colour of the soft shadow under connectors and relationships.</summary>
        public IBrush LineShadow { get; }

        public BoxShadows SelectionGlow { get; }

        public double ConnectorThickness { get; }

        public double SelectionThickness { get; }

        public IBrush Relationship { get; }

        /// <summary>A relationship's brush per <see cref="RelationshipColor"/>, the default first.</summary>
        public IBrush[] RelationshipColors { get; }

        /// <summary>
        /// The same brushes, faded, for a relationship with a hidden end. Faded brushes rather than an opacity layer: a
        /// layer per curve, as large as the curve, cost a 5,000-topic map half its frame rate at 100 % (measured).
        /// </summary>
        public IBrush[] FadedRelationshipColors { get; }

        /// <summary>The line shadow, faded the same way.</summary>
        public IBrush FadedLineShadow { get; }

        public IBrush LinkTarget { get; }

        public IBrush SearchHit { get; }

        public double RelationshipThickness { get; }

        public IBrush SelectionHalo { get; }

        private readonly IBrush _centralBar;
        private readonly IBrush _mainBar;
        private readonly IBrush _subBar;

        public IBrush TextBarOf(TopicLevel level) => level switch
        {
            TopicLevel.Central => _centralBar,
            TopicLevel.Main => _mainBar,
            _ => _subBar,
        };

        private static Color ColorOf(IBrush brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Gray;

        private static double Number(MapCanvas canvas, string key, double fallback) =>
            canvas.TryFindResource(key, canvas.ActualThemeVariant, out object? value) && value is double number ? number : fallback;

        private static BoxShadows Shadows(MapCanvas canvas, string key) =>
            canvas.TryFindResource(key, canvas.ActualThemeVariant, out object? value) && value is BoxShadows shadows ? shadows : default;
    }
}
