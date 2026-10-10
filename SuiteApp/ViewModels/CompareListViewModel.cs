using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;

namespace SuiteApp.ViewModels;

/// <summary>One file of the compare list: how many symbols differ from the open bin.</summary>
public sealed record CompareListRow(string File, string Name, int Differences);

/// <summary>
/// T5Suite's "Compare list: &lt;file&gt;" (CompareResultSelector): several files compared with the open bin at once, each with its number
/// of differing symbols; opening a row shows its compare results.
/// </summary>
public sealed class CompareListViewModel(MainWindowViewModel owner, string binFile, IReadOnlyList<CompareListRow> rows) : DocumentViewModel
{
    public override string Title => "Compare list: " + Path.GetFileName(binFile);

    public IReadOnlyList<CompareListRow> Rows { get; } = rows;

    public Task Open(CompareListRow? row) => row != null ? owner.CompareToFileAsync(row.File) : Task.CompletedTask;

    /// <summary>The rows: every file the suite can open, compared with the bin.</summary>
    public static List<CompareListRow> Count(SuiteBinary bin, IEnumerable<string> files, System.Func<string, SuiteBinary> open) =>
        files.Select(f => new CompareListRow(f, Path.GetFileName(f), SuiteCompare.Compare(bin, open(f)).Count)).ToList();
}
