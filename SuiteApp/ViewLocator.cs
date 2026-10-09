using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SuiteApp.ViewModels;

namespace SuiteApp;

/// <summary>
/// A document's view by name, next to its view model: Foo.ViewModels.BarViewModel shows as Foo.Views.BarView, from the view
/// model's assembly. The apps' only data template, so floating document windows find the views too.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data == null) return null;
        Type type = data.GetType();
        string name = type.FullName!.Replace(".ViewModels.", ".Views.").Replace("ViewModel", "View");
        return type.Assembly.GetType(name) is { } view ? (Control)Activator.CreateInstance(view)! : new TextBlock { Text = "No view: " + name };
    }

    public bool Match(object? data) => data is DocumentViewModel;
}
