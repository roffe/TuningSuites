using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace MapControls;

public enum SurfaceRenderMode { SolidWireframe, Solid, Wireframe }

/// <summary>
/// 3D surface of a map, ported from txlogger's meshgrid (image backend): one quad per cell on a corner-vertex grid,
/// drawn back to front as two Gouraud triangles folded along the diagonal with the smaller value gap, Lambert shaded per
/// triangle, plus T7Suite-style axis scales along three edges of the bounding box. Left drag orbits, right drag rolls,
/// middle drag pans, the wheel zooms.
/// </summary>
public class Surface3D : Control
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<Surface3D, IBrush?>(nameof(Background), new ImmutableSolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)));

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    private const double CellSize = 32;
    private const double RotationScale = 0.6, RollScale = 0.4, PanScale = 0.8;
    private const double FitBand = 64;
    private const double SurfaceEdgeFade = 0.45;

    // axis scale layout, see SurfaceAxes
    private static readonly Color CursorFill = Color.FromRgb(165, 55, 253);

    private double[] m_values = [0];
    private int m_cols = 1, m_rows = 1;
    private double m_zmin, m_zmax, m_zrange, m_depth;
    private double[]? m_xData, m_yData;
    private int m_xPrec, m_yPrec, m_zPrec;
    private string m_xLabel = "", m_yLabel = "", m_zLabel = "";

    // corner-vertex grid, (rows+1) x (cols+1), row-major
    private double[] m_ox = [], m_oy = [], m_oz = [], m_vx = [], m_vy = [], m_vz = [], m_v = [];
    private double m_cx, m_cy, m_cz;

    private M3 m_camera = M3.Identity;
    private double m_camX, m_camY, m_camZ;
    private double m_scale = 1;
    private bool m_fitted;
    // the user (or a synced viewer) moved the camera: resizes keep that view instead of fitting again
    private bool m_cameraMoved;
    private Size m_size;

    private double m_cursorX, m_cursorY;
    private bool m_showCursor;

    private Point m_lastPointer;
    private readonly SurfaceAxes m_axes;

    public SurfaceRenderMode RenderMode { get; set; } = SurfaceRenderMode.SolidWireframe;

    /// <summary>
    /// Rotation, zoom and pan, to keep viewers of the same map looking the same way (T7Suite's surface view sync). Zoom is
    /// relative to the viewer's own fit and pan a fraction of its size, so viewers of different sizes stay centred.
    /// </summary>
    public readonly record struct CameraState(double[] Rotation, double Zoom, double PanX, double PanY);

    /// <summary>The user rotated, zoomed or panned (raised on release and on each wheel step).</summary>
    public event EventHandler? CameraChanged;

    public CameraState Camera
    {
        get
        {
            double fit = FitScaleForSize(m_size);
            return new([m_camera.M00, m_camera.M01, m_camera.M02, m_camera.M10, m_camera.M11, m_camera.M12, m_camera.M20, m_camera.M21, m_camera.M22],
                fit > 0 ? m_scale / fit : 1, m_size.Width > 0 ? m_camX / m_size.Width : 0, m_size.Height > 0 ? m_camY / m_size.Height : 0);
        }
        set
        {
            double[] r = value.Rotation;
            m_camera = new M3(r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7], r[8]);
            m_cameraMoved = true;
            UpdateVertexPositions();
            if (m_size.Width > 0 && m_size.Height > 0)
            {
                m_scale = value.Zoom * FitScaleForSize(m_size);
                m_camX = value.PanX * m_size.Width;
                m_camY = value.PanY * m_size.Height;
                m_fitted = true;
            }
            UpdateVertexPositions();
            InvalidateVisual();
        }
    }

    /// <summary>T7Suite's online palette (wheat → dark blue) instead of green → red.</summary>
    public bool OnlineMode
    {
        get => m_online;
        set { m_online = value; InvalidateVisual(); }
    }

    private bool m_online;

    public Surface3D()
    {
        m_axes = new SurfaceAxes(this);
        ClipToBounds = true;
        SetData([0], 1, 1, null, null, "", "", "", 0, 0, 0);
    }

    // value range and size, read by SurfaceAxes
    internal int Cols => m_cols;
    internal int Rows => m_rows;
    internal double Depth => m_depth;
    internal double ZMin => m_zmin;
    internal double ZRange => m_zrange;
    internal double[]? XData => m_xData;
    internal double[]? YData => m_yData;
    internal int XPrec => m_xPrec;
    internal int YPrec => m_yPrec;
    internal int ZPrec => m_zPrec;
    internal string XLabel => m_xLabel;
    internal string YLabel => m_yLabel;
    internal string ZLabel => m_zLabel;
    internal Size ViewSize => m_size;

    /// <summary>
    /// Values are row-major, row 0 is the bottom row of the map (drawn at the far end like the table's last row).
    /// xData/yData hold one axis value per column/row for the scales and may be null.
    /// </summary>
    public void SetData(double[] values, int cols, int rows, double[]? xData, double[]? yData,
        string xLabel, string yLabel, string zLabel, int xPrec, int yPrec, int zPrec)
    {
        cols = Math.Max(1, cols);
        rows = Math.Max(1, rows);
        if (values.Length != cols * rows)
            throw new ArgumentException($"got {values.Length} values for {cols}x{rows}");

        bool reshaped = cols != m_cols || rows != m_rows;
        m_values = values;
        m_cols = cols;
        m_rows = rows;
        m_xData = xData;
        m_yData = yData;
        m_xLabel = xLabel;
        m_yLabel = yLabel;
        m_zLabel = zLabel;
        m_xPrec = xPrec;
        m_yPrec = yPrec;
        m_zPrec = zPrec;
        // height follows the footprint so a small map isn't drawn as a tower
        m_depth = 0.75 * Math.Max(cols, rows) * CellSize;
        (m_zmin, m_zmax) = MinMax(values);
        m_zrange = m_zmax - m_zmin;
        m_axes.Reset();
        CreateVertices();

        if (reshaped || m_ox.Length == 0)
        {
            m_scale = 0.3;
            m_camera = M3.Identity;
            m_camX = m_camY = m_camZ = 0;
            m_fitted = false;
            m_cameraMoved = false;
            if (cols == 1)
            {
                m_camera = M3.Rotation(0, 90, 0) * m_camera;
            }
            else
            {
                // T7Suite-style starting view: ~30° elevation with the mesh spun 35° around its vertical axis
                m_camera = M3.RotX(60) * m_camera * M3.RotZ(-35);
            }
        }
        UpdateVertexPositions();
        if (!m_fitted && m_size.Width > 0) AdaptZoom(default, m_size);
        InvalidateVisual();
    }

    /// <summary>Values changed but not the shape: keeps the camera.</summary>
    public void SetValues(double[] values)
    {
        if (values.Length != m_cols * m_rows) throw new ArgumentException("shape changed, use SetData");
        m_values = values;
        (m_zmin, m_zmax) = MinMax(values);
        m_zrange = m_zmax - m_zmin;
        CreateVertices();
        UpdateVertexPositions();
        InvalidateVisual();
    }

    /// <summary>Live tracking marker at a fractional cell index, interpolated on the surface.</summary>
    public void SetCursor(double xIdx, double yIdx)
    {
        xIdx = Math.Clamp(xIdx, 0, m_cols - 1);
        yIdx = Math.Clamp(yIdx, 0, m_rows - 1);
        if (m_showCursor && xIdx == m_cursorX && yIdx == m_cursorY) return;
        m_cursorX = xIdx;
        m_cursorY = yIdx;
        m_showCursor = true;
        InvalidateVisual();
    }

    public void HideCursor()
    {
        m_showCursor = false;
        InvalidateVisual();
    }

    private static (double, double) MinMax(double[] values)
    {
        double min = values[0], max = values[0];
        foreach (double v in values)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }
        return (min, max);
    }

    // one quad per cell; each corner takes the average of the 1-4 cell values touching it
    private void CreateVertices()
    {
        double zrange = m_zrange == 0 ? 1 : m_zrange;
        int vRows = m_rows + 1, vCols = m_cols + 1, n = vRows * vCols;
        if (m_ox.Length != n)
        {
            m_ox = new double[n]; m_oy = new double[n]; m_oz = new double[n];
            m_vx = new double[n]; m_vy = new double[n]; m_vz = new double[n]; m_v = new double[n];
        }
        double sx = 0, sy = 0, sz = 0;
        for (int i = 0; i < vRows; i++)
        {
            for (int j = 0; j < vCols; j++)
            {
                int k = i * vCols + j;
                double value = CornerValue(i, j);
                m_ox[k] = j * CellSize;
                // data row 0 is the bottom row of the map, keep it at the high-Y end of the mesh
                m_oy[k] = (vRows - i) * CellSize;
                m_oz[k] = (value - m_zmin) / zrange * m_depth;
                m_v[k] = value;
                sx += m_ox[k]; sy += m_oy[k]; sz += m_oz[k];
            }
        }
        m_cx = sx / n; m_cy = sy / n; m_cz = sz / n;
    }

    private double CornerValue(int vi, int vj)
    {
        int r0 = Math.Max(vi - 1, 0), r1 = Math.Min(vi, m_rows - 1);
        int c0 = Math.Max(vj - 1, 0), c1 = Math.Min(vj, m_cols - 1);
        double sum = 0;
        int n = 0;
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                sum += m_values[r * m_cols + c];
                n++;
            }
        return sum / n;
    }

    private void UpdateVertexPositions()
    {
        for (int k = 0; k < m_ox.Length; k++)
        {
            (m_vx[k], m_vy[k], m_vz[k]) = ViewPos(m_ox[k], m_oy[k], m_oz[k]);
        }
    }

    internal (double x, double y, double z) ViewPos(double ox, double oy, double oz)
    {
        double vx = (ox - m_cx) * m_scale, vy = (oy - m_cy) * m_scale, vz = (oz - m_cz) * m_scale;
        M3 r = m_camera;
        return (r.M00 * vx + r.M01 * vy + r.M02 * vz - m_camX,
                r.M10 * vx + r.M11 * vy + r.M12 * vz - m_camY,
                r.M20 * vx + r.M21 * vy + r.M22 * vz - m_camZ);
    }

    /// <summary>Original (untransformed) mesh coordinates to control pixels.</summary>
    internal Point Project(double ox, double oy, double oz)
    {
        var (x, y, _) = ViewPos(ox, oy, oz);
        return new Point(m_size.Width * 0.5 + x, m_size.Height * 0.5 + y);
    }

    // what is drawn, in view space: the surface plus the box edges the axis scales ride on
    private (double minX, double maxX, double minY, double maxY) ProjectedBounds()
    {
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        void Grow(double x, double y)
        {
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        for (int k = 0; k < m_vx.Length; k++) Grow(m_vx[k], m_vy[k]);
        var (floor, _, zIdx) = m_axes.Corners();
        foreach (var c in floor)
        {
            var (x, y, _) = ViewPos(c.X, c.Y, 0);
            Grow(x, y);
        }
        var (tx, ty, _) = ViewPos(floor[zIdx].X, floor[zIdx].Y, m_depth);
        Grow(tx, ty);
        return (minX, maxX, minY, maxY);
    }

    /// <summary>What is drawn, in control pixels (tests).</summary>
    internal Rect DrawnBounds()
    {
        var (minX, maxX, minY, maxY) = ProjectedBounds();
        return new Rect(m_size.Width * 0.5 + minX, m_size.Height * 0.5 + minY, maxX - minX, maxY - minY);
    }

    private void CenterInView()
    {
        var (minX, maxX, minY, maxY) = ProjectedBounds();
        if (double.IsInfinity(minX)) return;
        m_camX += (minX + maxX) / 2;
        m_camY += (minY + maxY) / 2;
        UpdateVertexPositions();
    }

    private double FitScaleForSize(Size size)
    {
        var (minX, maxX, minY, maxY) = ProjectedBounds();
        double w = maxX - minX, h = maxY - minY;
        if (double.IsInfinity(minX) || w <= 0 || h <= 0 || m_scale == 0) return m_scale;
        // a pane too small for the bands still gives the mesh half of itself
        double sx = Math.Max(size.Width - 2 * FitBand, size.Width / 2) / (w / m_scale);
        double sy = Math.Max(size.Height - 2 * FitBand, size.Height / 2) / (h / m_scale);
        return Math.Min(sx, sy);
    }

    // fit and center on the first real size, afterwards scale zoom and pan with the size so the user's zoom is kept
    private void AdaptZoom(Size oldSize, Size newSize)
    {
        if (newSize.Width <= 0 || newSize.Height <= 0) return;
        if (!m_fitted || !m_cameraMoved)
        {
            // untouched view: fit and centre for the new size (scaling the old fit drifted off centre and clipped the scales)
            m_camX = m_camY = 0;
            UpdateVertexPositions();
            m_scale = FitScaleForSize(newSize);
            m_fitted = true;
            UpdateVertexPositions();
            CenterInView();
            return;
        }
        if (oldSize.Width <= 0 || oldSize.Height <= 0) return;
        double oldFit = FitScaleForSize(oldSize);
        if (oldFit == 0) return;
        double ratio = FitScaleForSize(newSize) / oldFit;
        if (ratio <= 0 || double.IsInfinity(ratio) || double.IsNaN(ratio)) return;
        m_scale *= ratio;
        m_camX *= ratio;
        m_camY *= ratio;
        UpdateVertexPositions();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // a collapsed pane lays us out at zero, keep the last good size so zoom stays reversible
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        Size old = m_size;
        m_size = e.NewSize;
        AdaptZoom(old, m_size);
        InvalidateVisual();
    }

    // turntable orbit: spin around the mesh's vertical axis (model space), pitch around the camera X axis
    private void Orbit(double spin, double pitch)
    {
        m_cameraMoved = true;
        m_camera = M3.RotX(pitch) * m_camera * M3.RotZ(spin);
        UpdateVertexPositions();
    }

    private void Roll(double roll)
    {
        m_cameraMoved = true;
        m_camera = M3.RotZ(roll) * m_camera;
        UpdateVertexPositions();
    }

    private void Pan(double dx, double dy)
    {
        m_cameraMoved = true;
        m_camX -= dx * PanScale;
        m_camY -= dy * PanScale;
        UpdateVertexPositions();
    }

    private void Zoom(double factor)
    {
        m_cameraMoved = true;
        m_scale *= factor;
        UpdateVertexPositions();
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        m_lastPointer = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        CameraChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point p = e.GetPosition(this);
        double dx = p.X - m_lastPointer.X, dy = p.Y - m_lastPointer.Y;
        m_lastPointer = p;
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed)
        {
            // drag left spins clockwise, drag up tilts backwards
            Orbit(-dx * RotationScale, -dy * RotationScale);
        }
        else if (props.IsRightButtonPressed)
        {
            Roll((dx + dy) * RollScale);
        }
        else if (props.IsMiddleButtonPressed)
        {
            Pan(dx * PanScale, dy * PanScale);
        }
        else
        {
            return;
        }
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Zoom(e.Delta.Y > 0 ? 1.1 : 0.9);
        e.Handled = true;
        CameraChanged?.Invoke(this, EventArgs.Empty);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (Background != null) context.FillRectangle(Background, bounds);
        if (m_size.Width <= 0 || m_size.Height <= 0) return;

        context.Custom(new SurfaceDrawOperation(bounds, BuildCells()));
        m_axes.Draw(context);

        if (m_showCursor)
        {
            Point c = CursorScreenPosition();
            context.DrawEllipse(new SolidColorBrush(CursorFill), new Pen(Brushes.White, 2), c, 6, 6);
        }
    }

    /// <summary>The geometry of one frame, captured on the UI thread for the render thread.</summary>
    internal sealed class Cells
    {
        public required SKPoint[] Points;   // 6 per cell (two triangles), painter's order
        public required SKColor[] Colors;   // per point
        public required SKColor[] Edges;    // 4 per cell, or empty without wireframe
        public required SKPoint[] Corners;  // 4 per cell, a b c d, for the outline
        public required bool Fill;
    }

    private Cells BuildCells()
    {
        int vCols = m_cols + 1, n = m_vx.Length;

        // view-space depth range for the depth shading
        double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
        for (int k = 0; k < n; k++)
        {
            minZ = Math.Min(minZ, m_vz[k]);
            maxZ = Math.Max(maxZ, m_vz[k]);
        }
        double zRange = maxZ - minZ;
        if (zRange == 0) zRange = 1;

        var proj = new SKPoint[n];
        var col = new SKColor[n];
        float hx = (float)(m_size.Width * 0.5), hy = (float)(m_size.Height * 0.5);
        for (int k = 0; k < n; k++)
        {
            proj[k] = new SKPoint(hx + (float)m_vx[k], hy + (float)m_vy[k]);
            col[k] = ColorWithDepth(m_v[k], (m_vz[k] - minZ) / zRange);
        }

        // back to front: larger view-space Z is nearer the viewer
        int cells = m_rows * m_cols;
        var order = new int[cells];
        var depth = new double[cells];
        for (int i = 0; i < m_rows; i++)
            for (int j = 0; j < m_cols; j++)
            {
                int q = i * m_cols + j, a = i * vCols + j;
                order[q] = q;
                depth[q] = (m_vz[a] + m_vz[a + 1] + m_vz[a + vCols] + m_vz[a + vCols + 1]) * 0.25;
            }
        Array.Sort(depth, order);

        // fixed light direction in view space
        double lx = 0.3, ly = -0.5, lz = 0.8, il = 1 / Math.Sqrt(lx * lx + ly * ly + lz * lz);
        lx *= il; ly *= il; lz *= il;

        bool fill = RenderMode != SurfaceRenderMode.Wireframe;
        bool edges = RenderMode != SurfaceRenderMode.Solid;
        var points = new SKPoint[fill ? cells * 6 : 0];
        var colors = new SKColor[points.Length];
        var corners = new SKPoint[cells * 4];
        var edgeColors = new SKColor[edges ? cells * 4 : 0];
        int p = 0;
        for (int o = 0; o < cells; o++)
        {
            int q = order[o], i = q / m_cols, j = q % m_cols;
            int a = i * vCols + j, b = a + 1, d = a + vCols, c = d + 1;
            corners[o * 4] = proj[a]; corners[o * 4 + 1] = proj[b]; corners[o * 4 + 2] = proj[c]; corners[o * 4 + 3] = proj[d];
            if (edges)
            {
                // fill and wireframe: dimmed so the grid reads as a grid; wireframe only: full colour
                double fade = fill ? SurfaceEdgeFade : 1;
                edgeColors[o * 4] = Fade(col[a], fade); edgeColors[o * 4 + 1] = Fade(col[b], fade);
                edgeColors[o * 4 + 2] = Fade(col[c], fade); edgeColors[o * 4 + 3] = Fade(col[d], fade);
            }
            if (!fill) continue;

            // fold along the diagonal with the smaller corner-value gap so an outlier sits in one sloping triangle
            if (Math.Abs(m_v[a] - m_v[c]) <= Math.Abs(m_v[b] - m_v[d]))
            {
                double s1 = TriShade(a, b, c, lx, ly, lz), s2 = TriShade(a, c, d, lx, ly, lz);
                Tri(a, b, c, s1); Tri(a, c, d, s2);
            }
            else
            {
                double s1 = TriShade(a, b, d, lx, ly, lz), s2 = TriShade(b, c, d, lx, ly, lz);
                Tri(a, b, d, s1); Tri(b, c, d, s2);
            }
        }
        return new Cells { Points = points, Colors = colors, Edges = edgeColors, Corners = corners, Fill = fill };

        void Tri(int v0, int v1, int v2, double shade)
        {
            points[p] = proj[v0]; colors[p++] = Fade(col[v0], shade);
            points[p] = proj[v1]; colors[p++] = Fade(col[v1], shade);
            points[p] = proj[v2]; colors[p++] = Fade(col[v2], shade);
        }
    }

    // flat Lambert term from the triangle's view-space normal; abs since the surface may be seen from below
    private double TriShade(int a, int b, int c, double lx, double ly, double lz)
    {
        double ux = m_vx[b] - m_vx[a], uy = m_vy[b] - m_vy[a], uz = m_vz[b] - m_vz[a];
        double vx = m_vx[c] - m_vx[a], vy = m_vy[c] - m_vy[a], vz = m_vz[c] - m_vz[a];
        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (nl == 0) return 1;
        return 0.6 + 0.4 * Math.Abs((nx * lx + ny * ly + nz * lz) / nl);
    }

    private SKColor ColorWithDepth(double value, double depth)
    {
        Color baseColor = HeatColor.Interpolate(m_zmin, m_zmax, value, m_online);
        double f = 0.6 + 0.4 * depth;
        double haze = (1 - depth) * 15; // slight blue tint on distant parts
        byte r = (byte)(baseColor.R * f), g = (byte)(baseColor.G * f);
        byte b = (byte)Math.Min(255, (int)(baseColor.B * f + haze));
        return new SKColor(r, g, b, baseColor.A);
    }

    private static SKColor Fade(SKColor c, double f) =>
        new((byte)(c.Red * f), (byte)(c.Green * f), (byte)(c.Blue * f), c.Alpha);

    // the marker sits mid-cell on the corner grid, bilinear over the transformed corners
    private Point CursorScreenPosition()
    {
        int vCols = m_cols + 1;
        double sx = m_cursorX + 0.5, sy = m_cursorY + 0.5;
        int x0 = (int)sx, y0 = (int)sy, x1 = Math.Min(x0 + 1, m_cols), y1 = Math.Min(y0 + 1, m_rows);
        double fx = sx - x0, fy = sy - y0;
        int k00 = y0 * vCols + x0, k01 = y0 * vCols + x1, k10 = y1 * vCols + x0, k11 = y1 * vCols + x1;
        double vx = (1 - fy) * ((1 - fx) * m_vx[k00] + fx * m_vx[k01]) + fy * ((1 - fx) * m_vx[k10] + fx * m_vx[k11]);
        double vy = (1 - fy) * ((1 - fx) * m_vy[k00] + fx * m_vy[k01]) + fy * ((1 - fx) * m_vy[k10] + fx * m_vy[k11]);
        return new Point(m_size.Width * 0.5 + vx, m_size.Height * 0.5 + vy);
    }

    private sealed class SurfaceDrawOperation(Rect bounds, Cells cells) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease == null) return;
            using var api = lease.Lease();
            SKCanvas canvas = api.SkCanvas;
            // vertex colours are modulated with the paint colour, white keeps them as they are
            using var fill = new SKPaint { IsAntialias = false, Color = SKColors.White };
            using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            int cellCount = cells.Corners.Length / 4;
            var tri = new SKPoint[6];
            var triColors = new SKColor[6];
            for (int o = 0; o < cellCount; o++)
            {
                if (cells.Fill)
                {
                    Array.Copy(cells.Points, o * 6, tri, 0, 6);
                    Array.Copy(cells.Colors, o * 6, triColors, 0, 6);
                    canvas.DrawVertices(SKVertexMode.Triangles, tri, null, triColors, fill);
                }
                // the outline goes right after its cell so nearer cells hide it
                if (cells.Edges.Length > 0)
                {
                    for (int e = 0; e < 4; e++)
                    {
                        SKPoint p0 = cells.Corners[o * 4 + e], p1 = cells.Corners[o * 4 + (e + 1) % 4];
                        SKColor c0 = cells.Edges[o * 4 + e], c1 = cells.Edges[o * 4 + (e + 1) % 4];
                        line.Color = new SKColor((byte)((c0.Red + c1.Red) / 2), (byte)((c0.Green + c1.Green) / 2), (byte)((c0.Blue + c1.Blue) / 2));
                        canvas.DrawLine(p0, p1, line);
                    }
                }
            }
        }
    }
}

/// <summary>3x3 rotation matrix, row-major.</summary>
internal readonly record struct M3(double M00, double M01, double M02, double M10, double M11, double M12, double M20, double M21, double M22)
{
    public static readonly M3 Identity = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    public static M3 operator *(M3 a, M3 b) => new(
        a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20, a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21, a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
        a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20, a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21, a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
        a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20, a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21, a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22);

    public static M3 RotX(double deg)
    {
        double r = deg * Math.PI / 180, s = Math.Sin(r), c = Math.Cos(r);
        return new(1, 0, 0, 0, c, -s, 0, s, c);
    }

    public static M3 RotY(double deg)
    {
        double r = deg * Math.PI / 180, s = Math.Sin(r), c = Math.Cos(r);
        return new(c, 0, s, 0, 1, 0, -s, 0, c);
    }

    public static M3 RotZ(double deg)
    {
        double r = deg * Math.PI / 180, s = Math.Sin(r), c = Math.Cos(r);
        return new(c, -s, 0, s, c, 0, 0, 0, 1);
    }

    public static M3 Rotation(double pitch, double yaw, double roll) => RotX(pitch) * RotY(yaw) * RotZ(roll);
}
