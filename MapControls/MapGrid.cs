using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MapControls;

public enum OpenLoopMark { None = 0, Box = 1, Corner = 2 }

/// <summary>
/// The map table, replacing MapViewerEx's DevExpress grid: X axis values as column headers, Y axis values as row headers,
/// cells coloured green→yellow→red over the map's range like txlogger (T7Suite's red-white variant and online tint kept), open-loop marks,
/// T5Suite's padlocks and adaption / sample boxes (<see cref="MapData.Marks"/>) and the yellow live cell. Behaves like the DevExpress grid T7Suite used: click/drag selects a block, Shift extends, Ctrl
/// toggles, arrows move (Shift extends); typing, F2 or Enter edits the focused cell (Easy view starts from the value with two
/// decimals) and Enter commits it; +/- step by one, PgUp/PgDn by ten (0x10 in hex), Home sets max, End zero, on every
/// selected cell. Ctrl+C copies the shown text tab separated like DevExpress did; the context menu has T7Suite's copy/paste
/// (its own clipboard format) and smoothing. Ctrl+Z/Y undo and redo, which T7Suite didn't have.
/// </summary>
public class MapGrid : Control
{
    public static readonly StyledProperty<MapData?> MapProperty = AvaloniaProperty.Register<MapGrid, MapData?>(nameof(Map));
    public static readonly StyledProperty<MapViewType> ViewTypeProperty = AvaloniaProperty.Register<MapGrid, MapViewType>(nameof(ViewType), MapViewType.Easy);
    public static readonly StyledProperty<bool> IsRedWhiteProperty = AvaloniaProperty.Register<MapGrid, bool>(nameof(IsRedWhite));
    public static readonly StyledProperty<bool> OnlineModeProperty = AvaloniaProperty.Register<MapGrid, bool>(nameof(OnlineMode));
    public static readonly StyledProperty<bool> DisableColorsProperty = AvaloniaProperty.Register<MapGrid, bool>(nameof(DisableColors));
    public static readonly StyledProperty<ColorBlindMode> ColorModeProperty = AvaloniaProperty.Register<MapGrid, ColorBlindMode>(nameof(ColorMode));
    public static readonly StyledProperty<OpenLoopMark> OpenLoopMarkProperty = AvaloniaProperty.Register<MapGrid, OpenLoopMark>(nameof(OpenLoopMark));
    public static readonly StyledProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<MapGrid, bool>(nameof(IsReadOnly));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<MapGrid>();
    public static readonly StyledProperty<double> FontSizeProperty = TextElement.FontSizeProperty.AddOwner<MapGrid>();
    public static readonly StyledProperty<FontFamily> FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner<MapGrid>();
    public static readonly StyledProperty<IBrush?> HeaderBackgroundProperty = AvaloniaProperty.Register<MapGrid, IBrush?>(nameof(HeaderBackground));
    public static readonly StyledProperty<IBrush?> HeaderForegroundProperty = AvaloniaProperty.Register<MapGrid, IBrush?>(nameof(HeaderForeground));
    public static readonly StyledProperty<IBrush?> HeaderLinesProperty = AvaloniaProperty.Register<MapGrid, IBrush?>(nameof(HeaderLines));
    public static readonly StyledProperty<IBrush?> GridLinesProperty = AvaloniaProperty.Register<MapGrid, IBrush?>(nameof(GridLines));
    public static readonly StyledProperty<double> CellGapProperty = AvaloniaProperty.Register<MapGrid, double>(nameof(CellGap));

