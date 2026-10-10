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

        /// <summary>Examine binary (btnBinExaminor): "Examination report", the stage, sensor, fuel, injector and valve guesses.</summary>
        public static List<string> Examine(T5Binary bin)
        {
            Trionic5File f = bin.File;
            MapSensorType sensor = f.GetMapSensorType(true);
            Trionic5Properties p = f.GetTrionicProperties();
            List<string> r = ["", "Report for file: " + Path.GetFileName(bin.FileName), "", bin.IsTrionic55 ? "File type: Trionic 5.5" : "File type: Trionic 5.2",
                "CPU speed: " + p.CPUspeed, "Data name: " + p.Dataname, "Engine type: " + p.Enginetype, "Partnumber: " + p.Partnumber,
                "Software ID: " + p.SoftwareID, p.RAMlocked ? "SRAM is locked" : "SRAM is unlocked"];

            TuningStage stage = f.DetermineTuningStage(out float maxBoost);
            int n = (int)stage;
            r.Add(stage == TuningStage.Stock ? "Stage: stock" : stage == TuningStage.StageX ? "Stage: X" : "Stage: " + n);
            if (stage != TuningStage.Stock && (n >= 3 || stage == TuningStage.StageX))
            {
                // Stage X listed stage 8's parts without the plugs
                string[] parts = stage == TuningStage.StageX ? Requires[^1][..^1] : Requires[Math.Min(n, 8) - 3];
                r.AddRange(parts.Select(x => "\tRequires: " + x));
                r.Add("");
            }
            r.Add($"Boost request peak: {maxBoost:F2} bar");
            r.Add(sensor == MapSensorType.MapSensor25 ? "Mapsensor type: stock 2.5 bar sensor" : $"Mapsensor type: {T5Tuning.SensorName(sensor)[..3]} bar sensor");

            int injkonst = f.GetSymbolAsInt("Inj_konst!");
            int maxInjection = f.GetMaxInjection() * injkonst;
            byte[] xaxis = bin.Find("Fuel_map_xaxis!") is { } xs && bin.FileAddress(xs) is var xa and >= 0 ? bin.Read(xa, xs.Length) : [0];
            float maxSupportBoost = xaxis[^1] * SensorPercent(sensor) / 100 / 100f - 1;
            maxInjection = maxInjection * (int)(1.4F / maxSupportBoost * 100) / 100;
            byte[] afterstart = bin.Find("Eftersta_fak!") is { } es && bin.FileAddress(es) is var ea and >= 0 ? bin.Read(ea, es.Length) : [];
            bool e85 = maxInjection > 7500 || injkonst > 26 || afterstart.Length == 15 && afterstart[13] > 170;
            if (e85)
            {
                maxInjection = maxInjection * 10 / 14;
                r.Add("Probable fuel: E85");
            }
            else r.Add(maxBoost > 1.1 ? "Probable fuel: Premium quality petrol" : "Probable fuel: Petrol");
            r.Add(maxInjection switch
            {
                > 5000 => "Injectors: stock",
                > 3500 => "Injectors: Green giants (413 cc/min)",
                > 2000 => "Injectors: Siemens deka 630 cc/min",
                > 1565 => "Injectors: Siemens deka 875 cc/min",
                _ => "Injectors: Siemens deka 1000 cc/min",
            });
            int frek230 = f.GetSymbolAsInt("Frek_230!"), frek250 = f.GetSymbolAsInt("Frek_250!");
            bool t5Valve = bin.IsTrionic55 ? frek230 == 90 || frek250 == 70 : frek230 == 728 || frek250 == 935;
            r.Add(t5Valve ? "APC valve type: Trionic 5" : "APC valve type: Trionic 7");
            return r;
        }
    }
}
