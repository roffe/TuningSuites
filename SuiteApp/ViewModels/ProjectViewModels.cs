using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SuiteApp.ViewModels;

/// <summary>frmProjectProperties ("Trionic project properties"), for create and edit.</summary>
public partial class ProjectPropertiesViewModel : ObservableObject
{
    [ObservableProperty] private string _carMake = "SAAB";
    [ObservableProperty] private string _carModel = "";
    [ObservableProperty] private string _carMY = "";
    [ObservableProperty] private string _carVIN = "";
    [ObservableProperty] private string _projectName = "";
    [ObservableProperty] private string _version = "1.00.000";
    [ObservableProperty] private string _binaryFile = "";

    public static ProjectPropertiesViewModel From(ProjectProperties p) => new()
    {
        CarMake = p.CarMake, CarModel = p.CarModel, CarMY = p.CarMY, CarVIN = p.CarVIN, ProjectName = p.Name, Version = p.Version, BinaryFile = p.BinFile,
    };

    public ProjectProperties ToProperties() => new(CarMake, CarModel, CarMY, CarVIN, ProjectName, BinaryFile, Version);
}

/// <summary>frmTransactionLog: the project's transactions, newest first, notes editable, roll back / forward per entry.</summary>
public partial class TransactionLogViewModel : ObservableObject
{
    private readonly MainWindowViewModel m_owner;

    public ObservableCollection<TransactionEntry> Entries { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRollBack), nameof(CanRollForward))]
    private TransactionEntry? _selected;

    public bool CanRollBack => Selected is { IsRolledBack: false };
    public bool CanRollForward => Selected is { IsRolledBack: true };

    public TransactionLogViewModel(MainWindowViewModel owner)
    {
        m_owner = owner;
        Reload();
    }

    public void Reload()
    {
        TransactionEntry? keep = Selected;
        Entries.Clear();
        if (m_owner.Project is not { } project || m_owner.Binary is not { } bin) return;
        foreach (TransactionEntry e in project.TransactionLog.TransCollection.Cast<TransactionEntry>().OrderByDescending(e => e.EntryDateTime))
        {
            e.SymbolName = SuiteProject.SymbolNameByAddress(bin, e.SymbolAddress);
            Entries.Add(e);
        }
        Selected = keep != null && Entries.Contains(keep) ? keep : Entries.FirstOrDefault();
        OnPropertyChanged(nameof(CanRollBack));
        OnPropertyChanged(nameof(CanRollForward));
    }

    public void Roll(bool back)
    {
        if (Selected is not { } e || e.IsRolledBack == back) return;
        m_owner.Roll(e, back);
        Reload();
    }

    /// <summary>An edited note is written back (SetEntryNote rewrites the file).</summary>
    public void NoteChanged(TransactionEntry e) => m_owner.Project?.TransactionLog.SetEntryNote(e);
}

/// <summary>frmProjectLogbook: the logbook, newest first.</summary>
public class LogbookViewModel(SuiteProject project)
{
    public SuiteProject.LogbookLine[] Lines { get; } = project.ReadLogbook().OrderByDescending(l => l.Timestamp).ToArray();
}

/// <summary>frmRebuildFileParameters.</summary>
public partial class RebuildViewModel : ObservableObject
{
    // a date without time: the picker writes back dates only, a time here would make the two-way binding fight forever
    [ObservableProperty] private DateTime? _upTo = DateTime.Today;
    [ObservableProperty] private bool _storeAsCurrent = true;
}
