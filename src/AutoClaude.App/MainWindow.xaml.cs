using AutoClaude.App.ViewModels;
using AutoClaude.App.Views;
using AutoClaude.Core.Ports;
using AutoClaude.Core.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoClaude.App;

public sealed partial class MainWindow : Window
{
    private readonly SessionService _sessionService;
    private readonly WinUiOrchestrationNotifier _notifier;
    private readonly IExecutionRecordRepository _executionRepo;
    private readonly HomeViewModel _homeViewModel;

    public MainWindow(SessionService sessionService, WinUiOrchestrationNotifier notifier, IExecutionRecordRepository executionRepo)
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        _sessionService = sessionService;
        _notifier = notifier;
        _executionRepo = executionRepo;
        _notifier.Attach(this);

        _homeViewModel = new HomeViewModel(sessionService);
        _homeViewModel.SessionOpenRequested += OnSessionOpenRequested;

        // Add the fixed Home tab
        var homeTab = new TabViewItem
        {
            Header = "Sessoes",
            IsClosable = false,
            IconSource = new FontIconSource { Glyph = "\uE80F" },
            Content = new HomeTab(_homeViewModel)
        };
        MainTabView.TabItems.Add(homeTab);
        MainTabView.SelectedItem = homeTab;

        _ = _homeViewModel.InitializeAsync();
    }

    private async void OnSessionOpenRequested(object? sender, SessionOpenRequestedEventArgs e)
    {
        // Check if tab for this session already exists
        foreach (TabViewItem tab in MainTabView.TabItems)
        {
            if (tab.Tag is Guid id && id == e.SessionId)
            {
                MainTabView.SelectedItem = tab;
                return;
            }
        }

        // Create a new session tab
        var vm = new SessionTabViewModel(e.SessionId, e.SessionName);
        vm.InterruptCommand = new RelayCommand(() => _notifier.RequestInterrupt(vm));
        vm.DetailsViewModel = new SessionDetailsViewModel(_sessionService, _executionRepo);

        var sessionTab = new SessionTab(vm);
        var tabItem = new TabViewItem
        {
            Header = e.SessionName,
            Tag = e.SessionId,
            Content = sessionTab
        };
        MainTabView.TabItems.Add(tabItem);
        MainTabView.SelectedItem = tabItem;

        if (e.Mode == SessionOpenMode.ViewOnly)
            return;

        // Set active tab in notifier so events route here
        _notifier.SetActiveTab(vm);

        try
        {
            vm.IsBusy = true;
            if (e.Mode == SessionOpenMode.RunNew)
            {
                vm.AppendLog($"Sessao criada: {e.SessionId}");
                await _sessionService.RunAsync(e.SessionId);
            }
            else if (e.Mode == SessionOpenMode.Resume)
            {
                vm.AppendLog($"Retomando sessao: {e.SessionId}");
                await _sessionService.ResumeAsync(e.SessionId);
            }

            vm.AppendLog("Sessao finalizada.");
        }
        catch (Exception ex)
        {
            vm.AppendLog($"Erro [{ex.GetType().Name}]: {ex.Message}");
            vm.AppendLog(ex.StackTrace ?? "");
        }
        finally
        {
            vm.IsBusy = false;
            _notifier.ClearActiveTab();
            await _homeViewModel.RefreshSessionsAsync();
        }
    }

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab.Content is SessionTab { ViewModel.IsBusy: true })
            return;

        sender.TabItems.Remove(args.Tab);
    }
}
