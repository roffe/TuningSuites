namespace CommonSuite
{
    /// <summary>
    /// A grid group under another (the symbol list's subcategory, the partnumber list's engine type): shown as its name, equal only under
    /// the same parent. Avalonia's DataGrid folds the groups again after a refresh by their key alone, at any level, so with plain names
    /// a Fuel → Idle subcategory took the fold of the Idle category.
    /// </summary>
    public sealed record GroupKey(string Parent, string Name)
    {
        public override string ToString() => Name;
    }
}
