using System.Collections.Generic;

namespace T7
{
    /// <summary>A compressor map image and where its axes sit, in pixels at the image's own size: (XOffset, YOffset) is 0 lb/min at pressure ratio 1.</summary>
    public sealed record Compressor(string Name, string Image, double XOffset, double YOffset, double PixelsPerLbMin, double PixelsPerPr);

    /// <summary>
    /// ctrlCompressorMap: the WOT airmass per rpm as operating points on a compressor map, for sea level (14.7 psi), high
    /// altitude (12.5) and high pressure (15.4).
    /// </summary>
    public static class CompressorMap
    {
        public static readonly Compressor[] Compressors =
        [
            new("Garrett T1752", "GT17.jpg", 42, 539, 10.67, 166),
            new("Garrett T25 trim 55", "t25_55_saab.gif", 64, 865, 20, 396),
            new("Garrett T25 trim 60", "t25-60trim.gif", 60, 867, 17.28, 398),
            new("Mitsubishi TD04-15G", "td04-15g-cfm.gif", 66, 576, 10.45, 234.5),
            new("Mitsubishi TD04-16T", "td04h-16t-cfm.gif", 64, 573, 8.27, 233),
            new("Mitsubishi TD04-18T", "td04h-18t-cfm.gif", 65, 576, 8.27, 234),
            new("Mitsubishi TD04-19T", "td04h-19t-cfm.gif", 65, 576, 8.27, 234),
            new("Mitsubishi TD06-20G", "td06h-20g-cfm.gif", 58, 577, 8.30, 235),
            new("Garrett GT2871R", "gt2871r-48.jpg", 50, 595, 9.56, 276.5),
            new("Garrett GT28RS", "gt28rscompress.gif", 55, 460, 8, 211),
            new("Garrett GT3071R", "GT3071R86.jpg", 42, 556, 6.67, 171),
            new("Garrett GT3076R", "gt30rcompress.gif", 50, 463, 6.4, 158),
            new("Garrett GT40R", "gt40rcompress.gif", 54, 482, 5.31, 171),
            new("Holset HX40w", "hx40w.jpg", 35, 762, 5.03, 167),
        ];

        public static readonly double[] AmbientPsi = [14.7, 12.5, 15.4];

        /// <summary>The first guess: the TD04-15G on Aero part numbers, else the T1752; 2.3 litres unless B204 / B205.</summary>
        public static (int compressor, double litres) Defaults(string partNumber)
        {
            ECUInformation ecu = new PartNumberConverter().GetECUInfo(partNumber.Trim(), "");
            return (ecu.Isaero ? 3 : 0, ecu.Is2point3liter ? 2.3 : 2.0);
        }

        /// <summary>
        /// Operating points (lb/min, pressure ratio) per ambient pressure. Air flow = rpm × mg/c × 2 / 1000 g/min; the pressure
        /// ratio is the flow over what the engine swallows at ambient (VE, 1.2041 g/l), corrected to the intake temperature.
        /// </summary>
        public static List<(double lbmin, double pr)>[] Points(IReadOnlyList<int> rpm, IReadOnlyList<int> airmass, double litres, double vePercent, double tempC)
        {
            var curves = new List<(double, double)>[AmbientPsi.Length];
            for (int k = 0; k < curves.Length; k++) curves[k] = [];
            for (int i = 0; i < rpm.Count && i < airmass.Count; i++)
            {
                if (rpm[i] <= 0) continue;
                double ve = vePercent > 0 ? vePercent / 100 : 1 - rpm[i] / 100000.0 * 4;
                double swallowed = rpm[i] * ve * litres * 0.5 * 1.2041 / 453.59237;
                double flow = rpm[i] * airmass[i] * 2 / 1000.0 / 453.59237;
                double massRatio = flow / swallowed;
                for (int k = 0; k < AmbientPsi.Length; k++)
                    curves[k].Add((flow, massRatio * 14.7 / AmbientPsi[k] * (223 + tempC) / 223));
            }
            return curves;
        }
    }
}
