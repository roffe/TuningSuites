; TxExtras (Inno Setup 6): the stock bins of all three ECUs into TxSuite's folder, Binaries\T5, Binaries\T7 (with T7's symbol files)
; and Binaries\T8, where TxSuite looks for them (PartInfo.StockFolder). The version is the Txextras_v tag's (.github/workflows/extras.yml):
;   ISCC /DVersion=1.0.0 /O<dist> SetupTx\TxExtras.iss   ->   TxExtrasSetup.exe

#ifndef Version
  #define Version "0.0.0"
#endif

[Setup]
AppId=TxExtras
AppName=TxExtras
AppVersion={#Version}
VersionInfoVersion={#Version}
AppPublisher=MattiasC
AppPublisherURL=https://github.com/roffe/TuningSuites
AppUpdatesURL=https://github.com/roffe/TuningSuites/releases
; TxSuite's folder, from its setup's uninstall entry
DefaultDirName={reg:HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\TxSuite_is1,InstallLocation|{autopf}\MattiasC\TxSuite}
DirExistsWarning=no
DisableProgramGroupPage=yes
LicenseFile=disclaimer.rtf
SetupIconFile=..\TxSuite\TxSuite.ico
WizardStyle=modern
WizardImageFile=wizard.bmp,wizard200.bmp
WizardSmallImageFile=small.bmp,small200.bmp
OutputBaseFilename=TxExtrasSetup
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=admin
MinVersion=10.0

[Files]
Source: "..\T5Binaries\*"; DestDir: "{app}\Binaries\T5"; Flags: ignoreversion
Source: "..\T7Binaries\*"; DestDir: "{app}\Binaries\T7"; Flags: ignoreversion
Source: "..\T8Binaries\*"; DestDir: "{app}\Binaries\T8"; Flags: ignoreversion
