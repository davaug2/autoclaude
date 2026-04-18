using AutoClaude.App.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace AutoClaude.App.Views;

public sealed partial class SessionTab : UserControl
{
    public SessionTabViewModel ViewModel { get; }

    public SessionTab(SessionTabViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        ViewModel.LogChanged += OnLogChanged;
        ViewModel.InputRequested += OnInputRequested;
        ViewModel.CliOutputChanged += OnCliOutputChanged;
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

    private void OnLogChanged(object? sender, EventArgs e)
    {
        // Auto-scroll to bottom
        DispatcherQueue.TryEnqueue(() =>
        {
            LogScroller.ChangeView(null, LogScroller.ScrollableHeight, null);
        });
    }

    private void OnCliOutputChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            CliOutputScroller.ChangeView(null, CliOutputScroller.ScrollableHeight, null);
        });
    }

    private void OnInputRequested(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            InputBox.Focus(FocusState.Programmatic);
        });
    }

    // --- Splitter drag logic ---
    private bool _isDragging;
    private double _dragStartY;
    private double _dragStartCliHeight;

    private RowDefinition CliRow => ExecutionGrid.RowDefinitions[2];

    private void OnSplitterPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = true;
        _dragStartY = e.GetCurrentPoint(this).Position.Y;
        _dragStartCliHeight = CliRow.Height.Value;
        ((UIElement)sender).CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnSplitterPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        var currentY = e.GetCurrentPoint(this).Position.Y;
        var delta = _dragStartY - currentY;
        var newHeight = Math.Clamp(_dragStartCliHeight + delta, 50, 600);
        CliRow.Height = new GridLength(newHeight);
        e.Handled = true;
    }

    private void OnSplitterPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Ctrl+Enter sends the input
        if (e.Key == Windows.System.VirtualKey.Enter &&
            InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            if (ViewModel.SendInputCommand.CanExecute(null))
            {
                ViewModel.SendInputCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
