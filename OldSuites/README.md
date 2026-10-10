# OldSuites

The .NET Framework suites the .NET 10 ports at the top of the repository replaced, kept unchanged for reference: to look up how something worked before the port, or to find a feature that hasn't been ported yet.

| Folder | What it was |
|---|---|
| `T5Suite2.0`, `Trionic5Tools`, `Trionic5Controls`, `T5CANLib` | T5Suite 2.0, its file logic, its controls and CAN library |
| `T7Suite`, `T8Suite`, `CommonSuite` | T7Suite, T8SuitePro and the code they shared |
| `T5 CAN Flasher`, `T5CanFlash`, `T7CANFlasher`, `WakeupCANbus` | the old flashers and the CAN wake-up tool; TrionicCANFlasher (the `Trionic` submodule) replaced them |
| `AquaGauge`, `ICSharpCode.TextEditor`, `LBIndustrialCtrls`, `MouseGestures`, `OnlineGraph`, `Owf.Controls.DigitalDisplayControl`, `ProCharts`, `ProGauges`, `PSTaskDialog`, `radiobutton`, `RealtimeGraph` | the WinForms controls the old suites used |
| `T7Libs`, `T8Libs`, `packages` | the binary dependencies (DevExpress and others) and the old NuGet folder |
| `SetupT5SuiteII`, `SetupT7Suite`, `SetupT7Extras`, `SetupT8SuitePro`, `SetupT8Extras`, `release*.bat` | the old installers and release scripts |
| `SuiteLauncher`, `SuiteTest`, `CommonSuiteTest`, `Utils`, `Scripts` | the launcher, the old tests, helper scripts, BDM scripts |
| `*.sln`, `*.vsmdi` | the old Visual Studio solutions |

They don't build here: they need Visual Studio, .NET Framework and the commercial DevExpress components. How each suite behaved is written down in [docs/](../docs), and [PORTING.md](../PORTING.md) lists what the ports changed.
