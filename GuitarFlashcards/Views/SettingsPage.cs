using System.Globalization;
using GuitarFlashcards.Core.Domain;
using GuitarFlashcards.Services;
using GuitarFlashcards.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace GuitarFlashcards.Views;

public sealed class SettingsPage : ContentPage
{
    private readonly FlashcardsViewModel _viewModel;
    private readonly ChordEditorPage _editorPage;
    private readonly VerticalStackLayout _libraryRows = new() { Spacing = 8 };
    private readonly Label _selectedChordName = new() { FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#202A27") };
    private readonly ContentView _selectedChordDiagram = new();
    private readonly VerticalStackLayout _selectedChordPanel = new() { Spacing = 2 };
    private readonly Entry _minimumDuration = new()
    {
        Keyboard = Keyboard.Numeric,
        WidthRequest = 105,
        TextColor = Color.FromArgb("#202A27"),
        PlaceholderColor = Color.FromArgb("#65716D"),
        BackgroundColor = Colors.White
    };
    private readonly Entry _maximumDuration = new()
    {
        Keyboard = Keyboard.Numeric,
        WidthRequest = 105,
        TextColor = Color.FromArgb("#202A27"),
        PlaceholderColor = Color.FromArgb("#65716D"),
        BackgroundColor = Colors.White
    };
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
    private bool _snappingStrumSlider;
    private readonly Switch _showFingering = new();
    private readonly Switch _playDuringRun = new();
    private readonly Label _validationMessage = new() { TextColor = Color.FromArgb("#A33D32") };
    private Button? _saveSettingsButton;
    private PracticeSettings? _savedSettings;
    private bool _loadingControls;

    public SettingsPage(FlashcardsViewModel viewModel, ChordEditorPage editorPage)
    {
        _viewModel = viewModel;
        _editorPage = editorPage;
        _minimumDuration.TextChanged += OnSettingsEdited;
        _maximumDuration.TextChanged += OnSettingsEdited;
        _showFingering.Toggled += OnSettingsEdited;
        _playDuringRun.Toggled += OnSettingsEdited;
        Title = "Settings and chord library";
        BackgroundColor = Color.FromArgb("#F4F3ED");
        BuildContent();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadControls();
        RefreshLibrary();
    }

    private void BuildContent()
    {
        _strumInterval.ValueChanged += OnStrumIntervalChanged;
        _saveSettingsButton = new Button { Text = "Save settings", IsEnabled = false };
        _saveSettingsButton.Clicked += async (_, _) => await SaveSettingsAsync();
        var import = new Button { Text = "Import JSON" };
        import.Clicked += async (_, _) => await ImportAsync();
        var export = new Button { Text = "Export JSON" };
        export.Clicked += async (_, _) => await ExportAsync();
        var add = new Button { Text = "Add chord" };
        add.Clicked += async (_, _) =>
        {
            _editorPage.PrepareForAdd();
            await Navigation.PushModalAsync(_editorPage);
        };
        var close = new Button
        {
            Text = "Close",
            BackgroundColor = Color.FromArgb("#146C67"),
            TextColor = Colors.White
        };
        close.Clicked += async (_, _) => await CloseAsync();

        var titleRow = new Grid
        {
            Padding = new Thickness(16, 8),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        titleRow.Add(new Label { Text = "Settings and library", FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#202A27"), VerticalOptions = LayoutOptions.Center }, 0);
        titleRow.Add(close, 1);

        var timingGrid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12
        };
        timingGrid.Add(new VerticalStackLayout { Spacing = 2, Children = { new Label { Text = "Minimum seconds", FontSize = 12, TextColor = Color.FromArgb("#65716D") }, _minimumDuration } }, 0);
        timingGrid.Add(new VerticalStackLayout { Spacing = 2, Children = { new Label { Text = "Maximum seconds", FontSize = 12, TextColor = Color.FromArgb("#65716D") }, _maximumDuration } }, 1);

        var toggles = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12
        };
        toggles.Add(ToggleRow("Show fingering", _showFingering), 0);
        toggles.Add(ToggleRow("Play chord during run", _playDuringRun), 1);

        var settingsBlock = new VerticalStackLayout
        {
            Padding = new Thickness(16, 4),
            Spacing = 4,
            Children =
            {
                new Label { Text = "Practice settings", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#202A27") },
                timingGrid,
                BuildStrumIntervalControl(),
                toggles,
                _validationMessage,
                new HorizontalStackLayout { Spacing = 6, Children = { import, export, _saveSettingsButton } }
            }
        };

        var libraryHeader = new Grid
        {
            Padding = new Thickness(16, 2),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        libraryHeader.Add(new Label { Text = "Chord library", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#202A27"), VerticalOptions = LayoutOptions.Center }, 0);
        libraryHeader.Add(add, 1);

        _selectedChordPanel.Children.Add(_selectedChordName);
        _selectedChordPanel.Children.Add(_selectedChordDiagram);
        _selectedChordPanel.Padding = new Thickness(12, 2);

        var pageLayout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        pageLayout.Add(titleRow, 0, 0);
        pageLayout.Add(settingsBlock, 0, 1);
        pageLayout.Add(libraryHeader, 0, 2);
        pageLayout.Add(_selectedChordPanel, 0, 3);
        pageLayout.Add(new ScrollView { Content = _libraryRows, VerticalOptions = LayoutOptions.Fill }, 0, 4);
        Content = pageLayout;
    }

    private static Grid DurationRow(string title, Entry entry)
    {
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        row.Add(new Label { Text = title, TextColor = Color.FromArgb("#202A27"), VerticalOptions = LayoutOptions.Center }, 0);
        row.Add(entry, 1);
        return row;
    }

    private static VerticalStackLayout ToggleRow(string title, Switch toggle)
    {
        var row = new VerticalStackLayout { Spacing = 1 };
        row.Add(new Label { Text = title, FontSize = 12, TextColor = Color.FromArgb("#202A27"), VerticalOptions = LayoutOptions.Center });
        row.Add(toggle);
        return row;
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

        var tickGrid = new Grid { ColumnSpacing = 0, HeightRequest = 5 };
        for (var tick = 0; tick <= 15; tick++)
        {
            tickGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            tickGrid.Add(new BoxView
            {
                WidthRequest = 1,
                HeightRequest = tick % 4 == 0 ? 5 : 3,
                Color = Color.FromArgb("#65716D"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Start
            }, tick);
        }

        return new VerticalStackLayout
        {
            Spacing = 0,
            Children = { heading, _strumInterval, tickGrid }
        };
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
        UpdateSaveSettingsState();
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
        _validationMessage.Text = string.Empty;
        _savedSettings = _viewModel.Settings;
        _loadingControls = false;
        UpdateSaveSettingsState();
    }

    private void OnSettingsEdited(object? sender, EventArgs eventArgs) => UpdateSaveSettingsState();

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
                || Math.Abs(_strumInterval.Value - _savedSettings.StrumIntervalSeconds) > 0.0001;
        }
    }

    private void UpdateSaveSettingsState()
    {
        if (!_loadingControls && _saveSettingsButton is not null)
        {
            _saveSettingsButton.IsEnabled = HasUnsavedSettings;
        }
    }

    private async Task SaveSettingsAsync()
    {
        var error = await _viewModel.SaveSettingsAsync(
            _minimumDuration.Text ?? string.Empty,
            _maximumDuration.Text ?? string.Empty,
            _showFingering.IsToggled,
            _playDuringRun.IsToggled,
            _strumInterval.Value);
        _validationMessage.Text = error ?? string.Empty;
        if (error is null)
        {
            _savedSettings = _viewModel.Settings;
            await Navigation.PopModalAsync();
        }
    }

    private async Task CloseAsync()
    {
        if (!HasUnsavedSettings)
        {
            await Navigation.PopModalAsync();
            return;
        }

        var action = await DisplayActionSheetAsync(
            "Unsaved practice settings",
            "Stay",
            "Discard",
            "Save");
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

    private async Task ImportAsync()
    {
        try
        {
            var imported = await LocalConfigurationStore.ReadImportAsync();
            var confirmed = await DisplayAlertAsync(
                "Replace configuration?",
                "Importing replaces all saved chords and practice settings. Continue?",
                "Replace",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            await _viewModel.ReplaceConfigurationAsync(imported);
            LoadControls();
            RefreshLibrary();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Import failed", exception.Message, "OK");
        }
    }

    private async Task ExportAsync()
    {
        try
        {
            var saved = await LocalConfigurationStore.ExportAsync(_viewModel.ExportJson(), "GuitarFlashcards.json");
            if (!saved)
            {
                _validationMessage.Text = "Export was cancelled.";
            }
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Export failed", exception.Message, "OK");
        }
    }

    private void RefreshLibrary()
    {
        _libraryRows.Children.Clear();
        foreach (var chord in _viewModel.Chords)
        {
            var duplicateName = _viewModel.Chords.Count(item => string.Equals(item.DisplayName, chord.DisplayName, StringComparison.OrdinalIgnoreCase)) > 1;
            var displayName = duplicateName ? $"{chord.DisplayName}  ({chord.Id.ToString("N")[..6]})" : chord.DisplayName;
            var nameButton = new Button
            {
                Text = chord.DerivedChordName is null ? displayName : $"{displayName}\n{chord.DerivedChordName}",
                HorizontalOptions = LayoutOptions.Fill,
                BackgroundColor = _viewModel.SelectedChord?.Id == chord.Id ? Color.FromArgb("#D7E5E1") : Color.FromArgb("#F4F3ED"),
                BorderColor = _viewModel.SelectedChord?.Id == chord.Id ? Color.FromArgb("#146C67") : Color.FromArgb("#CFD5D0"),
                BorderWidth = 1,
                TextColor = Color.FromArgb("#202A27"),
                Padding = new Thickness(4, 8)
            };
            nameButton.Clicked += (_, _) => _viewModel.SelectChord(chord);
            var enabled = new Switch { IsToggled = chord.Enabled, VerticalOptions = LayoutOptions.Center };
            enabled.Toggled += async (_, args) => await _viewModel.SetChordEnabledAsync(chord.Id, args.Value);
            var play = new Button
            {
                Text = _viewModel.PlayingChordId == chord.Id ? "Pause" : "Play",
                Padding = new Thickness(10, 6)
            };
            play.Clicked += async (_, _) => await _viewModel.ToggleChordPlaybackAsync(chord.Fingering, chord.Id);
            var edit = new Button { Text = "Edit", Padding = new Thickness(10, 6) };
            edit.Clicked += async (_, _) =>
            {
                _editorPage.PrepareForEdit(chord);
                await Navigation.PushModalAsync(_editorPage);
            };
            var delete = new Button
            {
                Text = "Delete",
                Padding = new Thickness(10, 6),
                BackgroundColor = Color.FromArgb("#146C67"),
                TextColor = Colors.White
            };
            delete.Clicked += async (_, _) => await DeleteAsync(chord);

            var identityRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 4
            };
            identityRow.Add(nameButton, 0);
            identityRow.Add(enabled, 1);
            var actions = new HorizontalStackLayout { Spacing = 4, Children = { play, edit, delete } };
            var row = new VerticalStackLayout
            {
                Padding = new Thickness(8, 5),
                Spacing = 2,
                BackgroundColor = Colors.White,
                Children = { identityRow, actions }
            };
            _libraryRows.Add(new Border { Stroke = Color.FromArgb("#CFD5D0"), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 3 }, Content = row });
        }

        if (_viewModel.Chords.Count == 0)
        {
            _libraryRows.Add(new Label { Text = "No saved chords", TextColor = Color.FromArgb("#65716D"), Padding = new Thickness(4, 12) });
        }

        RefreshSelectedChord();
    }

    private void RefreshSelectedChord()
    {
        if (_viewModel.SelectedChord is not ChordDefinition chord)
        {
            _selectedChordName.Text = "Fingering preview · select a chord";
            _selectedChordDiagram.Content = new FingeringDiagramView(GuitarFingering.Open(), 1, false);
            return;
        }

        _selectedChordName.Text = chord.DerivedChordName is null
            ? chord.DisplayName
            : $"{chord.DisplayName}  ·  {chord.DerivedChordName}";
        _selectedChordDiagram.Content = new FingeringDiagramView(chord.Fingering, 1, false);
        _selectedChordPanel.IsVisible = true;
    }

    private async Task DeleteAsync(ChordDefinition chord)
    {
        var confirmed = await DisplayAlertAsync("Delete chord?", $"Delete '{chord.DisplayName}' from the library?", "Delete", "Cancel");
        if (confirmed)
        {
            await _viewModel.DeleteChordAsync(chord.Id);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(FlashcardsViewModel.Configuration))
        {
            MainThread.BeginInvokeOnMainThread(RefreshLibrary);
        }
        else if (eventArgs.PropertyName == nameof(FlashcardsViewModel.SelectedChord))
        {
            MainThread.BeginInvokeOnMainThread(RefreshLibrary);
        }
        else if (eventArgs.PropertyName == nameof(FlashcardsViewModel.PlayingChordId))
        {
            MainThread.BeginInvokeOnMainThread(RefreshLibrary);
        }
    }
}