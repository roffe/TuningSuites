using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommonSuite;
using SuiteApp.ViewModels;
using T5App.ViewModels;
using T7App.ViewModels;
using T8App.ViewModels;

namespace TxSuite.ViewModels;

/// <summary>
/// TxSuite's main window: T5, T7 and T8 binaries in one, each ECU's own part its module (T5Module, T7Module, T8Module). The menus show
/// the open file's ECU's own items (IsT5, IsT7, IsT8) and, with no file open, only what every ECU has.
/// </summary>
public sealed class MainViewModel : MainWindowViewModel
{
    /// <summary>The release tags Directory.Build.props versions TxSuite by.</summary>
    public const string ReleaseTag = "Txsuite_v";

    // T7 first: its old settings give the shared options (T7Suite was where TxSuite started), then T8's, then T5's
    public MainViewModel() : base(SettingsKey.Program, ReleaseTag, main => [new T7Module(main), new T8Module(main), new T5Module(main)])
    {
        T5.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(T5Module.AdvancedMode)) OnPropertyChanged(nameof(IsT5Advanced)); };
    }

    public T5Module T5 => Modules.OfType<T5Module>().First();

    public T7Module T7 => Modules.OfType<T7Module>().First();

    public T8Module T8 => Modules.OfType<T8Module>().First();

    /// <summary>The ECUs in the order the old suites came (the partnumber list's tabs).</summary>
    public IReadOnlyList<EcuModule> Ecus => [T5, T7, T8];

    /// <summary>A T5 binary is open: T5Suite's own menu items show.</summary>
    public bool IsT5 => HasFile && Module == T5;

    public bool IsT7 => HasFile && Module == T7;

    public bool IsT8 => HasFile && Module == T8;

    public bool IsT5OrT7 => IsT5 || IsT7;

    public bool IsT7OrT8 => IsT7 || IsT8;

    /// <summary>T5Suite's Advanced mode items (Settings → T5 → Advanced mode enabled).</summary>
    public bool IsT5Advanced => IsT5 && T5.AdvancedMode;

    /// <summary>T5's realtime panel: Shift+F5 / Toggle Autotune start and stop its fuel autotune (T5Suite's only).</summary>
    public RealtimeViewModel? T5Realtime => IsT5 ? Realtime : null;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Module) or nameof(HasFile))
            foreach (string name in new[] { nameof(IsT5), nameof(IsT7), nameof(IsT8), nameof(IsT5OrT7), nameof(IsT7OrT8), nameof(IsT5Advanced), nameof(T5Realtime) })
                OnPropertyChanged(name);
        else if (e.PropertyName == nameof(Realtime)) OnPropertyChanged(nameof(T5Realtime));
    }
}
