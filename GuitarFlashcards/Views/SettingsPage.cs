using System.Globalization;
using GuitarFlashcards.Core.Domain;
using GuitarFlashcards.Services;
using GuitarFlashcards.ViewModels;

namespace GuitarFlashcards.Views;

public sealed class SettingsPage : ContentPage
{
    private readonly FlashcardsViewModel _viewModel;
    private readonly Entry _minimumDuration = CreateDurationEntry();
    private readonly Entry _maximumDuration = CreateDurationEntry();
    private readonly Slider _strumInterval = new()
    {
        Minimum = 0.25,
        Maximum = 4.0,
        Value = 1.0,
        MinimumTrackColor = Color.FromArgb("#146C67"),
        MaximumTrackColor = Color.FromArgb("#AEBAB5"),
        ThumbColor = Color.FromArgb("#146C67")
    };
    private readonly Label _strumIntervalValue = new()
    {
        Text = "1.00 s",
        TextColor = Color.FromArgb("#202A27"),
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.End,
        VerticalTextAlignment = TextAlignment.Center
    };
    private readonly SettingSwitch _showFingering = new();
    private readonly SettingSwitch _playDuringRun = new();
    private readonly SettingSwitch _showNextChord = new() { IsToggled = true };
    private readonly Button _timedModeButton = new() { Text = "Timed" };
    private readonly Button _manualModeButton = new() { Text = "Manual" };
    private readonly Label _validationMessage = new() { TextColor = Color.FromArgb("#A33D32") };
    private Button? _saveButton;
    private PracticeSettings? _savedSettings;
    private PracticeMode _selectedMode = PracticeMode.Timed;
    private bool _loadingControls;
    private bool _snappingStrumSlider;

    public SettingsPage(FlashcardsViewModel viewModel)
    {
        _viewModel = viewModel;
        _minimumDuration.TextChanged += OnSettingsEdited;
        _maximumDuration.TextChanged += OnSettingsEdited;
        _showFingering.Toggled += OnSettingsEdited;
        _playDuringRun.Toggled += OnSettingsEdited;
        _showNextChord.Toggled += OnSettingsEdited;
        _strumInterval.ValueChanged += OnStrumIntervalChanged;
        _timedModeButton.Clicked += (_, _) => SetPracticeMode(PracticeMode.Timed);
        _manualModeButton.Clicked += (_, _) => SetPracticeMode(PracticeMode.Manual);
        Title = "Practice settings";
        BackgroundColor = Color.FromArgb("#F4F3ED");
        BuildContent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadControls();
    }

    private void BuildContent()
    {
        var closeButton = new Button
        {
            Text = "Close",
            BackgroundColor = Color.FromArgb("#146C67"),
            TextColor = Colors.White
        };
        closeButton.Clicked += async (_, _) => await CloseAsync();

        var titleRow = new Grid
        {
            Padding = new Thickness(16, 8),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        titleRow.Add(new Label
        {
            Text = "Practice settings",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#202A27"),
            VerticalOptions = LayoutOptions.Center
        }, 0);
        titleRow.Add(closeButton, 1);

        var timingGrid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12
        };
        timingGrid.Add(DurationControl("Minimum seconds", _minimumDuration), 0);
        timingGrid.Add(DurationControl("Maximum seconds", _maximumDuration), 1);

        var displayOptions = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12
        };
        displayOptions.Add(ToggleControl("Show fingering", _showFingering), 0);
        displayOptions.Add(ToggleControl("Play chord during run", _playDuringRun), 1);

        var modeButtons = new HorizontalStackLayout
        {
            Spacing = 0,
            Children = { _timedModeButton, _manualModeButton }
        };
        UpdateModeButtons();

        _saveButton = new Button { Text = "Save settings", IsEnabled = false };
        _saveButton.Clicked += async (_, _) => await SaveSettingsAsync();

