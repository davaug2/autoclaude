using AutoClaude.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoClaude.App.Views;

public sealed partial class HomeTab : UserControl
{
    public HomeViewModel ViewModel { get; }

    public HomeTab(HomeViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnOpenSessionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SessionListItem item })
        {
            ViewModel.OpenSessionCommand.Execute(item);
        }
    }

    private async void OnDeleteSessionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SessionListItem item })
            return;

        var dialog = new ContentDialog
        {
            Title = "Excluir sessao",
            Content = $"Deseja excluir a sessao \"{item.Name}\"?\nTodos os dados serao removidos.",
            PrimaryButtonText = "Excluir",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.DeleteSessionCommand.Execute(item);
        }
    }
}