    public MapData? Map { get => GetValue(MapProperty); set => SetValue(MapProperty, value); }
    public MapViewType ViewType { get => GetValue(ViewTypeProperty); set => SetValue(ViewTypeProperty, value); }
    public bool IsRedWhite { get => GetValue(IsRedWhiteProperty); set => SetValue(IsRedWhiteProperty, value); }
    public bool OnlineMode { get => GetValue(OnlineModeProperty); set => SetValue(OnlineModeProperty, value); }
    public bool DisableColors { get => GetValue(DisableColorsProperty); set => SetValue(DisableColorsProperty, value); }
    /// <summary>The value scale's colour blind mode (not the red-white or online tints, which are T7Suite's).</summary>
    public ColorBlindMode ColorMode { get => GetValue(ColorModeProperty); set => SetValue(ColorModeProperty, value); }
    public OpenLoopMark OpenLoopMark { get => GetValue(OpenLoopMarkProperty); set => SetValue(OpenLoopMarkProperty, value); }
    public bool IsReadOnly { get => GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    /// <summary>Header fill, text and cell lines, and the lines between the cells; unset they're shades of the foreground and the headers have no lines.</summary>
    public IBrush? HeaderBackground { get => GetValue(HeaderBackgroundProperty); set => SetValue(HeaderBackgroundProperty, value); }
    public IBrush? HeaderForeground { get => GetValue(HeaderForegroundProperty); set => SetValue(HeaderForegroundProperty, value); }
    public IBrush? HeaderLines { get => GetValue(HeaderLinesProperty); set => SetValue(HeaderLinesProperty, value); }
    public IBrush? GridLines { get => GetValue(GridLinesProperty); set => SetValue(GridLinesProperty, value); }
    /// <summary>Space between the cells, which become tiles without the lines between them; 0 is the DevExpress grid's look.</summary>
    public double CellGap { get => GetValue(CellGapProperty); set => SetValue(CellGapProperty, value); }

    /// <summary>The selection changed (data indices in <see cref="SelectedCells"/>).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>A typed value was rejected, with the reason.</summary>
    public event EventHandler<string>? EditRejected;

    private static readonly IBrush Highlight = new ImmutableSolidColorBrush(Colors.Yellow);
    private static readonly IBrush SelectionFill = new ImmutableSolidColorBrush(Color.FromArgb(90, 0x33, 0x66, 0xFF));
    private static readonly IBrush SelectionLine = new ImmutableSolidColorBrush(Color.FromRgb(0x33, 0x66, 0xFF));
    private static readonly IBrush FocusLine = new ImmutableSolidColorBrush(Color.FromRgb(0x10, 0x30, 0xC0));
    private static readonly IBrush OpenLoopCorner = new ImmutableSolidColorBrush(Colors.SeaGreen);
    private static readonly Lazy<Bitmap> Padlock = new(() => new Bitmap(AssetLoader.Open(new Uri("avares://MapControls/Assets/lock.png"))));

    private readonly HashSet<int> m_selected = new();
    private int m_anchorRow, m_anchorCol, m_focusRow, m_focusCol;
    private bool m_dragging;
    private string m_input = "";
    private bool m_editing, m_suppressText;
    private int m_highlight = -1;
    private double m_headerW, m_headerH, m_cellW, m_cellH;

    static MapGrid()
    {
        FocusableProperty.OverrideDefaultValue<MapGrid>(true);
        AffectsRender<MapGrid>(ViewTypeProperty, IsRedWhiteProperty, OnlineModeProperty, DisableColorsProperty, ColorModeProperty, OpenLoopMarkProperty, ForegroundProperty,
            HeaderBackgroundProperty, HeaderForegroundProperty, HeaderLinesProperty, GridLinesProperty, CellGapProperty);
        AffectsMeasure<MapGrid>(MapProperty, ViewTypeProperty, FontSizeProperty, FontFamilyProperty, CellGapProperty);
    }

    public MapGrid()
    {
        ContextMenu = BuildContextMenu();
    }

    public IReadOnlyCollection<int> SelectedCells => m_selected;

    public void Select(IEnumerable<int> cells)
    {
        m_selected.Clear();
        foreach (int i in cells) m_selected.Add(i);
        if (Map != null && m_selected.Count > 0) (m_anchorRow, m_anchorCol) = (m_focusRow, m_focusCol) = Map.Cell(m_selected.First());
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>The live cell from logging (T7Suite's HighlightCell): column and data row index, -1 clears.</summary>
    public void SetHighlight(int col, int dataRow)
    {
        MapData? map = Map;
        int i = map == null || col < 0 || dataRow < 0 ? -1 : Math.Min(dataRow, map.Rows - 1) * map.Cols + Math.Min(col, map.Cols - 1);
        if (map != null && i >= map.Count) i = -1;
        if (i == m_highlight) return;
        m_highlight = i;
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MapProperty)
        {
            if (change.OldValue is MapData old) old.Changed -= OnMapChanged;
            if (change.NewValue is MapData map) map.Changed += OnMapChanged;
            m_selected.Clear();
            m_input = "";
            m_editing = false;
            m_highlight = -1;
            m_anchorRow = m_anchorCol = m_focusRow = m_focusCol = 0;
        }
    }

    private void OnMapChanged(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    private Typeface Typeface => new(FontFamily);

    private FormattedText Text(string s, IBrush brush) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface, FontSize * m_textScale, brush);

    // narrow columns get smaller text, as T7Suite's Small / ExtraSmall views did, down to 60 % of the font; below that it clips
    private double m_textScale = 1;

    private string XLabel(int col)
    {
        MapData map = Map!;
        if (map.XAxis == null || col >= map.XAxis.Length) return col.ToString(CultureInfo.CurrentCulture);
        double x = map.XAxis[col];
        if (ViewType == MapViewType.Easy && map.XEasy is { } easy && col < easy.Length) return MapData.Fixed((float)easy[col], "F2");
        if (ViewType == MapViewType.Hex)
        {
            double[] hex = map.XHex ?? map.XAxis;
            return ((int)hex[col] & 0xFFFF).ToString(hex.Max() <= 255 ? "X2" : "X4", CultureInfo.InvariantCulture);
        }
        return x.ToString(CultureInfo.CurrentCulture);
    }

    // MapViewerEx's row indicator (all three suites): the axis's last value on the top row whatever the row count, so a shorter
    // axis leaves the bottom rows blank, and a map without one has no row labels
    internal string YLabel(int displayRow)
    {
        MapData map = Map!;
        int i = map.UpsideDown ? (map.YAxis?.Length ?? 0) - 1 - displayRow : displayRow;
        if (map.YAxis == null || i < 0 || i >= map.YAxis.Length) return "";
        if (ViewType == MapViewType.Easy && map.YEasy is { } easy && i < easy.Length) return MapData.Fixed((float)easy[i], "F2");
        double y = map.YAxis[i];
        return ViewType == MapViewType.Hex ? ((int)y & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture) : y.ToString(CultureInfo.CurrentCulture);
    }

    // natural size: the widest cell and header text plus padding; the cells stretch to fill whatever is arranged
    protected override Size MeasureOverride(Size availableSize)
    {
        MapData? map = Map;
        if (map == null) return default;
        IBrush b = Brushes.Black;
        m_textScale = 1; // the natural size; Layout scales down again for a narrow width
        double cellW = 0, headerW = 0, h = Text("0", b).Height;
        for (int i = 0; i < map.Count; i++) cellW = Math.Max(cellW, Text(map.FormatCell(i, ViewType), b).Width);
        for (int c = 0; c < map.Cols; c++) cellW = Math.Max(cellW, Text(XLabel(c), b).Width);
        for (int r = 0; r < map.Rows; r++) headerW = Math.Max(headerW, Text(YLabel(r), b).Width);
        double gap = Gap;
        return new Size(headerW + 10 + map.Cols * (cellW + 10 + gap), (map.Rows + 1) * (h + 6) + map.Rows * gap);
    }

    // the header column and row keep their natural size, the cells share the rest
    private void Layout(MapData map)
    {
        IBrush b = Brushes.Black;
        m_textScale = 1;
        double widest = 0;
        for (int i = 0; i < map.Count; i++) widest = Math.Max(widest, Text(map.FormatCell(i, ViewType), b).Width);
        for (int c = 0; c < map.Cols; c++) widest = Math.Max(widest, Text(XLabel(c), b).Width);
        double rowHeader = 0;
        for (int r = 0; r < map.Rows; r++) rowHeader = Math.Max(rowHeader, Text(YLabel(r), b).Width);
        // the open-loop box (and T5Suite's boxes inside it) take 3 px on each side of the text, on a map that can have them
        bool boxes = OpenLoopMark == OpenLoopMark.Box && map.OpenLoop != null || map.Marks != null;
        double room = (Bounds.Width - rowHeader - 10) / Math.Max(1, map.Cols) - (boxes ? 10 : 4) - Gap;
        if (widest > 0 && room < widest) m_textScale = Math.Clamp(room / widest, 0.6, 1);
        double headerW = 0;
        for (int r = 0; r < map.Rows; r++) headerW = Math.Max(headerW, Text(YLabel(r), b).Width);
        m_headerW = headerW + 10;
        m_headerH = Text("0", b).Height + 6;
        m_cellW = Math.Max(1, (Bounds.Width - m_headerW) / map.Cols);
        m_cellH = Math.Max(1, (Bounds.Height - m_headerH) / map.Rows);
    }

    // cell edges on whole device pixels, and lines as filled strips of whole pixels: fractional column widths left every edge
    // between two pixels, antialiased into a blurry double line (T7Suite's GDI grid drew on whole pixels)
    private double Scale => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
    private double Snap(double v) => Math.Round(v * Scale) / Scale;
    private double Pixels(int n) => n / Scale;
    private double ColX(int col) => Snap(m_headerW + col * m_cellW);
    private double RowY(int displayRow) => Snap(m_headerH + displayRow * m_cellH);

    // the gap in whole device pixels, so the tiles' edges stay on pixels too
    private double Gap => Math.Round(Math.Max(0, CellGap) * Scale) / Scale;

    // a cell; with a gap its tile, which leaves the gap free before it (from the headers and the cell to its left / above)
    internal Rect CellRect(int displayRow, int col)
    {
        double gap = Gap;
        return new(new Point(ColX(col) + gap, RowY(displayRow) + gap), new Point(Math.Max(ColX(col) + gap, ColX(col + 1)), Math.Max(RowY(displayRow) + gap, RowY(displayRow + 1))));
    }

    // a frame inside rect, its strips whole pixels thick
    private void Frame(DrawingContext context, IBrush brush, Rect r, int pixels)
    {
        double t = Pixels(pixels);
        context.FillRectangle(brush, new Rect(r.X, r.Y, r.Width, t));
        context.FillRectangle(brush, new Rect(r.X, r.Bottom - t, r.Width, t));
        context.FillRectangle(brush, new Rect(r.X, r.Y, t, r.Height));
        context.FillRectangle(brush, new Rect(r.Right - t, r.Y, t, r.Height));
    }

    internal Point CellCenter(int displayRow, int col) => CellRect(displayRow, col).Center;

    private (int row, int col) HitCell(Point p)
    {
        MapData map = Map!;
        int col = (int)Math.Floor((p.X - m_headerW) / m_cellW);
        int row = (int)Math.Floor((p.Y - m_headerH) / m_cellH);
        return (Math.Clamp(row, 0, map.Rows - 1), Math.Clamp(col, 0, map.Cols - 1));
    }

    private Color CellColor(int raw, int min, int max)
    {
        // txlogger's green → yellow → red over min..max; T7Suite's raw*255/max made a fuel map red from its lowest cell
        if (!OnlineMode && !IsRedWhite) return HeatColor.Interpolate(min, max, raw, false, ColorMode);
        // T7Suite: b = raw*255/max (integer), alpha red, or a white→red tint online
        int b = raw * 255;
        if (max != 0) b /= max;
        if (OnlineMode)
        {
            int r = Math.Clamp(b / 2, 0, 255);
            return Color.FromRgb((byte)r, (byte)(255 - r), (byte)(255 - r));
        }
        return Color.FromArgb((byte)Math.Min(Math.Abs(b), 255), 255, 0, 0);
    }

    public override void Render(DrawingContext context)
    {
        MapData? map = Map;
        if (map == null) return;
        Layout(map);
        IBrush fg = Foreground ?? Brushes.Black;
        IBrush headerBg = HeaderBackground ?? new ImmutableSolidColorBrush(fg is ISolidColorBrush s ? Color.FromArgb(28, s.Color.R, s.Color.G, s.Color.B) : Color.FromArgb(28, 0, 0, 0));
        IBrush headerFg = HeaderForeground ?? fg;
        IBrush gridLine = GridLines ?? new ImmutableSolidColorBrush(fg is ISolidColorBrush s2 ? Color.FromArgb(60, s2.Color.R, s2.Color.G, s2.Color.B) : Colors.Gray);

        // headers
        double gap = Gap;
        context.FillRectangle(headerBg, new Rect(0, 0, Bounds.Width, m_headerH));
        context.FillRectangle(headerBg, new Rect(0, 0, m_headerW, Bounds.Height));
        for (int c = 0; c < map.Cols; c++)
        {
            var t = Text(XLabel(c), headerFg);
            // a squeezed column cuts its text at the cell like the DevExpress grid did, instead of writing over its neighbours; over its tile
            double x = m_headerW + c * m_cellW + gap, w = m_cellW - gap;
            using (context.PushClip(new Rect(x, 0, w, m_headerH)))
                context.DrawText(t, new Point(x + Math.Max(1, (w - t.Width) / 2), (m_headerH - t.Height) / 2));
        }
        for (int r = 0; r < map.Rows; r++)
        {
            var t = Text(YLabel(r), headerFg);
            context.DrawText(t, new Point(4, m_headerH + r * m_cellH + gap + (m_cellH - gap - t.Height) / 2));
        }

        int max = map.MaxValue(), min = map.MinValue();
        bool colors = !DisableColors && ViewType != MapViewType.Ascii;
        for (int r = 0; r < map.Rows; r++)
            for (int c = 0; c < map.Cols; c++)
            {
                int i = map.Index(r, c);
                if (i < 0) continue;
                Rect rect = CellRect(r, c);
                IBrush textBrush = fg;
                IPen? markOutline = null;
                if (colors)
                {
                    Color col = CellColor(map[i], min, max);
                    context.FillRectangle(new ImmutableSolidColorBrush(col), rect);
                    if (!IsRedWhite) textBrush = HeatColor.TextOn(col);
                    // a colour blind scale can wear the corner mark's own colour (Tritanopia's teal is SeaGreen to a tritanope):
                    // outline the mark like the text; the green → red scale keeps T7Suite's plain corner
                    if (ColorMode != ColorBlindMode.Normal && !OnlineMode && !IsRedWhite) markOutline = new Pen(HeatColor.TextOn(col), 1);
                }
                // T5Suite's marks, none in the ASCII view as there; its padlock first, 10 × 10 at the right ending where the closed-loop box
                // begins, which goes over it like the 1 px boxes (left out of a cell too narrow for it)
                CellMarks marks = ViewType != MapViewType.Ascii ? map.Marks?.Invoke(i) ?? default : default;
                bool padlock = marks.Locked && rect.Width >= 12;
                if (padlock)
                    context.DrawImage(Padlock.Value, new Rect(Snap(rect.Right - Pixels(3)) - 10, Snap(rect.Center.Y - 5), 10, 10));
                if (OpenLoopMark != OpenLoopMark.None && map.IsOpenLoop(i))
                {
                    if (OpenLoopMark == OpenLoopMark.Box)
                    {
                        // T7Suite's 2 px black frame inside the grid line
                        Frame(context, Brushes.Black, rect.Deflate(Pixels(1)), 2);
                    }
                    else
                    {
                        var tri = new StreamGeometry();
                        using (var g = tri.Open())
                        {
                            g.BeginFigure(rect.TopRight, true);
                            g.LineTo(new Point(rect.Right - rect.Height / 2, rect.Top));
                            g.LineTo(new Point(rect.Right, rect.Top + rect.Height / 2));
                            g.EndFigure(true);
                        }
                        context.DrawGeometry(OpenLoopCorner, markOutline, tri);
                    }
                }
                // T5Suite's 1 px boxes just inside the closed-loop box: the knock adaption area blue, the boost adaption range white,
                // the AFR samples from red (1) to green (255 and up)
                Rect inner = rect.Deflate(Pixels(3));
                if (marks.KnockAdaption) Frame(context, Brushes.Blue, inner, 1);
                if (marks.BoostAdaption) Frame(context, Brushes.White, inner, 1);
                if (marks.Samples > 0)
                {
                    byte n = (byte)Math.Min(marks.Samples, 255);
                    Frame(context, new ImmutableSolidColorBrush(Color.FromRgb((byte)(255 - n), n, 0)), inner, 1);
                }
                if (i == m_highlight)
                {
                    context.FillRectangle(Highlight, rect);
                    textBrush = Brushes.Black;
                }
                if (m_selected.Contains(i)) context.FillRectangle(SelectionFill, rect);

                bool editing = m_editing && (r == m_focusRow && c == m_focusCol || m_selected.Contains(i) && m_selected.Contains(map.Index(m_focusRow, m_focusCol)));
                if (editing) context.FillRectangle(Brushes.White, rect.Deflate(1));
                if (editing) textBrush = Brushes.Black;
                var t = Text(editing ? m_input : map.FormatCell(i, ViewType), textBrush);
                // an edit wider than the cell keeps its end, where the typing is, in view; a value centred left of its padlock (T5Suite's
                // text was left aligned) but clear of the closed-loop box, over the padlock in a narrow cell; the live cell hides the padlock
                bool besidePadlock = padlock && !editing && i != m_highlight;
                double x = editing && t.Width > rect.Width - 2 ? rect.Right - 1 - t.Width
                    : rect.X + Math.Max(besidePadlock ? 3 : 1, (rect.Width - (besidePadlock ? 13 : 0) - t.Width) / 2);
                using (context.PushClip(rect))
                    context.DrawText(t, new Point(x, rect.Y + (rect.Height - t.Height) / 2));
            }

        // the lines on the pixel left of / above each edge, the last one inside the table; tiles have the gap instead
        double px = Pixels(1), top = RowY(0), bottom = RowY(map.Rows), left = ColX(0), right = ColX(map.Cols);
        if (Gap == 0)
        {
            for (int c = 0; c <= map.Cols; c++)
                context.FillRectangle(gridLine, new Rect(Math.Min(ColX(c), right - px), top, px, bottom - top));
            for (int r = 0; r <= map.Rows; r++)
                context.FillRectangle(gridLine, new Rect(left, Math.Min(RowY(r), bottom - px), right - left, px));
        }
        if (HeaderLines is { } headerLine)
        {
            // the header cells framed like the DevExpress grid's
            context.FillRectangle(headerLine, new Rect(left, 0, right - left, px));
            for (int c = 0; c <= map.Cols; c++)
                context.FillRectangle(headerLine, new Rect(Math.Min(ColX(c), right - px), 0, px, top));
            context.FillRectangle(headerLine, new Rect(0, top, px, bottom - top));
            for (int r = 0; r <= map.Rows; r++)
                context.FillRectangle(headerLine, new Rect(0, Math.Min(RowY(r), bottom - px), left, px));
        }

        if (m_selected.Count > 0)
        {
            foreach (int i in m_selected)
            {
                var (r, c) = map.Cell(i);
                Frame(context, SelectionLine, CellRect(r, c), 1);
            }
            if (IsFocused) Frame(context, FocusLine, CellRect(m_focusRow, m_focusCol), 2);
        }
    }

    // ---- selection ----

    private void SelectBlock(int row0, int col0, int row1, int col1, bool add)
    {
        MapData map = Map!;
        if (!add) m_selected.Clear();
        for (int r = Math.Min(row0, row1); r <= Math.Max(row0, row1); r++)
            for (int c = Math.Min(col0, col1); c <= Math.Max(col0, col1); c++)
            {
                int i = map.Index(r, c);
                if (i >= 0) m_selected.Add(i);
            }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        MapData? map = Map;
        if (map == null) return;
        Focus();
        CommitInput();
        Point p = e.GetPosition(this);
        if (p.X < m_headerW || p.Y < m_headerH) return;
        var (row, col) = HitCell(p);
        var props = e.GetCurrentPoint(this).Properties;
        KeyModifiers mods = e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta);
        bool ctrl = mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta);

        // the right button only opens the context menu, the selection stays as it is
        if (!props.IsLeftButtonPressed) return;

        if (ctrl)
        {
            int i = map.Index(row, col);
            if (i >= 0 && !m_selected.Remove(i)) m_selected.Add(i);
            (m_anchorRow, m_anchorCol) = (row, col);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (mods.HasFlag(KeyModifiers.Shift))
        {
            SelectBlock(m_anchorRow, m_anchorCol, row, col, false);
            m_dragging = true;
        }
        else
        {
            (m_anchorRow, m_anchorCol) = (row, col);
            SelectBlock(row, col, row, col, false);
            m_dragging = true;
        }
        (m_focusRow, m_focusCol) = (row, col);
        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!m_dragging || Map == null) return;
        var (row, col) = HitCell(e.GetPosition(this));
        if (row == m_focusRow && col == m_focusCol) return;
        (m_focusRow, m_focusCol) = (row, col);
        SelectBlock(m_anchorRow, m_anchorCol, row, col, false);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        m_dragging = false;
        e.Pointer.Capture(null);
    }

