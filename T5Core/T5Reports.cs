using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace Trionic5Tools
{
    /// <summary>The Actions page's reports (TuningReport in T5Suite), as plain lines.</summary>
    public static class T5Reports
    {
        /// <summary>Check for anomalies (Trionic5File.CheckForAnomalies): "Anomaly report".</summary>
        public static List<string> Anomalies(T5Binary bin)
        {
            try
            {
                return bin.File.CheckForAnomalies().Rows.Cast<DataRow>().Select(r => r[0]?.ToString() ?? "").ToList();
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException)
            {
                // the boost limiter check had no bounds check: a file with odd table sizes threw in T5Suite
                return ["Checking file " + Path.GetFileName(bin.FileName), "The check stopped, the file's tables don't have the expected sizes: " + e.Message];
            }
        }

        private static readonly string[][] Requires =
        [
            ["3'' turboback exhaust", "BCPR7ES plugs"],
            ["3'' turboback exhaust", "upgraded intercooler", "BCPR7ES plugs"],
            ["3'' turboback exhaust", "upgraded intercooler", "upgraded pressureplate", "GT28 turbo or better", "BCPR7ES plugs"],
            ["3'' turboback exhaust", "upgraded intercooler", "upgraded pressureplate", "upgraded fuelpump", "wideband lambda", "EGT gauge", "GT3071r .64 turbo or better", "BCPR8ES plugs"],
            ["3'' turboback exhaust", "upgraded intercooler", "upgraded pressureplate", "upgraded fuelpump", "wideband lambda", "EGT gauge", "GT3071r .86 turbo or better", "BCPR8ES plugs"],
            ["3'' turboback exhaust", "upgraded intercooler", "upgraded pressureplate", "upgraded fuelpump", "tubular exhaust manifold", "wideband lambda", "EGT gauge", "HX40 hybrid (super) turbo or better", "BCPR8ES plugs"],
        ];

        private static int SensorPercent(MapSensorType t) => t switch
        {
            MapSensorType.MapSensor30 => 120,
            MapSensorType.MapSensor35 => 140,
            MapSensorType.MapSensor40 => 160,
            MapSensorType.MapSensor50 => 200,
            _ => 100,
        };

        /// <summary>What T5Suite guessed about a file (Examine binary, the user library): its stage and peak boost, sensor, fuel, injectors, valve.</summary>
        public sealed record FileGuess(TuningStage Stage, float MaxBoost, MapSensorType Sensor, bool E85, InjectorType Injectors, bool T5Valve);

        /// <summary>
        /// The stage from the boost request peak, the sensor from the marker or the maps, E85 from the injection peak, Inj_konst! or
        /// the afterstart factor, the injectors from the injection peak corrected for the sensor and fuel, the valve from Frek_230! / Frek_250!.
        /// </summary>
        public static FileGuess Guess(T5Binary bin)
        {
            Trionic5File f = bin.File;
            MapSensorType sensor = f.GetMapSensorType(true);
            TuningStage stage = f.DetermineTuningStage(out float maxBoost);
            int injkonst = f.GetSymbolAsInt("Inj_konst!");
            int maxInjection = f.GetMaxInjection() * injkonst;
            byte[] xaxis = bin.Find("Fuel_map_xaxis!") is { } xs && bin.FileAddress(xs) is var xa and >= 0 ? bin.Read(xa, xs.Length) : [0];
            float maxSupportBoost = xaxis[^1] * SensorPercent(sensor) / 100 / 100f - 1;
            maxInjection = maxInjection * (int)(1.4F / maxSupportBoost * 100) / 100;
            byte[] afterstart = bin.Find("Eftersta_fak!") is { } es && bin.FileAddress(es) is var ea and >= 0 ? bin.Read(ea, es.Length) : [];
            bool e85 = maxInjection > 7500 || injkonst > 26 || afterstart.Length == 15 && afterstart[13] > 170;
            if (e85) maxInjection = maxInjection * 10 / 14;
            InjectorType injectors = maxInjection switch
            {
                > 5000 => InjectorType.Stock,
                > 3500 => InjectorType.GreenGiants,
                > 2000 => InjectorType.Siemens630Dekas,
                > 1565 => InjectorType.Siemens875Dekas,
                _ => InjectorType.Siemens1000cc,
            };
            int frek230 = f.GetSymbolAsInt("Frek_230!"), frek250 = f.GetSymbolAsInt("Frek_250!");
            bool t5Valve = bin.IsTrionic55 ? frek230 == 90 || frek250 == 70 : frek230 == 728 || frek250 == 935;
            return new FileGuess(stage, maxBoost, sensor, e85, injectors, t5Valve);
        }

        /// <summary>Examine binary (btnBinExaminor): "Examination report", the stage, sensor, fuel, injector and valve guesses.</summary>
        public static List<string> Examine(T5Binary bin)
        {
            Trionic5Properties p = bin.File.GetTrionicProperties();
            FileGuess g = Guess(bin);
            List<string> r = ["", "Report for file: " + Path.GetFileName(bin.FileName), "", bin.IsTrionic55 ? "File type: Trionic 5.5" : "File type: Trionic 5.2",
                "CPU speed: " + p.CPUspeed, "Data name: " + p.Dataname, "Engine type: " + p.Enginetype, "Partnumber: " + p.Partnumber,
                "Software ID: " + p.SoftwareID, p.RAMlocked ? "SRAM is locked" : "SRAM is unlocked"];
            int n = (int)g.Stage;
            r.Add(g.Stage == TuningStage.Stock ? "Stage: stock" : g.Stage == TuningStage.StageX ? "Stage: X" : "Stage: " + n);
            if (g.Stage != TuningStage.Stock && (n >= 3 || g.Stage == TuningStage.StageX))
            {
                // Stage X listed stage 8's parts without the plugs
                string[] parts = g.Stage == TuningStage.StageX ? Requires[^1][..^1] : Requires[Math.Min(n, 8) - 3];
                r.AddRange(parts.Select(x => "\tRequires: " + x));
                r.Add("");
            }
            r.Add($"Boost request peak: {g.MaxBoost:F2} bar");
            r.Add(g.Sensor == MapSensorType.MapSensor25 ? "Mapsensor type: stock 2.5 bar sensor" : $"Mapsensor type: {T5Tuning.SensorName(g.Sensor)[..3]} bar sensor");
            r.Add(g.E85 ? "Probable fuel: E85" : g.MaxBoost > 1.1 ? "Probable fuel: Premium quality petrol" : "Probable fuel: Petrol");
            r.Add(g.Injectors switch
            {
                InjectorType.Stock => "Injectors: stock",
                InjectorType.GreenGiants => "Injectors: Green giants (413 cc/min)",
                InjectorType.Siemens630Dekas => "Injectors: Siemens deka 630 cc/min",
                InjectorType.Siemens875Dekas => "Injectors: Siemens deka 875 cc/min",
                _ => "Injectors: Siemens deka 1000 cc/min",
            });
            r.Add(g.T5Valve ? "APC valve type: Trionic 5" : "APC valve type: Trionic 7");
            return r;
        }
}
}
