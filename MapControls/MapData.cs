using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MapControls;

/// <summary>How cells are shown, the values T7Suite's view combo and clipboard use.</summary>
public enum MapViewType { Hex = 0, Decimal = 1, Easy = 2, Ascii = 3 }

/// <summary>
/// One map (calibration table) as T7Suite's MapViewerEx holds it: raw integers decoded from the symbol's bytes (8-bit
/// unsigned, or 16-bit big-endian where 0xF001..0xFFFF read as negative), shown and edited as raw values; "Easy" view only
/// changes the display to raw*factor+offset. Data index i is cell (row i / Cols, col i % Cols) in byte order; with
/// UpsideDown (always set by T7Suite) the last data row is drawn at the top.
/// Every change goes through <see cref="Set"/>, which clamps, records one undo step and raises <see cref="Changed"/>.
/// </summary>
public sealed class MapData
{
    public string Name { get; }
    public int Cols { get; }
    public int Rows { get; }
    /// <summary>Number of values; the last row is partial when it isn't a multiple of Cols.</summary>
    public int Count => m_raw.Length;
    public bool SixteenBit { get; }
    public double Factor { get; set; } = 1;
    public double Offset { get; set; }
    public bool UpsideDown { get; set; } = true;

    /// <summary>One value per column / data row (the corrected axis symbol values), null if unknown.</summary>
    public double[]? XAxis { get; set; }
    public double[]? YAxis { get; set; }
    public string XName { get; set; } = "";
    public string YName { get; set; } = "";
    public string ZName { get; set; } = "";

    /// <summary>Open-loop load limit per data row in X-axis units; a cell is open loop when the limit is above its X value.</summary>
    public double[]? OpenLoop { get; set; }

    /// <summary>
    /// Raw values above it read as negative (two's complement). 16-bit cells: 0xF000 (T7 / T8), so only -0xFFF..0xF000 survive a
    /// save and reload unchanged; T5Suite used 32000. 8-bit cells: 0xFF (never negative), 128 for T5's signed ones.
    /// </summary>
    public int SignAbove { get; }

    private int Span => SixteenBit ? 0x10000 : 0x100;

    public int MinRaw => SignAbove - Span + 1;
    public int MaxRaw => SignAbove;

    /// <summary>T5Suite's 3.0 to 5.0 bar sensor views: the Decimal and Easy values are raw × percent / 100 (100 shows them raw).</summary>
    public int ScalePercent { get; set; } = 100;

    private int Scaled(int raw) => ScalePercent == 100 ? raw : raw * ScalePercent / 100;

    /// <summary>True once anything changed since load or <see cref="MarkSaved"/>.</summary>
    public bool Mutated { get; private set; }

    public event EventHandler? Changed;

    private int[] m_raw;
    private readonly Stack<Edit[]> m_undo = new(), m_redo = new();

    private readonly record struct Edit(int Index, int Before, int After);

    public MapData(string name, byte[] content, int cols, bool sixteenBit, int? signAbove = null)
    {
        Name = name;
        SixteenBit = sixteenBit;
        SignAbove = signAbove ?? (sixteenBit ? 0xF000 : 0xFF);
        Cols = Math.Max(1, cols);
        m_raw = Decode(content, sixteenBit, SignAbove);
        Rows = Math.Max(1, (m_raw.Length + Cols - 1) / Cols);
    }

    public int this[int index] => m_raw[index];

    public double Physical(int index) => Scaled(m_raw[index]) * Factor + Offset;

    public int[] RawValues() => (int[])m_raw.Clone();

    public double[] PhysicalValues()
    {
        var v = new double[m_raw.Length];
        for (int i = 0; i < v.Length; i++) v[i] = Physical(i);
        return v;
    }

    /// <summary>Largest raw value, at least 0: the colour scale's denominator (MaxValueInTable).</summary>
    public int MaxValue()
    {
        int max = 0;
        foreach (int v in m_raw) max = Math.Max(max, v);
        return max;
    }

    /// <summary>Smallest raw value: the bottom of the colour scale.</summary>
    public int MinValue() => m_raw.Length == 0 ? 0 : m_raw.Min();

    public int DataRow(int displayRow) => UpsideDown ? Rows - 1 - displayRow : displayRow;

    /// <summary>Data index of a displayed cell, -1 past the end of a partial last row.</summary>
    public int Index(int displayRow, int col)
    {
        if (displayRow < 0 || displayRow >= Rows || col < 0 || col >= Cols) return -1;
        int i = DataRow(displayRow) * Cols + col;
        return i < m_raw.Length ? i : -1;
    }

    public (int displayRow, int col) Cell(int index)
    {
        int row = index / Cols;
        return (UpsideDown ? Rows - 1 - row : row, index % Cols);
    }

    public bool IsOpenLoop(int index)
    {
        if (OpenLoop == null || XAxis == null) return false;
        int row = index / Cols, col = index % Cols;
        return row < OpenLoop.Length && col < XAxis.Length && OpenLoop[row] > XAxis[col];
    }

    public static int[] Decode(byte[] content, bool sixteenBit, int? signAbove = null)
    {
        int above = signAbove ?? (sixteenBit ? 0xF000 : 0xFF);
        if (!sixteenBit) return Array.ConvertAll(content, b => b > above ? b - 0x100 : b);
        var raw = new int[content.Length / 2];
        for (int i = 0; i < raw.Length; i++)
        {
            int b = content[i * 2] << 8 | content[i * 2 + 1];
            raw[i] = b > above ? b - 0x10000 : b;
        }
        return raw;
    }