        var settings = new VerticalStackLayout
        {
            Padding = new Thickness(16, 8),
            Spacing = 8,
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children = { CreateImportButton(), CreateExportButton() }
                },
                timingGrid,
                BuildStrumIntervalControl(),
                displayOptions,
                ToggleControl("Show next chord", _showNextChord),
                new Label { Text = "Practice mode", FontSize = 12, TextColor = Color.FromArgb("#202A27") },
                modeButtons,
                _validationMessage,
                new VerticalStackLayout
                {
                    Margin = new Thickness(0, 8, 0, 8),
                    Children = { _saveButton }
                }
            }
        };

        var page = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }
        };
        page.Add(titleRow, 0, 0);
        page.Add(new MouseWheelScrollView { Content = settings }, 0, 1);
        Content = page;
    }

    private static Entry CreateDurationEntry() => new()
    {
        Keyboard = Keyboard.Numeric,
        WidthRequest = 105,
        TextColor = Color.FromArgb("#202A27"),
        PlaceholderColor = Color.FromArgb("#65716D"),
        BackgroundColor = Colors.White
    };

    private Button CreateImportButton()
    {
        var button = new Button { Text = "Import" };
        button.Clicked += async (_, _) => await ImportSettingsAsync();
        return button;
    }

    private Button CreateExportButton()
    {
        var button = new Button { Text = "Export" };
        button.Clicked += async (_, _) => await ExportSettingsAsync();
        return button;
    }

    private static VerticalStackLayout DurationControl(string title, Entry entry)
    {
        var control = new VerticalStackLayout { Spacing = 2 };
        control.Add(new Label { Text = title, FontSize = 12, TextColor = Color.FromArgb("#65716D") });
        control.Add(entry);
        return control;
    }

    private static VerticalStackLayout ToggleControl(string title, Switch toggle)
    {
        var control = new VerticalStackLayout { Spacing = 1 };
        control.Add(new Label { Text = title, FontSize = 12, TextColor = Color.FromArgb("#202A27") });
        control.Add(toggle);
        return control;
    }

    private View BuildStrumIntervalControl()
    {
        var heading = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        heading.Add(new Label
        {
            Text = "Strum interval",
            FontSize = 12,
            TextColor = Color.FromArgb("#202A27"),
            VerticalOptions = LayoutOptions.Center
        }, 0);
        heading.Add(_strumIntervalValue, 1);

        var ticks = new Grid { ColumnSpacing = 0, HeightRequest = 5 };
        for (var tick = 0; tick <= 15; tick++)
        {
            ticks.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            ticks.Add(new BoxView
            {
                WidthRequest = 1,
                HeightRequest = tick % 4 == 0 ? 5 : 3,
                Color = Color.FromArgb("#65716D"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Start
            }, tick);
        }

        return new VerticalStackLayout { Spacing = 0, Children = { heading, _strumInterval, ticks } };
    }

    private void OnStrumIntervalChanged(object? sender, ValueChangedEventArgs eventArgs)
    {
        if (_snappingStrumSlider)
        {
            return;
        }

        var snapped = Math.Clamp(Math.Round(eventArgs.NewValue * 4, MidpointRounding.AwayFromZero) / 4, 0.25, 4.0);
        if (Math.Abs(snapped - eventArgs.NewValue) > 0.0001)
        {
            _snappingStrumSlider = true;
            _strumInterval.Value = snapped;
            _snappingStrumSlider = false;
        }

        _strumIntervalValue.Text = $"{snapped:0.00} s";
        UpdateSaveButton();
    }

    private void LoadControls()
    {
        _loadingControls = true;
        _minimumDuration.Text = _viewModel.MinimumDurationText;
        _maximumDuration.Text = _viewModel.MaximumDurationText;
        _strumInterval.Value = _viewModel.StrumIntervalSeconds;
        _strumIntervalValue.Text = $"{_viewModel.StrumIntervalSeconds:0.00} s";
        _showFingering.IsToggled = _viewModel.ShowFingering;
        _playDuringRun.IsToggled = _viewModel.PlayChordDuringRun;
        _showNextChord.IsToggled = _viewModel.ShowNextChord;
        _selectedMode = _viewModel.PracticeMode;
        UpdateModeButtons();
        _validationMessage.Text = string.Empty;
        _savedSettings = _viewModel.Settings;
        _loadingControls = false;
        UpdateSaveButton();
    }

    private bool HasUnsavedSettings
    {
        get
        {
            if (_savedSettings is null
                || !double.TryParse(_minimumDuration.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minimum)
                || !double.TryParse(_maximumDuration.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var maximum))
            {
                return true;
            }

            return minimum != _savedSettings.MinimumDurationSeconds
                || maximum != _savedSettings.MaximumDurationSeconds
                || _showFingering.IsToggled != _savedSettings.ShowFingering
                || _playDuringRun.IsToggled != _savedSettings.PlayChordDuringRun
                || _showNextChord.IsToggled != _savedSettings.ShowNextChord
                || _selectedMode != _savedSettings.Mode
                || Math.Abs(_strumInterval.Value - _savedSettings.StrumIntervalSeconds) > 0.0001;
        }
    }

    private void OnSettingsEdited(object? sender, EventArgs eventArgs) => UpdateSaveButton();

    private void SetPracticeMode(PracticeMode mode)
    {
        _selectedMode = mode;
        UpdateModeButtons();
        UpdateSaveButton();
    }

    private void UpdateModeButtons()
    {
        StyleModeButton(_timedModeButton, _selectedMode == PracticeMode.Timed);
        StyleModeButton(_manualModeButton, _selectedMode == PracticeMode.Manual);
    }

    private static void StyleModeButton(Button button, bool selected)
    {
        button.BackgroundColor = selected ? Color.FromArgb("#146C67") : Colors.White;
        button.TextColor = selected ? Colors.White : Color.FromArgb("#202A27");
        button.BorderColor = Color.FromArgb("#146C67");
        button.BorderWidth = 1;
        button.CornerRadius = 0;
    }

    private void UpdateSaveButton()
    {
        if (!_loadingControls && _saveButton is not null)
        {
            _saveButton.IsEnabled = HasUnsavedSettings;
        }
    }

    private async Task SaveSettingsAsync()
    {
        var error = await _viewModel.SaveSettingsAsync(
            _minimumDuration.Text ?? string.Empty,
            _maximumDuration.Text ?? string.Empty,
            _showFingering.IsToggled,
            _playDuringRun.IsToggled,
            _strumInterval.Value,
            _showNextChord.IsToggled,
            _selectedMode);
        _validationMessage.Text = error ?? string.Empty;
        if (error is null)
        {
            _savedSettings = _viewModel.Settings;
            await Navigation.PopModalAsync();
        }
    }

    private async Task ImportSettingsAsync()
    {
        try
        {
            var imported = await LocalConfigurationStore.ReadPracticeSettingsImportAsync();
            var confirmed = await DisplayAlertAsync(
                "Replace practice settings?",
                "Importing replaces practice timing, display, preview, and mode settings. The chord library will be left unchanged. Continue?",
                "Replace",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            await _viewModel.ReplacePracticeSettingsAsync(imported);
            LoadControls();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Import failed", exception.Message, "OK");
        }
    }

    private async Task ExportSettingsAsync()
    {
        try
        {
            var saved = await LocalConfigurationStore.ExportPracticeSettingsAsync(_viewModel.Settings);
            if (!saved)
            {
                await DisplayAlertAsync("Export", "Export was cancelled.", "OK");
            }
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Export failed", exception.Message, "OK");
        }
    }

    private async Task CloseAsync()
    {
        if (!HasUnsavedSettings)
        {
            await Navigation.PopModalAsync();
            return;
        }

        var action = await DisplayActionSheetAsync("Unsaved practice settings", "Stay", "Discard", "Save");
        if (action == "Save")
        {
            await SaveSettingsAsync();
        }
        else if (action == "Discard")
        {
            LoadControls();
            await Navigation.PopModalAsync();
        }
    }
}
