using System.Collections.Generic;

namespace T7
{
    /// <summary>The realtime panel's decoded status texts (frmMain.ConvertFuelcutStatus / ConvertLambdaStatus / ConvertActiveAirDemand).</summary>
    public static class RealtimeStatus
    {
        private static readonly Dictionary<int, string> s_fuelcut = new()
        {
            [0] = "No fuelcut",
            [1] = "Ignition key turned off",
            [2] = "Accelerator pedal pressed during start",
            [3] = "RPM limiter (engine speed guard)",
            [4] = "Throttle block adaption active 1st time",
            [5] = "Airmass limit (pressure guard)",
            [6] = "Airmass limit (pressure guard)",
            [7] = "Immobilizer code incorrect",
            [8] = "Current to h-bridge to high during throttle limphome",
            [9] = "Torque to high during throttle limphome",
            [11] = "Tampering protection of throttle",
            [12] = "Error on all ignition trigger outputs",
            [13] = "ECU not correctly programmed",
            [14] = "To high rpm in throttle limp home, pedal potentiometer fault",
            [15] = "Torque master fuel cut request",
            [16] = "TCM requests fuelcut to smoothen gear shift",
            [20] = "Application conditions for fuel cut",
        };

        private static readonly Dictionary<int, string> s_lambda = new()
        {
            [0] = "Closed loop activated",
            [1] = "Load too high during a specific time",
            [2] = "Load too low",
            [3] = "Load too high, no knock",
            [4] = "Load too high, knocking",
            [5] = "CW temp too low, closed throttle",
            [6] = "CW temp too low, open throttle",
            [7] = "Engine speed too low",
            [8] = "Throttle transient in progress",
            [9] = "Throttle transient in progress and low temperature",
            [10] = "Fuel cut",
            [11] = "Load to high and exhaust temperature algorithm decides it is time to enrich",
            [12] = "Diagnostic failure that affects the lambda control",
            [13] = "Cloosed loop not enabled",
            [14] = "Waiting number of combustion before hardware check",
            [15] = "Waiting until engine probe is warm",
            [16] = "Waiting until number of combustions have past after probe is warm",
            [17] = "SAI request open loop",
            [18] = "Number of combustion to start closed loop has not passed",
            [19] = "Lambda integrator is frozen to 0 by SAI lean clamp",
            [20] = "Catalyst diagnose for V6 controls the fuel",
            [21] = "Gas hybrid active, T7 lambdacontrol stopped",
            [22] = "Lambda integrator may not decrease below 0 during start.",
        };

        private static readonly Dictionary<int, string> s_airDemand = new()
        {
            [10] = "PedalMap",
            [11] = "Cruise control",
            [12] = "Idle control",
            [20] = "Max engine torque",
            [21] = "Traction control",
            [22] = "Manual gearbox limit",
            [23] = "Automatic gearbox limit",
            [24] = "Stall limit (Aut)",
            [25] = "Special mode",
            [26] = "Reverse limit",
            [27] = "Misfire diagnose limit",
            [28] = "Brake management",
            [29] = "Diff protection (Aut)",
            [31] = "Max vehicle speed",
            [40] = "LDA request",
            [41] = "Min load",
            [50] = "Knock airmass limit",
            [51] = "Max engine speed",
            [52] = "Max air for lambda 1",
            [53] = "Max turbo speed",
            [54] = "Crankcase vent error",
            [55] = "Faulty APC",
            [61] = "Engine tipin limit",
            [62] = "Engine tipout limit",
        };

        /// <summary>FCut.CutStatus.</summary>
        public static string Fuelcut(int value) => s_fuelcut.GetValueOrDefault(value, value.ToString());

        /// <summary>Lambda.Status.</summary>
        public static string Lambda(int value) => s_lambda.GetValueOrDefault(value, value.ToString());

        /// <summary>ECMStat.ST_ActiveAirDem: the airmass limiter.</summary>
        public static string AirDemand(int value) => s_airDemand.GetValueOrDefault(value, value.ToString());
    }
}
