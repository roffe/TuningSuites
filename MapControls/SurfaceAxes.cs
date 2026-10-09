using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace MapControls;

/// <summary>
/// T7Suite-style axis scales for <see cref="Surface3D"/>, ported from txlogger's meshgrid_axis.go: the X (column), Y (row)
/// and Z (value) scales ride three edges of the mesh's bounding box, labelled with the table's real axis values and thinned
/// so they don't overlap. The X/Y scales sit on the front-most floor corner, Z on the most side-on remaining corner; both
/// picks have hysteresis so the scales don't bounce between edges while dragging.
/// </summary>
internal sealed class SurfaceAxes(Surface3D mesh)
{
    private const double TextSize = 11;
    private const double CharW = 7.0;           // rough glyph width, only to decide how many labels fit
    private const double EdgeOffset = 8.0;      // lifts the whole scale off the mesh edge
    private const double TickLen = 7.0;
    private const double LabelGap = 4.0;
    private const double NameGap = 12.0;
    private const int ZDivisions = 5;
    private const double ShallowSin = 0.12;     // X/Y floor edges this close to collinear overprint each other
    private const double BandLift = 56.0;
    private const double MinEdgePx = 24.0;      // shorter edges get no ticks, their labels would stack on one spot
    private const double CornerHysteresis = 24.0;
    private const double CellSize = 32;

    // light on the dark background, deeper shades of the same hues on the light one
    private static readonly (IBrush x, IBrush y, IBrush z) DarkBrushes = (Solid(255, 90, 90), Solid(90, 220, 90), Solid(120, 170, 255));
    private static readonly (IBrush x, IBrush y, IBrush z) LightBrushes = (Solid(200, 30, 30), Solid(20, 130, 20), Solid(30, 80, 210));

    private static IBrush Solid(byte r, byte g, byte b) => new ImmutableSolidColorBrush(Color.FromRgb(r, g, b));

    private int m_frontCorner = -1, m_zCorner = -1;

    public void Reset() => m_frontCorner = m_zCorner = -1;

    public (Point[] floor, int frontIdx, int zIdx) Corners()
    {
        double xMax = mesh.Cols * CellSize, yMin = CellSize, yMax = (mesh.Rows + 1) * CellSize;
        Point[] floor = [new(0, yMin), new(xMax, yMin), new(0, yMax), new(xMax, yMax)];
        var screen = new Point[4];
        double frontY = double.NegativeInfinity, meanX = 0;
        int frontIdx = 0;
        for (int i = 0; i < 4; i++)
        {
            screen[i] = mesh.Project(floor[i].X, floor[i].Y, 0);
            meanX += screen[i].X * 0.25;
            if (screen[i].Y > frontY) { frontY = screen[i].Y; frontIdx = i; }
        }
        int p = m_frontCorner;
        if (p >= 0 && p != frontIdx && screen[p].Y > frontY - CornerHysteresis) frontIdx = p;
        m_frontCorner = frontIdx;

        // Z rides the silhouette: the remaining corner farthest from the floor's screen centre
        int zIdx = -1;
        double zDist = -1;
        for (int i = 0; i < 4; i++)
        {
            if (i == frontIdx) continue;
            double d = Math.Abs(screen[i].X - meanX);
            if (d > zDist) { zDist = d; zIdx = i; }
        }
        p = m_zCorner;
        if (p >= 0 && p != frontIdx && p != zIdx && Math.Abs(screen[p].X - meanX) > zDist - CornerHysteresis) zIdx = p;
        m_zCorner = zIdx;
        return (floor, frontIdx, zIdx);
    }