    /// <summary>Back to the symbol's bytes: 16-bit big-endian two's complement, or one byte per value.</summary>
    public byte[] ToBytes()
    {
        if (!SixteenBit) return Array.ConvertAll(m_raw, v => (byte)v);
        var bytes = new byte[m_raw.Length * 2];
        for (int i = 0; i < m_raw.Length; i++)
        {
            bytes[i * 2] = (byte)(m_raw[i] >> 8);
            bytes[i * 2 + 1] = (byte)m_raw[i];
        }
        return bytes;
    }

    /// <summary>Applies new raw values (clamped to the cell's range) as one undo step.</summary>
    public void Set(IEnumerable<(int index, int raw)> values)
    {
        var edits = new List<Edit>();
        foreach (var (index, raw) in values)
        {
            if (index < 0 || index >= m_raw.Length) continue;
            int v = Math.Clamp(raw, MinRaw, MaxRaw);
            if (v == m_raw[index]) continue;
            edits.Add(new Edit(index, m_raw[index], v));
            m_raw[index] = v;
        }
        if (edits.Count == 0) return;
        m_undo.Push(edits.ToArray());
        m_redo.Clear();
        Mutated = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool CanUndo => m_undo.Count > 0;
    public bool CanRedo => m_redo.Count > 0;

    public void Undo() => Replay(m_undo, m_redo, e => e.Before);

    public void Redo() => Replay(m_redo, m_undo, e => e.After);

    private void Replay(Stack<Edit[]> from, Stack<Edit[]> to, Func<Edit, int> value)
    {
        if (from.Count == 0) return;
        Edit[] step = from.Pop();
        foreach (Edit e in step) m_raw[e.Index] = value(e);
        to.Push(step);
        Mutated = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces all values (read from file or ECU), dropping the undo history.</summary>
    public void Load(byte[] content)
    {
        int[] raw = Decode(content, SixteenBit, SignAbove);
        if (raw.Length != m_raw.Length) throw new ArgumentException($"{Name}: got {raw.Length} values, have {m_raw.Length}");
        m_raw = raw;
        m_undo.Clear();
        m_redo.Clear();
        Mutated = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkSaved() => Mutated = false;

    // ---- text, as MapViewerEx shows and parses cells ----

    public string FormatCell(int index, MapViewType view) => Format(m_raw[index], view);

    public string Format(int raw, MapViewType view)
    {
        switch (view)
        {
            case MapViewType.Hex:
                // the old viewer printed negative values as FFFFFFFF, show the 16 bits the ECU gets
                return SixteenBit ? (raw & 0xFFFF).ToString("X4") : (raw & 0xFF).ToString("X2");
            case MapViewType.Ascii:
                return raw < 0 ? " " : ((char)raw).ToString();
            case MapViewType.Easy:
                raw = Scaled(raw);
                if (Factor != 1 || Offset != 0)
                {
                    float v = (float)raw * (float)Factor + (float)Offset;
                    if (Name.StartsWith("Ign_map_0!") || Name.StartsWith("Ign_map_4!")) return v.ToString("F1", CultureInfo.CurrentCulture) + "°";
                    if (Name.StartsWith("Reg_kon_mat")) return v.ToString("F0", CultureInfo.CurrentCulture) + "%";
                    return v.ToString("F2", CultureInfo.CurrentCulture);
                }
                if (Name.StartsWith("Reg_kon_mat")) return raw.ToString("F0", CultureInfo.CurrentCulture) + "%";
                return raw.ToString(CultureInfo.CurrentCulture);
            default:
                return Scaled(raw).ToString(CultureInfo.CurrentCulture);
        }
    }

    /// <summary>
    /// Typed text to a raw value: hex in Hex view, (value-offset)/factor rounded half to even in Easy view, an integer
    /// otherwise. ASCII view is not editable. Values outside the cell's range are rejected.
    /// </summary>
    public bool TryParse(string text, MapViewType view, out int raw, out string error)
    {
        raw = 0;
        error = "";
        text = text.Trim();
        switch (view)
        {
            case MapViewType.Hex:
                if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out raw)) { error = "Not a hex value"; return false; }
                if (raw > SignAbove && raw < Span) raw -= Span; // as the bytes read back
                break;
            case MapViewType.Easy:
                if (!double.TryParse(text.TrimEnd('°', '%'), NumberStyles.Float, CultureInfo.CurrentCulture, out double d)
                    && !double.TryParse(text.TrimEnd('°', '%'), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) { error = "Not a number"; return false; }
                double unscaled = Factor == 0 ? d - Offset : (d - Offset) / Factor;
                // a scaled view rounds up, as T5Suite's 3 bar views did
                raw = ScalePercent == 100 ? (int)Math.Round(unscaled, MidpointRounding.ToEven) : Unscale(unscaled);
                break;
            case MapViewType.Decimal:
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out raw)) { error = "Not an integer"; return false; }
                if (ScalePercent != 100) raw = Unscale(raw);
                break;
            default:
                error = "ASCII view is read-only";
                return false;
        }
        if (raw < MinRaw || raw > MaxRaw) { error = $"Value not valid, {MinRaw}..{MaxRaw}"; return false; }
        return true;
    }

    private int Unscale(double v) => (int)Math.Ceiling(Math.Round(v * 100 / ScalePercent, 6));
}
