using AutoClaude.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoClaude.App.Views;

public sealed partial class SessionDetailsPanel : UserControl
{
    public SessionDetailsViewModel ViewModel { get; }

    public SessionDetailsPanel(SessionDetailsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnAddDirectoryClick(object sender, RoutedEventArgs e)
        => ViewModel.ShowAddDirectoryCommand.Execute(null);

    private void OnRemoveDirectoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
            ViewModel.RemoveDirectoryCommand.Execute(path);
    }

    private void OnAddPersistentMemoryClick(object sender, RoutedEventArgs e)
        => ViewModel.AddPersistentMemoryCommand.Execute(null);

    private void OnAddTemporaryMemoryClick(object sender, RoutedEventArgs e)
        => ViewModel.AddTemporaryMemoryCommand.Execute(null);

    private void OnRemovePersistentMemoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MemoryEntryItem item })
            ViewModel.RemovePersistentMemoryCommand.Execute(item);
    }

    private void OnRemoveTemporaryMemoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MemoryEntryItem item })
            ViewModel.RemoveTemporaryMemoryCommand.Execute(item);
    }
}