    // ---- keyboard ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        MapData? map = Map;
        if (map == null || e.Handled) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        e.Handled = true;

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.C: CopyText(); return;
                case Key.Z: if (!IsReadOnly) map.Undo(); return;
                case Key.Y: if (!IsReadOnly) map.Redo(); return;
                case Key.A: SelectBlock(0, 0, map.Rows - 1, map.Cols - 1, false); return;
            }
            e.Handled = false;
            return;
        }

        switch (e.Key)
        {
            case Key.Up: case Key.Down: case Key.Left: case Key.Right:
                CommitInput();
                MoveFocus(e.Key, shift);
                return;
            case Key.Escape:
                m_editing = false;
                m_input = "";
                InvalidateVisual();
                return;
            case Key.Enter:
            case Key.F2 when !m_editing:
                if (m_editing) CommitInput();
                else StartEdit(null);
                return;
            case Key.Back when m_editing:
                if (m_input.Length > 0) m_input = m_input[..^1];
                InvalidateVisual();
                return;
        }
        // while editing, +/- are part of the typed value
        if (IsReadOnly || m_selected.Count == 0 || m_editing)
        {
            e.Handled = false;
            return;
        }
        MapStep? step = e.Key switch
        {
            Key.Add or Key.OemPlus => MapStep.Up,
            Key.Subtract or Key.OemMinus => MapStep.Down,
            Key.PageUp => MapStep.PageUp,
            Key.PageDown => MapStep.PageDown,
            Key.Home => MapStep.Max,
            Key.End => MapStep.Zero,
            _ => null,
        };
        if (step is MapStep s)
        {
            // + and - also arrive as text input, which must not start the cell editor
            m_suppressText = s is MapStep.Up or MapStep.Down;
            MapOps.Step(map, m_selected, ViewType, s);
            return;
        }
        e.Handled = false;
    }

    // cell editor: typing starts it with the typed text, F2/Enter with the cell's value; Enter commits the focused cell only
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        bool suppress = m_suppressText;
        m_suppressText = false;
        if (suppress || IsReadOnly || Map == null || ViewType == MapViewType.Ascii || string.IsNullOrEmpty(e.Text)) return;
        string text = "";
        foreach (char ch in e.Text)
        {
            bool ok = ViewType == MapViewType.Hex
                ? Uri.IsHexDigit(ch)
                : char.IsDigit(ch) || ch == '.' || ch == ',' || ch == '-';
            if (!ok) return;
            text += ch;
        }
        if (!m_editing) StartEdit(text);
        else m_input += text;
        e.Handled = true;
        InvalidateVisual();
    }

    private void StartEdit(string? typed)
    {
        MapData? map = Map;
        int i = map?.Index(m_focusRow, m_focusCol) ?? -1;
        if (map == null || i < 0 || IsReadOnly || ViewType == MapViewType.Ascii) return;
        m_editing = true;
        m_input = typed ?? (ViewType == MapViewType.Easy
            ? MapData.Fixed((float)map.Scaled(map[i]) * (float)map.Factor + (float)map.Offset, "F2")
            : map.FormatCell(i, ViewType));
        InvalidateVisual();
    }

    private void CommitInput()
    {
        if (!m_editing) return;
        m_editing = false;
        MapData map = Map!;
        string text = m_input;
        m_input = "";
        int i = map.Index(m_focusRow, m_focusCol);
        if (i >= 0 && text.Length > 0)
        {
            if (map.TryParse(text, ViewType, out int raw, out string error)) map.Set(EditTargets(i).Select(c => (c, raw)));
            else EditRejected?.Invoke(this, error);
        }
        InvalidateVisual();
    }

    // a typed value goes to every selected cell when the focused cell is one of them (one undo step); T7Suite set only the focused one
    private IEnumerable<int> EditTargets(int focused) => m_selected.Contains(focused) ? m_selected : [focused];

    private void MoveFocus(Key key, bool extend)
    {
        MapData map = Map!;
        int row = m_focusRow, col = m_focusCol;
        switch (key)
        {
            case Key.Up: row--; break;
            case Key.Down: row++; break;
            case Key.Left: col--; break;
            case Key.Right: col++; break;
        }
        row = Math.Clamp(row, 0, map.Rows - 1);
        col = Math.Clamp(col, 0, map.Cols - 1);
        (m_focusRow, m_focusCol) = (row, col);
        if (!extend) (m_anchorRow, m_anchorCol) = (row, col);
        SelectBlock(m_anchorRow, m_anchorCol, row, col, false);
    }

    // ---- clipboard and menu ----

    private IClipboard? Clipboard => TopLevel.GetTopLevel(this)?.Clipboard;

    /// <summary>Ctrl+C: the shown text of the selected cells' bounding rows/columns, tab separated (what DevExpress copied).</summary>
    public void CopyText()
    {
        MapData? map = Map;
        if (map == null || m_selected.Count == 0 || Clipboard is not { } clipboard) return;
        var pos = m_selected.Select(map.Cell).ToList();
        var lines = new List<string>();
        for (int r = pos.Min(p => p.displayRow); r <= pos.Max(p => p.displayRow); r++)
        {
            var cells = new List<string>();
            for (int c = pos.Min(p => p.col); c <= pos.Max(p => p.col); c++)
            {
                int i = map.Index(r, c);
                cells.Add(i >= 0 && m_selected.Contains(i) ? map.FormatCell(i, ViewType) : "");
            }
            lines.Add(string.Join('\t', cells));
        }
        _ = clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
    }

    /// <summary>T7Suite's "Copy selected cells", its own clipboard format; with nothing selected the whole map.</summary>
    public void CopySelection()
    {
        MapData? map = Map;
        if (map == null || Clipboard is not { } clipboard) return;
        IEnumerable<int> cells = m_selected.Count > 0 ? m_selected : Enumerable.Range(0, map.Count);
        _ = clipboard.SetTextAsync(MapOps.Copy(map, cells, ViewType));
    }

    /// <summary>Pastes T7Suite clipboard text at its original cells, or shifted onto the first selected cell.</summary>
    public async void PasteAt(bool atSelection)
    {
        MapData? map = Map;
        if (map == null || IsReadOnly || Clipboard is not { } clipboard) return;
        string? text = await clipboard.TryGetTextAsync();
        if (text == null) return;
        (int, int)? anchor = null;
        if (atSelection)
        {
            if (m_selected.Count == 0) return;
            anchor = m_selected.Select(map.Cell).OrderBy(p => p.displayRow).ThenBy(p => p.col).First();
        }
        MapOps.Paste(map, text, anchor);
    }

    private ContextMenu BuildContextMenu()
    {
        MenuItem Item(string header, Action action) { var m = new MenuItem { Header = header }; m.Click += (_, _) => action(); return m; }
        var paste = new MenuItem { Header = "Paste selected cells" };
        paste.Items.Add(Item("At original position", () => PasteAt(false)));
        paste.Items.Add(Item("At currently selected location", () => PasteAt(true)));
        var menu = new ContextMenu();
        menu.Items.Add(Item("Copy selected cells", CopySelection));
        menu.Items.Add(paste);
        menu.Items.Add(Item("Smooth selection", () => { if (Map != null && !IsReadOnly) MapOps.Smooth(Map, m_selected); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Undo", () => { if (!IsReadOnly) Map?.Undo(); }));
        menu.Items.Add(Item("Redo", () => { if (!IsReadOnly) Map?.Redo(); }));
        return menu;
    }
}
