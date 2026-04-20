using System.Collections.Specialized;
using AutoClaude.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AutoClaude.App.Views;

public sealed partial class SessionTab : UserControl
{
    public SessionTabViewModel ViewModel { get; }

    public SessionTab(SessionTabViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        ViewModel.Events.CollectionChanged += OnEventsChanged;
        ViewModel.InputRequested += OnInputRequested;
        ViewModel.DetailsToggled += OnDetailsToggled;
    }

    private void OnDetailsToggled(object? sender, EventArgs e)
    {
        if (ViewModel.IsDetailsVisible && ViewModel.DetailsViewModel != null)
        {
            if (DetailsContainer.Content == null)
                DetailsContainer.Content = new SessionDetailsPanel(ViewModel.DetailsViewModel);

            _ = ViewModel.DetailsViewModel.LoadAsync(ViewModel.SessionId);
        }
    }

    private void OnEventsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            EventsScroller.UpdateLayout();
            EventsScroller.ChangeView(null, EventsScroller.ScrollableHeight, null);
        });
    }

    private void OnInputRequested(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            EventsScroller.UpdateLayout();
            EventsScroller.ChangeView(null, EventsScroller.ScrollableHeight, null);
        });
    }
}
