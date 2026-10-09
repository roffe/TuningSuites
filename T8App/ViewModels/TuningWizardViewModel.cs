using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using T8SuitePro;

namespace T8App.ViewModels;

/// <summary>
/// frmTuningWizard: welcome, the compatible packs, the code (packs with one), the confirmation, the result. Next on the
/// confirmation applies the pack; from then on there is no way back.
/// </summary>
public partial class TuningWizardViewModel : ObservableObject
{
    public const int Welcome = 0, Select = 1, EnterCode = 2, Confirm = 3, Completed = 4;

    private readonly Func<WizardPack, Task<List<string>?>> m_apply;

    public TuningWizardViewModel(string software, IReadOnlyList<WizardPack> packs, Func<WizardPack, Task<List<string>?>> apply)
    {
        Packs = packs;
        SoftwareVersion = software.Length > 4 ? software[..4] : software.Length > 0 ? software : "nAn!";
        m_apply = apply;
        _selected = packs.Count > 0 ? packs[0] : null;
    }

    public IReadOnlyList<WizardPack> Packs { get; }

    /// <summary>The first four characters of the file's software version.</summary>
    public string SoftwareVersion { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(CanNext), nameof(CanBack), nameof(CanCancel), nameof(IsFinished), nameof(OnWelcome), nameof(OnSelect),
        nameof(OnCode), nameof(OnConfirm))]
    private int _page;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Author), nameof(CanNext))]
    private WizardPack? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanNext))]
    private string _code = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanNext))]
    private bool _understood;

    [ObservableProperty]
    private string _finishText = "";

    [ObservableProperty]
    private List<string> _results = [];

    public string Title => Page switch
    {
        Select => "Select Tuning Action",
        EnterCode => "Enter Tuning Code",
        Confirm => "Confirm Tuning Action",
        Completed => "Completed Tuning Wizard",
        _ => "Tuning Wizard",
    };

    public string Author => Selected?.Author ?? "";
    public string CodeText => $"The Tuning Package '{Selected}' requires that you enter the correct code.";
    public string HintText => "Hint: Try with authors first. For this Tuning Package it is " + Selected?.Author;

    public bool CanNext => Page switch
    {
        Welcome => true,
        Select => Selected != null,
        EnterCode => Code == Selected?.Code,
        Confirm => Understood,
        _ => false,
    };

    public bool CanBack => Page is > Welcome and < Completed;
    public bool CanCancel => Page != Completed;
    public bool IsFinished => Page == Completed;
    public bool OnWelcome => Page == Welcome;
    public bool OnSelect => Page == Select;
    public bool OnCode => Page == EnterCode;
    public bool OnConfirm => Page == Confirm;

    /// <summary>Next; true when it applied the pack.</summary>
    public async Task<bool> NextAsync()
    {
        if (!CanNext) return false;
        if (Page == Select)
        {
            Code = "";
            OnPropertyChanged(nameof(CodeText));
            OnPropertyChanged(nameof(HintText));
            Page = Selected!.Code != "" ? EnterCode : Confirm;
            return false;
        }
        if (Page != Confirm)
        {
            Page++;
            return false;
        }
        List<string>? results = await m_apply(Selected!);
        Results = results ?? [];
        FinishText = results != null
            ? $"You have now completed the Tuning Action '{Selected}'. Please check the modified maps below so that they are what you expect them to be. Easiest way to do that is to compare to the original binary."
            : $"The Tuning Action '{Selected}' failed! You should likely not use this binary at this point.";
        Page = Completed;
        return results != null;
    }

    /// <summary>Back; leaving the confirmation unticks it.</summary>
    public void Back()
    {
        if (!CanBack) return;
        if (Page == Confirm) Understood = false;
        Page = Page == Confirm && Selected?.Code == "" ? Select : Page - 1;
    }
}
