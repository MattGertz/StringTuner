using System.Globalization;
using GuitarFlashcards.Core.Domain;
using GuitarFlashcards.ViewModels;
#if WINDOWS
using Windows.System;
using WinUIKeyEventArgs = Microsoft.UI.Xaml.Input.KeyRoutedEventArgs;
using WinUIElement = Microsoft.UI.Xaml.UIElement;
#endif

namespace GuitarFlashcards.Views;

public sealed class ChordEditorPage : ContentPage
{
    private readonly FlashcardsViewModel _viewModel;
    private readonly Button _playButton = new() { Text = "Play", HorizontalOptions = LayoutOptions.Fill };
    private readonly Entry _nameEntry = new()
    {
        Placeholder = "Chord name (auto from fingering)",
        FontSize = 20,
        TextColor = Color.FromArgb("#202A27"),
        PlaceholderColor = Color.FromArgb("#65716D"),
        BackgroundColor = Colors.White
    };
    private readonly ContentView _diagramHost = new();
    private readonly Label _identification = new() { TextColor = Color.FromArgb("#65716D"), HorizontalTextAlignment = TextAlignment.Center };
    private readonly List<Button> _fingerButtons = [];
    private double _diagramScrollY;
    private GuitarFingering _draft = GuitarFingering.Open();
    private Guid? _editingId;
    private int _selectedFinger = 1;
    private bool _displayNameManuallyOverridden;
    private bool _settingNameProgrammatically;
#if WINDOWS
    private WinUIElement? _keyboardEventTarget;
#endif

    public ChordEditorPage(FlashcardsViewModel viewModel)
    {
        _viewModel = viewModel;
        Title = "Chord editor";
        BackgroundColor = Color.FromArgb("#F4F3ED");
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _nameEntry.TextChanged += OnDisplayNameChanged;
        BuildContent();
    }

    public void PrepareForAdd()
    {
        _editingId = null;
        _selectedFinger = 1;
        _draft = GuitarFingering.Open();
        _diagramScrollY = 0;
        _diagramHost.Content = null;
        _displayNameManuallyOverridden = false;
        SetDisplayName(string.Empty);
        UpdateFingerButtonStyles();
        RefreshDiagram();
    }

    public void PrepareForEdit(ChordDefinition chord)
    {
        _editingId = chord.Id;
        _draft = chord.Fingering;
        _diagramScrollY = 0;
        _diagramHost.Content = null;
        _displayNameManuallyOverridden = true;
        SetDisplayName(chord.DisplayName);
        RefreshDiagram();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if WINDOWS
        if (_keyboardEventTarget is not null)
        {
            _keyboardEventTarget.KeyDown -= OnWindowsKeyDown;
        }

        _keyboardEventTarget = Handler?.PlatformView as WinUIElement;
        if (_keyboardEventTarget is not null)
        {
            _keyboardEventTarget.KeyDown += OnWindowsKeyDown;
        }
#endif
    }

    private void BuildContent()
    {
        _fingerButtons.Clear();
        var fingers = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center };
        for (var finger = 1; finger <= 4; finger++)
        {
            var selected = finger;
            var button = new Button
            {
                Text = finger.ToString(CultureInfo.InvariantCulture),
                WidthRequest = 44,
                HeightRequest = 44,
                CornerRadius = 22,
                Command = new Command(() => SetSelectedFinger(selected))
            };
            _fingerButtons.Add(button);
            fingers.Add(button);
        }

        _playButton.Clicked += async (_, _) => await _viewModel.ToggleChordPlaybackAsync(_draft, _editingId);
        var saveButton = new Button { Text = "Save", BackgroundColor = Color.FromArgb("#146C67"), TextColor = Colors.White, FontAttributes = FontAttributes.Bold };
        saveButton.Clicked += async (_, _) => await SaveAsync();
        var cancelButton = new Button { Text = "Cancel", BackgroundColor = Color.FromArgb("#F4F3ED"), TextColor = Color.FromArgb("#202A27") };
        cancelButton.Clicked += async (_, _) => await Navigation.PopModalAsync();

