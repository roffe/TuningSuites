; TxSuite's Windows setup (Inno Setup 6): the self-contained win-x86 publish folder with libusb-1.0.dll and the VC++ 2010 runtime
; added (.github/workflows/build.yml), the disclaimer, shortcuts and the choice to open .bin files with TxSuite.
;   ISCC /DPublishDir=<out\TxSuite> /O<dist> SetupTx\TxSuite.iss   ->   TxSuiteSetup.exe
; TxExtras (TxExtras.iss) puts the stock bins into the folder this installs to.

#ifndef PublishDir
  #define PublishDir "..\TxSuite\bin\Release\net10.0\win-x86\publish"
#endif
#define Exe PublishDir + "\TxSuite.exe"

[Setup]
; TxExtras finds TxSuite's folder by this id's uninstall entry (TxSuite_is1)
AppId=TxSuite
AppName=TxSuite
; the Txsuite_v tag's version (Directory.Build.props), the file version is it padded to four parts
AppVersion={#GetStringFileInfo(Exe, "ProductVersion")}
VersionInfoVersion={#GetVersionNumbersString(Exe)}
AppPublisher=MattiasC
AppPublisherURL=https://github.com/roffe/TuningSuites
AppUpdatesURL=https://github.com/roffe/TuningSuites/releases
DefaultDirName={autopf}\MattiasC\TxSuite
DisableProgramGroupPage=yes
LicenseFile=disclaimer.rtf
SetupIconFile=..\TxSuite\TxSuite.ico
UninstallDisplayIcon={app}\TxSuite.exe
WizardStyle=modern
WizardImageFile=wizard.bmp,wizard200.bmp
WizardSmallImageFile=small.bmp,small200.bmp
OutputBaseFilename=TxSuiteSetup
Compression=lzma2/max
SolidCompression=yes
ChangesAssociations=yes
PrivilegesRequired=admin
; .NET 10's oldest Windows
MinVersion=10.0

[Tasks]
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: binassoc; Description: "Open .bin files with TxSuite"; GroupDescription: "File associations:"

[Files]
; native SkiaSharp / HarfBuzz symbols are ~100 MB
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "lib*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\TxSuite"; Filename: "{app}\TxSuite.exe"
Name: "{autodesktop}\TxSuite"; Filename: "{app}\TxSuite.exe"; Tasks: desktopicon

[Registry]
; .bin files always list TxSuite under Open with; the task makes it their program. Windows keeps a program the user picked with
; "Always use this app" over it, Open with → TxSuite → Always changes that
Root: HKA; Subkey: "Software\Classes\.bin\OpenWithProgids"; ValueType: string; ValueName: "TxSuite.bin"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\TxSuite.bin"; ValueType: string; ValueName: ""; ValueData: "Trionic binary"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\TxSuite.bin\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\TxSuite.exe,0"
Root: HKA; Subkey: "Software\Classes\TxSuite.bin\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\TxSuite.exe"" ""%1"""
Root: HKA; Subkey: "Software\Classes\.bin"; ValueType: string; ValueName: ""; ValueData: "TxSuite.bin"; Flags: uninsdeletevalue; Tasks: binassoc

[Run]
Filename: "{app}\TxSuite.exe"; Description: "{cm:LaunchProgram,TxSuite}"; Flags: nowait postinstall skipifsilent