    public void Draw(DrawingContext context)
    {
        double xMax = mesh.Cols * CellSize, yMin = CellSize, yMax = (mesh.Rows + 1) * CellSize, zTop = mesh.Depth;
        var (floor, frontIdx, zIdx) = Corners();
        Point front = floor[frontIdx], zCorner = floor[zIdx];

        // "inside" reference: screen centroid of the eight box corners, labels are pushed away from it
        double sumX = 0, sumY = 0;
        foreach (double oz in new[] { 0, zTop })
            foreach (Point c in floor)
            {
                Point s = mesh.Project(c.X, c.Y, oz);
                sumX += s.X; sumY += s.Y;
            }
        var inside = new Point(sumX / 8, sumY / 8);

        // seen at a shallow angle the X and Y edges project onto one line; lift the shorter one past the other's labels
        double liftX = 0, liftY = 0;
        {
            Point x0 = mesh.Project(0, front.Y, 0), x1 = mesh.Project(xMax, front.Y, 0);
            Point u0 = mesh.Project(front.X, yMin, 0), u1 = mesh.Project(front.X, yMax, 0);
            double xdx = x1.X - x0.X, xdy = x1.Y - x0.Y, ydx = u1.X - u0.X, ydy = u1.Y - u0.Y;
            double xL = Math.Sqrt(xdx * xdx + xdy * xdy), yL = Math.Sqrt(ydx * ydx + ydy * ydy);
            if (xL > 0 && yL > 0)
            {
                double s = Math.Abs(xdx * ydy - xdy * ydx) / (xL * yL);
                if (s < ShallowSin)
                {
                    double lift = (1 - s / ShallowSin) * BandLift;
                    if (xL < yL) liftX = lift; else liftY = lift;
                }
            }
        }

        int cols = mesh.Cols, rows = mesh.Rows;
        var (xBrush, yBrush, zBrush) = mesh.IsDark ? DarkBrushes : LightBrushes;
        string[] xLabels = Labels(mesh.XData, cols, mesh.XPrec);
        Axis(context, inside, xBrush, liftX, false, new(0, front.Y, 0), new(xMax, front.Y, 0), mesh.XLabel, xLabels,
            k => new((k + 0.5) * CellSize, front.Y, 0));

        // data row 0 sits at the high-Y (far) end, so row k maps to Oy = (rows+0.5-k)*cell
        string[] yLabels = Labels(mesh.YData, rows, mesh.YPrec);
        Axis(context, inside, yBrush, liftY, false, new(front.X, yMin, 0), new(front.X, yMax, 0), mesh.YLabel, yLabels,
            k => new(front.X, (rows + 0.5 - k) * CellSize, 0));

        string[] zLabels = [];
        if (mesh.ZRange > 0 && zTop > 0)
        {
            zLabels = new string[ZDivisions + 1];
            for (int k = 0; k <= ZDivisions; k++)
                zLabels[k] = (mesh.ZMin + (double)k / ZDivisions * mesh.ZRange).ToString("F" + mesh.ZPrec, CultureInfo.InvariantCulture);
        }
        Axis(context, inside, zBrush, 0, true, new(zCorner.X, zCorner.Y, 0), new(zCorner.X, zCorner.Y, zTop), mesh.ZLabel, zLabels,
            k => new(zCorner.X, zCorner.Y, (double)k / ZDivisions * zTop));
    }

    private static string[] Labels(double[]? data, int n, int prec)
    {
        if (data == null || data.Length < n) return [];
        var labels = new string[n];
        for (int i = 0; i < n; i++) labels[i] = data[i].ToString("F" + prec, CultureInfo.InvariantCulture);
        return labels;
    }

    private readonly record struct P3(double X, double Y, double Z);

