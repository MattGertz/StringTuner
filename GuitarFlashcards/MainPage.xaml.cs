using GuitarFlashcards.ViewModels;
using GuitarFlashcards.Views;
#if WINDOWS
using WinUIElement = Microsoft.UI.Xaml.UIElement;
using WinUIKeyEventArgs = Microsoft.UI.Xaml.Input.KeyRoutedEventArgs;
using WinUIKeyEventHandler = Microsoft.UI.Xaml.Input.KeyEventHandler;
#endif
namespace GuitarFlashcards;

public partial class MainPage : ContentPage
{
    private readonly FlashcardsViewModel _viewModel;
    private readonly IServiceProvider _services;
    private SettingsPage? _settingsPage;
    private LibraryPage? _libraryPage;
#if WINDOWS
    private WinUIElement? _keyboardEventTarget;
    private WinUIKeyEventHandler? _keyboardEventHandler;
#endif
    public MainPage(FlashcardsViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _services = services;
        BindingContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if WINDOWS
        if (_keyboardEventTarget is not null && _keyboardEventHandler is not null)
        {
            _keyboardEventTarget.RemoveHandler(WinUIElement.PreviewKeyDownEvent, _keyboardEventHandler);
        }

        _keyboardEventTarget = Handler?.PlatformView as WinUIElement;
        if (_keyboardEventTarget is not null)
        {
            _keyboardEventHandler = OnWindowsPreviewKeyDown;
            _keyboardEventTarget.AddHandler(WinUIElement.PreviewKeyDownEvent, _keyboardEventHandler, handledEventsToo: true);
        }
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.InitializeAsync();
            RefreshPracticeDiagram();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Configuration unavailable", exception.Message, "OK");
        }
    }

    private async void OnSettingsClicked(object? sender, EventArgs eventArgs)
    {
        _settingsPage ??= _services.GetRequiredService<SettingsPage>();
        await Navigation.PushModalAsync(_settingsPage);
    }

    private async void OnLibraryClicked(object? sender, EventArgs eventArgs)
    {
        _libraryPage ??= _services.GetRequiredService<LibraryPage>();
        await Navigation.PushModalAsync(_libraryPage);
    }

    private async void OnStartStopClicked(object? sender, EventArgs eventArgs)
    {
        await _viewModel.TogglePracticeAsync();
    }

#if WINDOWS
    private void OnWindowsPreviewKeyDown(object sender, WinUIKeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Windows.System.VirtualKey.Space && _viewModel.IsRunning && _viewModel.IsManualPractice)
        {
            _viewModel.AdvancePractice();
            eventArgs.Handled = true;
        }
    }
#endif

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(FlashcardsViewModel.PracticeFingering)
            or nameof(FlashcardsViewModel.ShowPracticeFingering))
        {
            RefreshPracticeDiagram();
        }
    }

    private void RefreshPracticeDiagram()
    {
        PracticeDiagramHost.Content = _viewModel.ShowPracticeFingering
            ? new FingeringDiagramView(_viewModel.PracticeFingering, 1, false, autoScrollToChord: true, showScrollBar: false)
            : null;
    }
}