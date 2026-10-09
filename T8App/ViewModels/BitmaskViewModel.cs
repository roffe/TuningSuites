using System.Collections.Generic;
using System.Linq;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace T8App.ViewModels;

/// <summary>A box of the bit mask viewer: named by the symbol whose mask is exactly this bit, else blank and disabled.</summary>
public partial class MaskBit : ObservableObject
{
    public int Mask { get; init; }
    public string Name { get; init; } = "";
    public bool IsEnabled => Name != "";

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>frmBitmaskViewer ("Bit masked view of symbol"): a 16-bit word, its bits named by the symbols sharing its address.</summary>
public class BitmaskViewModel
{
    /// <summary>0x0001-0x0080, the left column.</summary>
    public List<MaskBit> Low { get; }

    /// <summary>0x0100-0x8000, the right column.</summary>
    public List<MaskBit> High { get; }

    public BitmaskViewModel(IReadOnlyList<SymbolHelper> group, int value)
    {
        // a later symbol with the same mask takes the box; multi-bit masks get none
        List<MaskBit> bits = Enumerable.Range(0, 16).Select(i =>
        {
            int mask = 1 << i;
            SymbolHelper? sh = group.LastOrDefault(s => s.BitMask == mask);
            return new MaskBit { Mask = mask, Name = sh?.SmartVarname ?? "", IsChecked = sh != null && (value & mask) != 0 };
        }).ToList();
        Low = bits[..8];
        High = bits[8..];
    }

    /// <summary>Ok: the checked, named bits; bits without a symbol become 0.</summary>
    public int Value => Low.Concat(High).Where(b => b.IsEnabled && b.IsChecked).Sum(b => b.Mask);
}