    // one labelled axis: the edge from p0 to p1 lifted outward, the name, and a thinned set of ticks with value labels
    private void Axis(DrawingContext context, Point inside, IBrush brush, double lift, bool nameAtEnd,
        P3 p0, P3 p1, string name, string[] vals, Func<int, P3> pointAt)
    {
        var pen = new Pen(brush, 1);
        Point s0 = mesh.Project(p0.X, p0.Y, p0.Z), s1 = mesh.Project(p1.X, p1.Y, p1.Z);
        var (nx, ny) = OutwardNormal(s0, s1, inside);
        double ox = nx * (EdgeOffset + lift), oy = ny * (EdgeOffset + lift);
        var e0 = new Point(s0.X + ox, s0.Y + oy);
        var e1 = new Point(s1.X + ox, s1.Y + oy);
        context.DrawLine(pen, e0, e1);

        double L = Math.Sqrt((e1.X - e0.X) * (e1.X - e0.X) + (e1.Y - e0.Y) * (e1.Y - e0.Y));
        int n = vals.Length, maxChars = 1;
        foreach (string v in vals) maxChars = Math.Max(maxChars, v.Length);

        if (name != "" && nameAtEnd)
        {
            double ux = nx, uy = ny;
            if (L > 1e-6) { ux = (e1.X - e0.X) / L; uy = (e1.Y - e0.Y) / L; }
            double d = NameGap + TextExtent(ux, uy, name.Length);
            Text(context, name, e1.X + ux * d, e1.Y + uy * d, brush);
        }
        else if (name != "")
        {
            // on the edge midpoint, past the value-label band
            double mx = (s0.X + s1.X) * 0.5, my = (s0.Y + s1.Y) * 0.5;
            double dist = lift + EdgeOffset + TickLen + 2 * LabelGap + 2 * TextExtent(nx, ny, maxChars) + TextExtent(nx, ny, name.Length);
            Text(context, name, mx + nx * dist, my + ny * dist, brush);
        }
        if (n <= 0 || L < MinEdgePx) return;

        double minSpacing = maxChars * CharW + 8;
        int step = LabelStep(n, L, minSpacing);

        void Tick(int k)
        {
            P3 p = pointAt(k);
            Point s = mesh.Project(p.X, p.Y, p.Z);
            double bx = s.X + ox, by = s.Y + oy;
            context.DrawLine(pen, new Point(bx, by), new Point(bx + nx * TickLen, by + ny * TickLen));
            double d = TickLen + LabelGap + TextExtent(nx, ny, vals[k].Length);
            Text(context, vals[k], bx + nx * d, by + ny * d, brush);
        }

        // the last tick is always labelled; drop the stepped neighbour if the two would overprint
        int last = n - 1, prev = last / step * step;
        bool skipPrev = last != prev && (last - prev) * (L / n) < minSpacing;
        for (int k = 0; k < n; k += step)
        {
            if (k == prev && skipPrev) continue;
            Tick(k);
        }
        if (last != prev) Tick(last);
    }

    internal static int LabelStep(int n, double L, double minSpacing)
    {
        if (n <= 1 || L <= 0) return 1;
        int fit = Math.Max(1, (int)(L / minSpacing));
        return Math.Max(1, (n + fit - 1) / fit);
    }

    // half extent of a horizontal text box along the unit direction: how far its centre must sit to clear a point
    private static double TextExtent(double dx, double dy, int chars) =>
        Math.Abs(dx) * chars * CharW / 2 + Math.Abs(dy) * TextSize / 2;

    private static (double, double) OutwardNormal(Point s0, Point s1, Point inside)
    {
        double dx = s1.X - s0.X, dy = s1.Y - s0.Y, L = Math.Sqrt(dx * dx + dy * dy);
        if (L < 1e-6)
        {
            double rx = s0.X - inside.X, ry = s0.Y - inside.Y, d = Math.Sqrt(rx * rx + ry * ry);
            return d < 1e-3 ? (0, 1) : (rx / d, ry / d);
        }
        double nx = -dy / L, ny = dx / L;
        double mx = (s0.X + s1.X) * 0.5, my = (s0.Y + s1.Y) * 0.5;
        if (nx * (mx - inside.X) + ny * (my - inside.Y) < 0) { nx = -nx; ny = -ny; }
        return (nx, ny);
    }

    private static void Text(DrawingContext context, string text, double cx, double cy, IBrush brush)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, TextSize, brush);
        context.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
    }
}
