using System;
using System.Collections.Generic;
using System.Linq;

namespace T7
{
    public enum MatrixMode { Mean, Minimum, Maximum }

    /// <summary>
    /// View matrix from logfile: Z over a 16 × 16 grid of X and Y, both axes split evenly between their logged minimum and
    /// maximum, every line's last seen values into the nearest cell. Lines count only once all three symbols have been seen
    /// (T7Suite also counted the zeros before that), and empty cells stay empty (T7Suite used 0 for "empty", so a real 0 lost
    /// to the next value in min / max).
    /// </summary>
    public sealed class LogMatrix
    {
        public const int Size = 16;
        public double[] X { get; } = new double[Size];
        public double[] Y { get; } = new double[Size];
        /// <summary>[y, x], NaN where nothing was logged.</summary>
        public double[,] Values { get; } = new double[Size, Size];

        private LogMatrix() { }

        /// <summary>Null when X or Y never changes (T7Suite: "x or y axis contains no differentiated values").</summary>
        public static LogMatrix Build(IEnumerable<T7LogLine> lines, string x, string y, string z, MatrixMode mode)
        {
            var points = new List<(double x, double y, double z)>();
            double? hx = null, hy = null, hz = null;
            foreach (T7LogLine line in lines)
            {
                hx = line[x] ?? hx;
                hy = line[y] ?? hy;
                hz = line[z] ?? hz;
                if (hx is { } px && hy is { } py && hz is { } pz) points.Add((px, py, pz));
            }
            if (points.Count == 0) return null;
            double xmin = points.Min(p => p.x), xmax = points.Max(p => p.x), ymin = points.Min(p => p.y), ymax = points.Max(p => p.y);
            if (xmin == xmax || ymin == ymax) return null;
            var m = new LogMatrix();
            for (int i = 0; i < Size; i++)
            {
                m.X[i] = xmin + i * (xmax - xmin) / (Size - 1);
                m.Y[i] = ymin + i * (ymax - ymin) / (Size - 1);
            }
            var count = new int[Size, Size];
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++) m.Values[r, c] = double.NaN;
            foreach (var (px, py, pz) in points)
            {
                int c = Nearest(m.X, px), r = Nearest(m.Y, py);
                double cur = m.Values[r, c];
                m.Values[r, c] = count[r, c] == 0 ? pz : mode switch
                {
                    MatrixMode.Minimum => Math.Min(cur, pz),
                    MatrixMode.Maximum => Math.Max(cur, pz),
                    _ => (cur * count[r, c] + pz) / (count[r, c] + 1),
                };
                count[r, c]++;
            }
            return m;
        }

        // the nearest breakpoint, ties to the lower one
        private static int Nearest(double[] axis, double v)
        {
            int best = 0;
            for (int i = 1; i < axis.Length; i++)
                if (Math.Abs(axis[i] - v) < Math.Abs(axis[best] - v)) best = i;
            return best;
        }
    }
}