        var editorContent = new VerticalStackLayout
        {
            Padding = new Thickness(20, 14),
            Spacing = 12,
            Children =
            {
                new Label { Text = "Chord details", FontSize = 23, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#202A27") },
                _nameEntry,
                new Label { Text = "Fretting finger", FontSize = 14, TextColor = Color.FromArgb("#65716D") },
                fingers,
                _diagramHost,
                _identification
            }
        };

        var actions = new Grid
        {
            Padding = new Thickness(16, 8),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 8
        };
        actions.Add(cancelButton, 0);
        actions.Add(_playButton, 1);
        actions.Add(saveButton, 2);

        var pageLayout = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) }
        };
        pageLayout.Add(new MouseWheelScrollView { Content = editorContent }, 0, 0);
        pageLayout.Add(actions, 0, 1);
        Content = pageLayout;
        RefreshDiagram();
    }

    private void SetSelectedFinger(int finger)
    {
        _selectedFinger = Math.Clamp(finger, 1, 4);
        UpdateFingerButtonStyles();
    }

#if WINDOWS
    private void OnWindowsKeyDown(object sender, WinUIKeyEventArgs eventArgs)
    {
        if (eventArgs.Key == VirtualKey.Left)
        {
            SetSelectedFinger(_selectedFinger - 1);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == VirtualKey.Right)
        {
            SetSelectedFinger(_selectedFinger + 1);
            eventArgs.Handled = true;
        }
    }
#endif

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(FlashcardsViewModel.IsAudioPlaying) or nameof(FlashcardsViewModel.PlayingChordId))
        {
            MainThread.BeginInvokeOnMainThread(() =>
                _playButton.Text = _viewModel.IsAudioPlaying && (_editingId is null || _viewModel.PlayingChordId == _editingId) ? "Pause" : "Play");
        }
    }

    private void UpdateFingerButtonStyles()
    {
        for (var index = 0; index < _fingerButtons.Count; index++)
        {
            var selected = index + 1 == _selectedFinger;
            _fingerButtons[index].BackgroundColor = selected ? Color.FromArgb("#146C67") : Colors.White;
            _fingerButtons[index].TextColor = selected ? Colors.White : Color.FromArgb("#202A27");
        }
    }

    private void RefreshDiagram()
    {
        if (_diagramHost.Content is FingeringDiagramView previousDiagram)
        {
            _diagramScrollY = previousDiagram.CurrentScrollY;
        }

        _diagramHost.Content = new FingeringDiagramView(
            _draft,
            _selectedFinger,
            true,
            (stringIndex, fret) =>
            {
                var current = _draft.Strings[stringIndex];
                var clearingExistingPosition = current.State == StringNoteState.Fretted && current.Fret == fret;
                if (clearingExistingPosition && current.Finger is int existingFinger)
                {
                    SetSelectedFinger(existingFinger);
                }

                _draft = FingeringEditor.SetFrettedPosition(_draft, stringIndex, fret, _selectedFinger);
                if (!clearingExistingPosition)
                {
                    SetSelectedFinger(_selectedFinger == 4 ? 1 : _selectedFinger + 1);
                }

                RefreshDiagram();
            },
            (stringIndex, state) =>
            {
                _draft = FingeringEditor.SetStringState(_draft, stringIndex, state);
                RefreshDiagram();
            },
            initialScrollY: _diagramScrollY);

        var result = ChordNoteEngine.IdentifyFingering(_draft, InstrumentTuning.StandardGuitar);
        _identification.Text = result.DisplayText;
        if (!_displayNameManuallyOverridden)
        {
            SetDisplayName(result.Name ?? string.Empty);
        }
    }

    private void OnDisplayNameChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        if (_settingNameProgrammatically)
        {
            return;
        }

        _displayNameManuallyOverridden = !string.IsNullOrWhiteSpace(eventArgs.NewTextValue);
        if (!_displayNameManuallyOverridden)
        {
            var notes = ChordNoteEngine.DeriveNotes(_draft, InstrumentTuning.StandardGuitar);
            var bass = notes.MinBy(note => note.MidiNote)?.PitchClass;
            SetDisplayName(ChordNoteEngine.Identify(notes.Select(note => note.PitchClass).ToArray(), bass).Name ?? string.Empty);
        }
    }

    private void SetDisplayName(string value)
    {
        if (_nameEntry.Text == value)
        {
            return;
        }

        _settingNameProgrammatically = true;
        _nameEntry.Text = value;
        _settingNameProgrammatically = false;
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_nameEntry.Text))
        {
            await DisplayAlertAsync("Name required", "Enter a display name for this chord.", "OK");
            return;
        }

        try
        {
            if (_editingId is Guid id)
            {
                await _viewModel.UpdateChordAsync(id, _nameEntry.Text, _draft);
            }
            else
            {
                await _viewModel.AddChordAsync(_nameEntry.Text, _draft);
            }

            await Navigation.PopModalAsync();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Could not save chord", exception.Message, "OK");
        }
    }
}