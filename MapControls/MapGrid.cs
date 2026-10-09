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

namespace MapControls;

public enum OpenLoopMark { None = 0, Box = 1, Corner = 2 }

/// <summary>
/// The map table, replacing MapViewerEx's DevExpress grid: X axis values as column headers, Y axis values as row headers,
/// cells coloured like T7Suite (green→red by value/max, a red-white variant, a light tint in online mode), open-loop marks
/// and the yellow live cell. Behaves like the DevExpress grid T7Suite used: click/drag selects a block, Shift extends, Ctrl
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
    public static readonly StyledProperty<OpenLoopMark> OpenLoopMarkProperty = AvaloniaProperty.Register<MapGrid, OpenLoopMark>(nameof(OpenLoopMark));
    public static readonly StyledProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<MapGrid, bool>(nameof(IsReadOnly));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<MapGrid>();
    public static readonly StyledProperty<double> FontSizeProperty = TextElement.FontSizeProperty.AddOwner<MapGrid>();
    public static readonly StyledProperty<FontFamily> FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner<MapGrid>();

    public MapData? Map { get => GetValue(MapProperty); set => SetValue(MapProperty, value); }
    public MapViewType ViewType { get => GetValue(ViewTypeProperty); set => SetValue(ViewTypeProperty, value); }
    public bool IsRedWhite { get => GetValue(IsRedWhiteProperty); set => SetValue(IsRedWhiteProperty, value); }
    public bool OnlineMode { get => GetValue(OnlineModeProperty); set => SetValue(OnlineModeProperty, value); }
    public bool DisableColors { get => GetValue(DisableColorsProperty); set => SetValue(DisableColorsProperty, value); }
    public OpenLoopMark OpenLoopMark { get => GetValue(OpenLoopMarkProperty); set => SetValue(OpenLoopMarkProperty, value); }
    public bool IsReadOnly { get => GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    /// <summary>The selection changed (data indices in <see cref="SelectedCells"/>).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>A typed value was rejected, with the reason.</summary>
    public event EventHandler<string>? EditRejected;

    private static readonly IBrush Highlight = new ImmutableSolidColorBrush(Colors.Yellow);
    private static readonly IBrush SelectionFill = new ImmutableSolidColorBrush(Color.FromArgb(90, 0x33, 0x66, 0xFF));
    private static readonly IPen SelectionPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x33, 0x66, 0xFF)), 1);
    private static readonly IPen FocusPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x10, 0x30, 0xC0)), 2);
    private static readonly IPen OpenLoopPen = new ImmutablePen(Brushes.Black, 2);
    private static readonly IBrush OpenLoopCorner = new ImmutableSolidColorBrush(Colors.SeaGreen);

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
        AffectsRender<MapGrid>(ViewTypeProperty, IsRedWhiteProperty, OnlineModeProperty, DisableColorsProperty, OpenLoopMarkProperty, ForegroundProperty);
        AffectsMeasure<MapGrid>(MapProperty, ViewTypeProperty, FontSizeProperty, FontFamilyProperty);
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
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface, FontSize, brush);

    private string XLabel(int col)
    {
        MapData map = Map!;
        if (map.XAxis == null || col >= map.XAxis.Length) return col.ToString(CultureInfo.CurrentCulture);
        double x = map.XAxis[col];
        if (ViewType == MapViewType.Hex)
            return ((int)x & 0xFFFF).ToString(map.XAxis.Max() <= 255 ? "X2" : "X4", CultureInfo.InvariantCulture);
        return x.ToString(CultureInfo.CurrentCulture);
    }

    private string YLabel(int displayRow)
    {
        MapData map = Map!;
        int row = map.DataRow(displayRow);
        if (map.YAxis == null || row >= map.YAxis.Length) return row.ToString(CultureInfo.CurrentCulture);
        double y = map.YAxis[row];
        return ViewType == MapViewType.Hex ? ((int)y & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture) : y.ToString(CultureInfo.CurrentCulture);
    }

    // natural size: the widest cell and header text plus padding; the cells stretch to fill whatever is arranged
    protected override Size MeasureOverride(Size availableSize)
    {
        MapData? map = Map;
        if (map == null) return default;
        IBrush b = Brushes.Black;
        double cellW = 0, headerW = 0, h = Text("0", b).Height;
        for (int i = 0; i < map.Count; i++) cellW = Math.Max(cellW, Text(map.FormatCell(i, ViewType), b).Width);
        for (int c = 0; c < map.Cols; c++) cellW = Math.Max(cellW, Text(XLabel(c), b).Width);
        for (int r = 0; r < map.Rows; r++) headerW = Math.Max(headerW, Text(YLabel(r), b).Width);
        return new Size(headerW + 10 + map.Cols * (cellW + 10), (map.Rows + 1) * (h + 6));
    }

    // the header column and row keep their natural size, the cells share the rest
    private void Layout(MapData map)
    {
        IBrush b = Brushes.Black;
        double headerW = 0;
        for (int r = 0; r < map.Rows; r++) headerW = Math.Max(headerW, Text(YLabel(r), b).Width);
        m_headerW = headerW + 10;
        m_headerH = Text("0", b).Height + 6;
        m_cellW = Math.Max(1, (Bounds.Width - m_headerW) / map.Cols);
        m_cellH = Math.Max(1, (Bounds.Height - m_headerH) / map.Rows);
    }

    private Rect CellRect(int displayRow, int col) =>
        new(m_headerW + col * m_cellW, m_headerH + displayRow * m_cellH, m_cellW, m_cellH);

    internal Point CellCenter(int displayRow, int col) => CellRect(displayRow, col).Center;

    private (int row, int col) HitCell(Point p)
    {
        MapData map = Map!;
        int col = (int)Math.Floor((p.X - m_headerW) / m_cellW);
        int row = (int)Math.Floor((p.Y - m_headerH) / m_cellH);
        return (Math.Clamp(row, 0, map.Rows - 1), Math.Clamp(col, 0, map.Cols - 1));
    }

    private Color CellColor(int raw, int max)
    {
        // T7Suite: b = raw*255/max (integer), green→red, or alpha red, or a white→red tint online
        int b = raw * 255;
        if (max != 0) b /= max;
        if (OnlineMode)
        {
            int r = Math.Clamp(b / 2, 0, 255);
            return Color.FromRgb((byte)r, (byte)(255 - r), (byte)(255 - r));
        }
        if (IsRedWhite) return Color.FromArgb((byte)Math.Min(Math.Abs(b), 255), 255, 0, 0);
        int g = Math.Clamp(b, 0, 255);
        return Color.FromRgb((byte)g, (byte)(255 - g), 0);
    }

    public override void Render(DrawingContext context)
    {
        MapData? map = Map;
        if (map == null) return;
        Layout(map);
        IBrush fg = Foreground ?? Brushes.Black;
        var headerBg = new ImmutableSolidColorBrush(fg is ISolidColorBrush s ? Color.FromArgb(28, s.Color.R, s.Color.G, s.Color.B) : Color.FromArgb(28, 0, 0, 0));
        var gridPen = new Pen(new ImmutableSolidColorBrush(fg is ISolidColorBrush s2 ? Color.FromArgb(60, s2.Color.R, s2.Color.G, s2.Color.B) : Colors.Gray), 1);

        // headers
        context.FillRectangle(headerBg, new Rect(0, 0, Bounds.Width, m_headerH));
        context.FillRectangle(headerBg, new Rect(0, 0, m_headerW, Bounds.Height));
        for (int c = 0; c < map.Cols; c++)
        {
            var t = Text(XLabel(c), fg);
            context.DrawText(t, new Point(m_headerW + c * m_cellW + (m_cellW - t.Width) / 2, (m_headerH - t.Height) / 2));
        }
        for (int r = 0; r < map.Rows; r++)
        {
            var t = Text(YLabel(r), fg);
            context.DrawText(t, new Point(4, m_headerH + r * m_cellH + (m_cellH - t.Height) / 2));
        }

        int max = map.MaxValue();
        bool colors = !DisableColors && ViewType != MapViewType.Ascii;
        for (int r = 0; r < map.Rows; r++)
            for (int c = 0; c < map.Cols; c++)
            {
                int i = map.Index(r, c);
                if (i < 0) continue;
                Rect rect = CellRect(r, c);
                IBrush textBrush = fg;
                if (colors)
                {
                    Color col = CellColor(map[i], max);
                    context.FillRectangle(new ImmutableSolidColorBrush(col), rect);
                    if (!IsRedWhite) textBrush = Brushes.Black;
                }
                if (OpenLoopMark != OpenLoopMark.None && map.IsOpenLoop(i))
                {
                    if (OpenLoopMark == OpenLoopMark.Box)
                    {
                        context.DrawRectangle(OpenLoopPen, rect.Deflate(2));
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
                        context.DrawGeometry(OpenLoopCorner, null, tri);
                    }
                }
                if (i == m_highlight)
                {
                    context.FillRectangle(Highlight, rect);
                    textBrush = Brushes.Black;
                }
                if (m_selected.Contains(i)) context.FillRectangle(SelectionFill, rect);

                bool editing = m_editing && r == m_focusRow && c == m_focusCol;
                if (editing) context.FillRectangle(Brushes.White, rect.Deflate(1));
                if (editing) textBrush = Brushes.Black;
                var t = Text(editing ? m_input : map.FormatCell(i, ViewType), textBrush);
                context.DrawText(t, new Point(rect.X + (rect.Width - t.Width) / 2, rect.Y + (rect.Height - t.Height) / 2));
            }

        for (int c = 0; c <= map.Cols; c++)
            context.DrawLine(gridPen, new Point(m_headerW + c * m_cellW, m_headerH), new Point(m_headerW + c * m_cellW, m_headerH + map.Rows * m_cellH));
        for (int r = 0; r <= map.Rows; r++)
            context.DrawLine(gridPen, new Point(m_headerW, m_headerH + r * m_cellH), new Point(m_headerW + map.Cols * m_cellW, m_headerH + r * m_cellH));

        if (m_selected.Count > 0)
        {
            foreach (int i in m_selected)
            {
                var (r, c) = map.Cell(i);
                context.DrawRectangle(SelectionPen, CellRect(r, c));
            }
            if (IsFocused) context.DrawRectangle(FocusPen, CellRect(m_focusRow, m_focusCol).Deflate(1));
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
            ? ((float)map[i] * (float)map.Factor + (float)map.Offset).ToString("F2", CultureInfo.CurrentCulture)
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
            if (map.TryParse(text, ViewType, out int raw, out string error)) map.Set([(i, raw)]);
            else EditRejected?.Invoke(this, error);
        }
        InvalidateVisual();
    }

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
