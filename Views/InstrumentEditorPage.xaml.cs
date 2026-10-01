using StringTuner.ViewModels;
using StringTuner.Core.Domain;
using System.Text.Json;

namespace StringTuner.Views;

public partial class InstrumentEditorPage : ContentPage
{
    private readonly InstrumentEditorViewModel _viewModel;
    private readonly Dictionary<InstrumentEditorViewModel.InstrumentDraft, Border> _instrumentRowViews = [];
    private readonly Dictionary<InstrumentEditorViewModel.StringDraft, VisualElement> _accidentalPanels = [];
    private bool _isClosing;
    private bool _suppressAutoScroll;

    public InstrumentEditorPage(InstrumentEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        viewModel.Saved += OnSaved;
        viewModel.NoteModeSelected += OnNoteModeSelected;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isClosing = false;
        _suppressAutoScroll = true;
        await _viewModel.InitializeAsync();
        await Task.Yield();
        _suppressAutoScroll = false;
    }

    public async Task PrepareForOpeningAsync()
    {
        _suppressAutoScroll = true;
        _instrumentRowViews.Clear();
        _accidentalPanels.Clear();
        await _viewModel.ReloadDraftAsync();
        await Task.Yield();
        _suppressAutoScroll = false;
    }

    protected override bool OnBackButtonPressed()
    {
        MainThread.BeginInvokeOnMainThread(async () => await ConfirmCancelAsync());
        return true;
    }

    private async void OnCancelClicked(object? sender, EventArgs eventArgs) => await ConfirmCancelAsync();

    private async void OnAddInstrumentClicked(object? sender, EventArgs eventArgs)
    {
        _suppressAutoScroll = true;
        var added = _viewModel.AddInstrument();
        await Task.Yield();
        if (_instrumentRowViews.TryGetValue(added, out var row))
        {
            await EditorScrollView.ScrollToAsync(row, ScrollToPosition.End, true);
        }

        _suppressAutoScroll = false;
    }

    private void OnInstrumentRowLoaded(object? sender, EventArgs eventArgs)
    {
        if (sender is Border { BindingContext: InstrumentEditorViewModel.InstrumentDraft instrument } border)
        {
            _instrumentRowViews[instrument] = border;
        }
    }

    private void OnAccidentalPanelLoaded(object? sender, EventArgs eventArgs)
    {
        if (sender is VisualElement { BindingContext: InstrumentEditorViewModel.StringDraft stringDraft } panel)
        {
            _accidentalPanels[stringDraft] = panel;
        }
    }

    private void OnNoteModeSelected(object? sender, InstrumentEditorViewModel.StringDraft stringDraft)
    {
        if (_suppressAutoScroll)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (!_accidentalPanels.TryGetValue(stringDraft, out var panel))
            {
                return;
            }

            for (var attempt = 0; attempt < 8 && (panel.Height <= 0 || EditorScrollView.Height <= 0); attempt++)
            {
                await Task.Delay(16);
            }

            if (!panel.IsVisible || panel.Height <= 0 || EditorScrollView.Height <= 0)
            {
                return;
            }

            const double edgePadding = 8;
            var panelTop = GetVerticalPositionInScrollContent(panel);
            var panelBottom = panelTop + panel.Height;
            var viewportTop = EditorScrollView.ScrollY;
            var viewportBottom = viewportTop + EditorScrollView.Height;
            var targetOffset = viewportTop;

            if (panelTop < viewportTop + edgePadding)
            {
                targetOffset = panelTop - edgePadding;
            }
            else if (panelBottom > viewportBottom - edgePadding)
            {
                targetOffset = panelBottom - EditorScrollView.Height + edgePadding;
            }

            var maximumOffset = Math.Max(0, EditorScrollView.ContentSize.Height - EditorScrollView.Height);
            targetOffset = Math.Clamp(targetOffset, 0, maximumOffset);
            if (Math.Abs(targetOffset - viewportTop) > 1)
            {
                await EditorScrollView.ScrollToAsync(0, targetOffset, false);
            }
        });
    }

    private double GetVerticalPositionInScrollContent(VisualElement element)
    {
        double position = 0;
        Element? current = element;
        while (current is not null && !ReferenceEquals(current, EditorScrollView))
        {
            if (current is VisualElement visualElement)
            {
                position += visualElement.Y;
            }

            current = current.Parent;
        }

        return position;
    }

    private void OnToneValueUnfocused(object? sender, FocusEventArgs eventArgs)
    {
        if (sender is Entry { BindingContext: InstrumentEditorViewModel.StringDraft stringDraft })
        {
            _viewModel.NormalizeToneValue(stringDraft);
        }
    }

    private async void OnImportClicked(object? sender, EventArgs eventArgs)
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Import instrument catalog",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = [".json"],
                    [DevicePlatform.Android] = ["application/json", "text/json", "application/octet-stream"]
                })
            });
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var imported = _viewModel.ParseImport(json);
            var proceed = await DisplayAlertAsync(
                "Import preview",
                $"The file contains {imported.Count} tuning(s). Choose how to apply it to the editor draft.",
                "Choose mode",
                "Cancel");
            if (!proceed)
            {
                return;
            }

            var mode = await DisplayActionSheetAsync("Import mode", "Cancel", null, "Replace draft", "Merge into draft");
            if (mode == "Replace draft")
            {
                _viewModel.ReplaceDraft(imported);
            }
            else if (mode == "Merge into draft")
            {
                await _viewModel.MergeDraftAsync(imported, ResolveMergeConflictAsync);
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _viewModel.ReportError($"Import failed: {exception.Message}");
        }
    }

    private async void OnExportClicked(object? sender, EventArgs eventArgs)
    {
        try
        {
            var json = _viewModel.ExportDraft();
            var exportPath = Path.Combine(FileSystem.CacheDirectory, $"instruments-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(exportPath, json, new System.Text.UTF8Encoding(false));
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export instrument catalog",
                File = new ShareFile(exportPath, "application/json")
            });
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or IOException or UnauthorizedAccessException)
        {
            _viewModel.ReportError($"Export failed: {exception.Message}");
        }
    }

    private async Task<bool?> ResolveMergeConflictAsync(InstrumentTuning imported)
    {
        var choice = await DisplayActionSheetAsync(
            $"{imported.InstrumentName} · {imported.TuningName} already exists.",
            "Cancel merge",
            null,
            "Keep current",
            "Use imported");
        return choice switch
        {
            "Keep current" => false,
            "Use imported" => true,
            _ => null
        };
    }

    private async Task ConfirmCancelAsync()
    {
        if (_isClosing)
        {
            return;
        }

        if (_viewModel.IsDirty)
        {
            var discard = await DisplayAlertAsync("Discard changes?", "Unsaved catalog changes will be discarded.", "Discard", "Keep editing");
            if (!discard)
            {
                return;
            }
        }

        _isClosing = true;
        await Navigation.PopModalAsync();
    }

    private async void OnSaved(object? sender, EventArgs eventArgs)
    {
        _isClosing = true;
        await Navigation.PopModalAsync();
    }
}
