using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SuiteApp.ViewModels;

namespace SuiteApp;

/// <summary>
/// A document's view by name, next to its view model: Foo.ViewModels.BarViewModel shows as Foo.Views.BarView, from the view
/// model's assembly; a view model without a view of its own shows as its base class (T7's realtime panel as the shared one).
/// The apps' only data template, so floating document windows find the views too.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data == null) return null;
        for (Type? type = data.GetType(); type != null && type != typeof(DocumentViewModel); type = type.BaseType)
        {
            string name = type.FullName!.Replace(".ViewModels.", ".Views.").Replace("ViewModel", "View");
            if (type.Assembly.GetType(name) is { } view) return (Control)Activator.CreateInstance(view)!;
        }
        return new TextBlock { Text = "No view: " + data.GetType().FullName };
    }

    public bool Match(object? data) => data is DocumentViewModel;
}
