using GuitarFlashcards.Core.Domain;
using GuitarFlashcards.Services;
using GuitarFlashcards.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace GuitarFlashcards.Views;

public sealed class LibraryPage : ContentPage
{
    private readonly FlashcardsViewModel _viewModel;
    private readonly ChordEditorPage _editorPage;
    private readonly VerticalStackLayout _libraryRows = new() { Spacing = 8 };
    private readonly Label _selectedChordName = new()
    {
        FontSize = 17,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#202A27")
    };
    private readonly ContentView _selectedChordDiagram = new();
    private readonly VerticalStackLayout _selectedChordPanel = new() { Spacing = 2 };
    private double _selectedDiagramScrollY;

    public LibraryPage(FlashcardsViewModel viewModel, ChordEditorPage editorPage)
    {
        _viewModel = viewModel;
        _editorPage = editorPage;
        Title = "Chord library";
        BackgroundColor = Color.FromArgb("#F4F3ED");
        BuildContent();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.InitializeAsync();
            RefreshLibrary();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Library unavailable", exception.Message, "OK");
        }
    }

    private void BuildContent()
    {
        var add = new Button { Text = "Add chord" };
        add.Clicked += async (_, _) =>
        {
            _editorPage.PrepareForAdd();
            await Navigation.PushModalAsync(_editorPage);
        };
        var import = new Button { Text = "Import" };
        import.Clicked += async (_, _) => await ImportAsync();
        var export = new Button { Text = "Export" };
        export.Clicked += async (_, _) => await ExportAsync();
        var close = new Button
        {
            Text = "Home",
            BackgroundColor = Color.FromArgb("#146C67"),
            TextColor = Colors.White
        };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();

        var titleRow = new Grid
        {
            Padding = new Thickness(16, 8),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        titleRow.Add(new Label
        {
            Text = "Chord library",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#202A27"),
            VerticalOptions = LayoutOptions.Center
        }, 0);
        titleRow.Add(close, 1);

        var commands = new HorizontalStackLayout
        {
            Padding = new Thickness(16, 2),
            Spacing = 8,
            Children = { add, import, export }
        };

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
                new RowDefinition(GridLength.Star)
            }
        };
        pageLayout.Add(titleRow, 0, 0);
        pageLayout.Add(commands, 0, 1);
        pageLayout.Add(_selectedChordPanel, 0, 2);
        pageLayout.Add(new MouseWheelScrollView { Content = _libraryRows, VerticalOptions = LayoutOptions.Fill }, 0, 3);
        Content = pageLayout;
    }

    private void RefreshLibrary()
    {
        _libraryRows.Children.Clear();
        foreach (var chord in _viewModel.Chords)
        {
            var duplicateName = _viewModel.Chords.Count(item => string.Equals(item.DisplayName, chord.DisplayName, StringComparison.OrdinalIgnoreCase)) > 1;
            var displayName = duplicateName ? $"{chord.DisplayName}  ({chord.Id.ToString("N")[..6]})" : chord.DisplayName;
            var identification = ChordNoteEngine.IdentifyFingering(chord.Fingering, InstrumentTuning.StandardGuitar);
            var nameButton = new Button
            {
                Text = $"{displayName}\n{identification.DisplayText}",
                HorizontalOptions = LayoutOptions.Fill,
                BackgroundColor = _viewModel.SelectedChord?.Id == chord.Id ? Color.FromArgb("#D7E5E1") : Color.FromArgb("#F4F3ED"),
                BorderColor = _viewModel.SelectedChord?.Id == chord.Id ? Color.FromArgb("#146C67") : Color.FromArgb("#CFD5D0"),
                BorderWidth = 1,
                TextColor = Color.FromArgb("#202A27"),
                Padding = new Thickness(4, 8)
            };
            nameButton.Clicked += (_, _) => _viewModel.SelectChord(chord);

            var enabled = new SettingSwitch { IsToggled = chord.Enabled, VerticalOptions = LayoutOptions.Center };
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
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
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
            _libraryRows.Add(new Border
            {
                Stroke = Color.FromArgb("#CFD5D0"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 3 },
                Content = row
            });
        }

        if (_viewModel.Chords.Count == 0)
        {
            _libraryRows.Add(new Label
            {
                Text = "No saved chords",
                TextColor = Color.FromArgb("#65716D"),
                Padding = new Thickness(4, 12)
            });
        }

        RefreshSelectedChord();
    }

    private void RefreshSelectedChord()
    {
        if (_selectedChordDiagram.Content is FingeringDiagramView previousDiagram)
        {
            _selectedDiagramScrollY = previousDiagram.CurrentScrollY;
        }

        if (_viewModel.SelectedChord is not ChordDefinition chord)
        {
            _selectedChordName.Text = "Fingering preview · select a chord";
            _selectedChordDiagram.Content = new FingeringDiagramView(
                GuitarFingering.Open(), 1, false, initialScrollY: _selectedDiagramScrollY);
            return;
        }

        _selectedChordName.Text = $"{chord.DisplayName} · {ChordNoteEngine.IdentifyFingering(chord.Fingering, InstrumentTuning.StandardGuitar).DisplayText}";
        _selectedChordDiagram.Content = new FingeringDiagramView(
            chord.Fingering, 1, false, initialScrollY: _selectedDiagramScrollY);
    }

    private async Task ImportAsync()
    {
        try
        {
            var imported = await LocalConfigurationStore.ReadChordLibraryImportAsync();
            var confirmed = await DisplayAlertAsync(
                "Replace chord library?",
                "Importing replaces all saved chords. Practice settings will be left unchanged. Continue?",
                "Replace",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            await _viewModel.ReplaceChordLibraryAsync(imported);
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
            var saved = await LocalConfigurationStore.ExportChordLibraryAsync(_viewModel.Configuration.Chords);
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

    private async Task DeleteAsync(ChordDefinition chord)
    {
        var confirmed = await DisplayAlertAsync("Delete chord?", $"Delete '{chord.DisplayName}' from the library?", "Delete", "Cancel");
        if (confirmed)
        {
            await _viewModel.DeleteChordAsync(chord.Id);
            RefreshLibrary();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(FlashcardsViewModel.SelectedChord)
            or nameof(FlashcardsViewModel.PlayingChordId))
        {
            MainThread.BeginInvokeOnMainThread(RefreshLibrary);
        }
    }
}