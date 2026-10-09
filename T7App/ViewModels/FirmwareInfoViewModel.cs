using System;
using System.Globalization;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using T7;

namespace T7App.ViewModels;

/// <summary>frmFirmwareInformation: the detected values, editable where T7Suite allowed it; OK applies them (FirmwareInfo.Apply).</summary>
public partial class FirmwareInfoViewModel : ObservableObject
{
    public FirmwareInfo Info { get; }

    /// <summary>Programming date only editable with WriteTimestampInBinary.</summary>
    public bool CanEditDate { get; }

    // footer fields keep their length in the file
    public int CarDescriptionLength => Info.CarDescription.Length;
    public int SoftwareVersionLength => Info.SoftwareVersion.Length;
    public int ImmobilizerIDLength => Math.Max(Info.ImmobilizerID.Length, m_immo.Length);
    public int ChassisIDLength => Math.Max(Info.ChassisID.Length, m_chassis.Length);
    public int SIDDateLength => Info.SIDDate.Length;

    [ObservableProperty] private string _carDescription;
    [ObservableProperty] private string _softwareVersion;
    [ObservableProperty] private string _immobilizerID;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Decoded))]
    private string _chassisID;
    [ObservableProperty] private string _sIDDate;
    [ObservableProperty] private string _programmingDate;

    [ObservableProperty] private bool _openSIDInfo;
    [ObservableProperty] private bool _torqueLimiters;
    [ObservableProperty] private bool _catalystLightOff;
    [ObservableProperty] private bool _secondLambda;
    [ObservableProperty] private bool _oBDII;
    [ObservableProperty] private bool _fastThrottleResponse;
    [ObservableProperty] private bool _extraFastThrottleResponse;
    [ObservableProperty] private bool _noTCS;
    [ObservableProperty] private bool _disableEmissionLimiting;
    [ObservableProperty] private bool _disableStartScreen;
    [ObservableProperty] private bool _disableAdaptionMessages;

    /// <summary>The import's previous VIN and immobilizer code, for Undo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndoImport))]
    private (string chassis, string immo)? _beforeImport;

    public bool CanUndoImport => BeforeImport != null;

    /// <summary>Shown when Open SID info gets checked, as T7Suite did.</summary>
    public event Action<string>? Hint;

    private string m_chassis = "", m_immo = "";

    public FirmwareInfoViewModel(FirmwareInfo info, bool writeTimestamp)
    {
        Info = info;
        CanEditDate = writeTimestamp;
        _carDescription = info.CarDescription;
        _softwareVersion = info.SoftwareVersion;
        _immobilizerID = info.ImmobilizerID;
        _chassisID = info.ChassisID;
        _sIDDate = info.SIDDate;
        // an unstamped bin shows "now", like the old date box
        _programmingDate = (info.ProgrammingDate ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        _openSIDInfo = info.OpenSIDInfo.Enabled;
        _torqueLimiters = info.TorqueLimiters.Enabled;
        _catalystLightOff = info.CatalystLightOff.Enabled;
        _secondLambda = info.SecondLambda.Enabled;
        _oBDII = info.OBDII.Enabled;
        _fastThrottleResponse = info.FastThrottleResponse.Enabled;
        _extraFastThrottleResponse = info.ExtraFastThrottleResponse.Enabled;
        _noTCS = info.NoTCS.Enabled;
        _disableEmissionLimiting = info.DisableEmissionLimiting.Enabled;
        _disableStartScreen = info.DisableStartScreen.Enabled;
        _disableAdaptionMessages = info.DisableAdaptionMessages.Enabled;
    }

    // the two throttle options exclude each other
    partial void OnFastThrottleResponseChanged(bool value)
    {
        if (value) ExtraFastThrottleResponse = false;
    }

    partial void OnExtraFastThrottleResponseChanged(bool value)
    {
        if (value) FastThrottleResponse = false;
    }

    partial void OnOpenSIDInfoChanged(bool value)
    {
        if (value) Hint?.Invoke("You can edit the SID parameters in T7Suite by starting the SID editor via Actions -> SID information");
    }

    /// <summary>The VIN decoder below the fields.</summary>
    public SuiteApp.ViewModels.VinDecoding Decoded => new(ChassisID);

    /// <summary>"Import": the VIN and immobilizer code of another bin.</summary>
    public void Import(string file)
    {
        var header = new TrionicCANLib.Checksum.T7FileHeader();
        if (!header.init(file, false)) return;
        BeforeImport ??= (ChassisID, ImmobilizerID);
        m_chassis = header.getChassisID();
        m_immo = header.getImmobilizerID();
        ChassisID = m_chassis;
        ImmobilizerID = m_immo;
        OnPropertyChanged(nameof(ChassisIDLength));
        OnPropertyChanged(nameof(ImmobilizerIDLength));
    }

    public void UndoImport()
    {
        if (BeforeImport is not var (chassis, immo)) return;
        ChassisID = chassis;
        ImmobilizerID = immo;
        BeforeImport = null;
    }

    public FirmwareEdit ToEdit() => new()
    {
        CarDescription = CarDescription,
        SoftwareVersion = SoftwareVersion,
        ImmobilizerID = ImmobilizerID,
        ChassisID = ChassisID,
        SIDDate = SIDDate,
        ProgrammingDate = DateTime.TryParse(ProgrammingDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) ? d : Info.ProgrammingDate,
        OpenSIDInfo = OpenSIDInfo,
        TorqueLimiters = TorqueLimiters,
        CatalystLightOff = CatalystLightOff,
        SecondLambda = SecondLambda,
        OBDII = OBDII,
        FastThrottleResponse = FastThrottleResponse,
        ExtraFastThrottleResponse = ExtraFastThrottleResponse,
        NoTCS = NoTCS,
        DisableEmissionLimiting = DisableEmissionLimiting,
        DisableStartScreen = DisableStartScreen,
        DisableAdaptionMessages = DisableAdaptionMessages,
    };
}
