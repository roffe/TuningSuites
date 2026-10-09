using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MapControls;

public enum MapStep { Up, Down, PageUp, PageDown, Max, Zero }

public enum MapMath { Add = 0, Multiply = 1, Divide = 2, Fill = 3 }

/// <summary>
/// The editing operations of T7Suite's MapViewerEx, on raw values, each applied as one undo step through
/// <see cref="MapData.Set"/> (which clamps to the cell's range).
/// </summary>
public static class MapOps
{
    /// <summary>Keyboard steps: ±1, PgUp/PgDn ±10 (±0x10 in hex view), Home = max, End = 0.</summary>
    public static void Step(MapData map, IReadOnlyCollection<int> cells, MapViewType view, MapStep step)
    {
        int page = view == MapViewType.Hex ? 0x10 : 10;
        map.Set(cells.Select(i => (i, step switch
        {
            MapStep.Up => map[i] + 1,
            MapStep.Down => map[i] - 1,
            MapStep.PageUp => map[i] + page,
            MapStep.PageDown => map[i] - page,
            MapStep.Max => map.MaxRaw,
            _ => 0,
        })));
    }

    /// <summary>
    /// Add / multiply / divide / fill the selection with w. In Easy view on the physical value (raw*factor+offset, result
    /// back to raw truncated), otherwise on the raw value with w rounded half to even (fill truncates). ASCII does nothing.
    /// </summary>
    public static void Apply(MapData map, IReadOnlyCollection<int> cells, MapViewType view, MapMath op, double w)
    {
        if (view == MapViewType.Ascii) return;
        double f = map.Factor, o = map.Offset;
        map.Set(cells.Select(i => (i, view == MapViewType.Easy ? Easy(map[i]) : Raw(map[i]))));

        int Raw(int v)
        {
            int rw = (int)Math.Round(w, MidpointRounding.ToEven);
            return op switch
            {
                MapMath.Add => v + rw,
                MapMath.Multiply => v * rw,
                MapMath.Divide => rw == 0 ? v : v / rw,
                _ => (int)w,
            };
        }

        int Easy(int v)
        {
            double phys = v * f + o;
            double result = op switch
            {
                MapMath.Add => phys + w - o,
                MapMath.Multiply => phys * w - o,
                MapMath.Divide => (w == 0 ? phys : phys / w) - o,
                _ => w - o,
            };
            return (int)(f == 0 ? result : result / f);
        }
    }

    /// <summary>
    /// Smooth the selection's bounding box (at least 3 cells). A single row or column is interpolated linearly between its
    /// end cells with an integer step; a block gets every interior cell replaced, row by row from the top, by the average of
    /// its left/right and up/down neighbour averages (already smoothed neighbours included), like the old viewer.
    /// </summary>
    public static bool Smooth(MapData map, IReadOnlyCollection<int> cells)
    {
        if (cells.Count <= 2) return false;
        var pos = cells.Select(map.Cell).ToList();
        int minRow = pos.Min(p => p.displayRow), maxRow = pos.Max(p => p.displayRow);
        int minCol = pos.Min(p => p.col), maxCol = pos.Max(p => p.col);
        var v = new Dictionary<int, int>();
        int Get(int row, int col)
        {
            int i = map.Index(row, col);
            return i < 0 ? 0 : v.TryGetValue(i, out int x) ? x : map[i];
        }
        void Put(int row, int col, int value)
        {
            int i = map.Index(row, col);
            if (i >= 0) v[i] = value;
        }

        if (minCol == maxCol || minRow == maxRow)
        {
            bool column = minCol == maxCol;
            int n = column ? maxRow - minRow + 1 : maxCol - minCol + 1;
            int bot = column ? Get(minRow, minCol) : Get(minRow, minCol);
            int top = column ? Get(maxRow, minCol) : Get(minRow, maxCol);
            int diff = (top - bot) / (n - 1);
            for (int t = 1; t < n - 1; t++)
            {
                if (column) Put(minRow + t, minCol, bot + t * diff);
                else Put(minRow, minCol + t, bot + t * diff);
            }
        }
        else
        {
            for (int row = minRow + 1; row < maxRow; row++)
                for (int col = minCol + 1; col < maxCol; col++)
                {
                    double lr = (Get(row, col - 1) + Get(row, col + 1)) / 2.0;
                    double ud = (Get(row - 1, col) + Get(row + 1, col)) / 2.0;
                    Put(row, col, (int)Math.Round((float)((lr + ud) / 2), MidpointRounding.AwayFromZero));
                }
        }
        map.Set(v.Select(kv => (kv.Key, kv.Value)));
        return true;
    }

    /// <summary>
    /// T7Suite's clipboard text: the view type digit, then "col:row:raw:~" per cell with the displayed row, in display order.
    /// Values are always raw decimal integers.
    /// </summary>
    public static string Copy(MapData map, IEnumerable<int> cells, MapViewType view)
    {
        var sb = new StringBuilder(((int)view).ToString(CultureInfo.InvariantCulture));
        foreach (int i in cells.OrderBy(i => map.Cell(i).displayRow).ThenBy(i => map.Cell(i).col))
        {
            var (row, col) = map.Cell(i);
            sb.Append(CultureInfo.InvariantCulture, $"{col}:{row}:{map[i]}:~");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Pastes T7Suite clipboard text, at the original cells or, with an anchor (displayed row, col), shifted so the first
    /// copied cell lands on the anchor. The values are raw whatever view they were copied from; cells off the map are skipped.
    /// </summary>
    public static bool Paste(MapData map, string text, (int displayRow, int col)? anchor = null)
    {
        if (string.IsNullOrEmpty(text) || !char.IsDigit(text[0])) return false;
        var cells = new List<(int row, int col, int raw)>();
        foreach (string entry in text[1..].Split('~', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] p = entry.Split(':');
            if (p.Length < 3
                || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int col)
                || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int row)
                || !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                continue;
            cells.Add((row, col, (int)value));
        }
        if (cells.Count == 0) return false;
        int dRow = 0, dCol = 0;
        if (anchor is var (aRow, aCol))
        {
            dRow = aRow - cells[0].row;
            dCol = aCol - cells[0].col;
        }
        map.Set(cells.Select(c => (map.Index(c.row + dRow, c.col + dCol), c.raw)));
        return true;
    }
}
