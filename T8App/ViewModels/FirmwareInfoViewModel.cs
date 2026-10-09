using System;
using CommunityToolkit.Mvvm.ComponentModel;
using T8SuitePro;

namespace T8App.ViewModels;

/// <summary>
/// frmFirmwareInformation: every field read only, except the software version after a double-click on its label, and the VIN with
/// the immobilizer code after a double-click on either label (T8Suite dropped the edits when it was the serial number's).
/// </summary>
public partial class FirmwareInfoViewModel(FirmwareInfo info) : ObservableObject
{
    public FirmwareInfo Info { get; } = info;

    [ObservableProperty] private string _softwareVersion = info.SoftwareVersion;
    [ObservableProperty] private string _serialNumber = info.SerialNumber;
    [ObservableProperty] private string _chassisId = info.ChassisId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SoftwareVersionLength))]
    private bool _editSoftwareVersion;

    [ObservableProperty] private bool _editVinAndImmo;

    /// <summary>The software version keeps its length until it's edited, as the old text box did; 0 is no limit.</summary>
    public int SoftwareVersionLength => EditSoftwareVersion ? 0 : Info.SoftwareVersion.Length;

    /// <summary>The experimental-support warnings, shown by the view.</summary>
    public event Action<string>? Hint;

    public void StartSoftwareVersionEdit()
    {
        if (EditSoftwareVersion) return;
        EditSoftwareVersion = true;
        Hint?.Invoke("Warning: T8Suite only has experimental support for replacing software version string");
    }

    public void StartVinAndImmoEdit()
    {
        if (EditVinAndImmo) return;
        EditVinAndImmo = true;
        Hint?.Invoke("Warning: T8Suite only has experimental support for changing VIN and immobilizer codes!!!");
    }

    /// <summary>"Clear": a blank VIN, written with OK (T8Suite wrote it into the file at once, which Cancel didn't undo).</summary>
    public void ClearVin() => ChassisId = new string(' ', 17);

    public FirmwareEdit ToEdit() =>
        new(EditSoftwareVersion ? SoftwareVersion : null, EditVinAndImmo ? ChassisId : null, EditVinAndImmo ? SerialNumber : null);
}
