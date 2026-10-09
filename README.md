# TuningSuites
T7 and T8 Suites

The TxSuites are Open Source tools used to tune Trionic 7 and 8 based ECU’s.

It is possible to tune how the software behaves to increase the performance of the car and adjust the software for hardware modifications, such a high performance injectors.

The TxSuites also supports real-time view and can be used to predict the behavior of the tune through it’s Air-mass viewer.

Installer packages and other information for the original suites are located here:
http://www.txsuite.org

# T7Suite for Windows, Linux and macOS

T7Suite has been rebuilt on .NET 10 and Avalonia, without the commercial components the old suite needed, so it runs on Windows, Linux and macOS. It reads, edits and writes the same files as the old T7Suite and talks to the ECU through [TrionicCANLib](https://github.com/roffe/Trionic), the library of the Trionic CAN Flasher. [PORTING.md](PORTING.md) tracks what has been ported and every deliberate difference. T8Suite and T5Suite are still the old .NET Framework applications.

T7Suite supports the same interfaces as the flasher:

| Interface | Windows | Linux | macOS |
|---|---|---|---|
| Lawicel CANUSB | Lawicel CANUSB DLL driver | FTDI serial port (`/dev/ttyUSB*`) | FTDI serial port (`/dev/cu.usbserial-LW*`) |
| CombiAdapter | libusb (WinUSB driver) | libusb + udev rule | libusb |
| OBDLink SX / ELM327 | serial port | serial port | serial port |
| Just4Trionic | serial port | serial port | serial port |
| SLCAN | serial port | serial port | serial port |
| Kvaser HS | Kvaser CANlib driver | Kvaser linuxcan | not supported |
| J2534 (Beta) | vendor DLL (32-bit, from the registry) | vendor `.so` from `~/.passthru/*.json` | not supported |
| Wideband lambda (realtime) | serial port | serial port | serial port |

## System Requirements
- Windows 10 or Windows 11. The setup brings its own .NET runtime and the Visual C++ 2010 runtime the Lawicel driver needs.
- Linux x64 or arm64 with a desktop (X11 or XWayland).
- macOS on Apple silicon or Intel.

The adapters still need their own drivers, see [Adapter setup](https://github.com/roffe/Trionic#adapter-setup) in the flasher's README.

## Installation

The downloads are on the [releases page](https://github.com/roffe/TuningSuites/releases): the `T7suite_v…` releases, and `T7suite_nightly` with the latest build. Every package has the stock binaries in `Binaries` (Compare to original file, Lookup partnumber), which the separate T7Extras setup used to install.

### Windows
Download T7Suite.zip (or T7Suite.msi), extract T7Suite.msi and run it. It installs to `Program Files (x86)\MattiasC\T7Suite` and replaces an installed old T7Suite; T7Suite takes the old suite's settings over the first time it starts. Running the setup of a newer build over an existing installation upgrades it. `.bin` files get T7Suite under Open with; their default program stays as it is.

### Linux
Download T7Suite-linux-x64.tar.gz (or -linux-arm64) and run it from where you extracted it:

    tar xzf T7Suite-linux-x64.tar.gz
    ./T7Suite/T7Suite

`./T7Suite/install-desktop.sh` adds T7Suite to the application menu and to Open with for `.bin` files, for your user only (run it again after moving the folder). The CombiAdapter needs libusb and the udev rule next to the program:

    sudo cp T7Suite/70-t7suite.rules /etc/udev/rules.d/
    sudo udevadm control --reload && sudo udevadm trigger

### macOS
Download T7Suite-osx-arm64.zip (Apple silicon) or -osx-x64 (Intel). The build is not signed, so clear the quarantine flag after extracting it, then start it from Terminal or by double-clicking T7Suite in Finder:

    unzip T7Suite-osx-arm64.zip
    xattr -dr com.apple.quarantine T7Suite
    ./T7Suite/T7Suite

## Updates
T7Suite looks for a newer `T7suite_v…` release when it starts and from Help → Check for updates, and shows the result in the status bar. When there is one it asks; OK downloads the setup on Windows and opens the release page elsewhere.

## Disclaimer
This is Open Source software that pokes around in your car's control system. The authors of the tools shall not be held accountable for how you decide to use the tools. If you are not careful, you can easily brick your car with these tools so please use this software with care.

## Building from source
You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). TrionicCANLib comes from the `Trionic` submodule:

    git clone https://github.com/roffe/TuningSuites
    cd TuningSuites
    git checkout net10
    git submodule init Trionic
    git submodule update
    dotnet test T7CoreTest
    dotnet run --project T7App

`-p:TrionicDir=../Trionic` builds against another checkout of roffe/Trionic instead, for working on both.

A self-contained build that runs on a machine without .NET, for `win-x86`, `linux-x64`, `linux-arm64`, `osx-arm64` or `osx-x64`:

    dotnet publish T7App/T7App.csproj -c Release -r linux-x64 --self-contained -o out/T7Suite

The Windows build is 32-bit (`win-x86`) because most J2534 drivers and the Lawicel CANUSB driver only come as 32-bit DLLs, so on Windows `dotnet run` needs the x86 .NET 10 runtime installed.

The Windows setup is built with WiX 6 (restored from NuGet) on Windows, from a `win-x86` publish folder with libusb-1.0.dll added to it (`MinGW32/dll/libusb-1.0.dll` from the [libusb release](https://github.com/libusb/libusb/releases)):

    dotnet build SetupT7/SetupT7.wixproj -c Release -p:Platform=x86 -p:PublishDir=C:\path\to\out\T7Suite

The release builds are made by [.github/workflows/build.yml](.github/workflows/build.yml): every push to `net10` updates the `T7suite_nightly` pre-release, a `T7suite_v*` tag makes a release.

## Versioning and releasing
No file holds the version. T7Suite takes it from the nearest `T7suite_vX.Y.Z` git tag, the tag scheme the suites' releases have always used ([Directory.Build.props](Directory.Build.props)), so a T7Suite tag doesn't version T8Suite or T5Suite. The window title shows it:

| Build | Version |
|---|---|
| A tagged commit, for example `T7suite_v2.0.0` | `2.0.0` |
| Commits after the tag (nightlies, local builds) | `2.0.0-<first 9 characters of the commit sha>` |
| Local build with uncommitted changes | `2.0.0-<sha>-dirty` |
| A tag with a label, for example `T7suite_v2.1.0-beta` | `2.1.0-beta`, after it `2.1.0-beta-<sha>` |
| No git, no tag or a shallow clone | `0.0.0-<sha>`, with a build warning |

The file and assembly version, which the update check and the MSI use, is the tag's numbers padded to four parts (`2.0.0.0`). A nightly keeps its tag's number, so running its setup again reinstalls over the same version.

To make a release, tag the commit and push the tag:

    git tag T7suite_v2.0.0
    git push origin T7suite_v2.0.0

To build without git, for example from a source archive, pass the version: `dotnet build -p:Version=2.0.0`.
