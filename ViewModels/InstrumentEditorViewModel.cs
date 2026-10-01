using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using StringTuner.Core.Domain;
using StringTuner.Services;

namespace StringTuner.ViewModels;

public sealed class InstrumentEditorViewModel : INotifyPropertyChanged
{
    public static IReadOnlyList<string> AvailablePitchKinds { get; } = ["Frequency", "Note", "MIDI"];
    private readonly IInstrumentCatalogProvider _catalogProvider;
    private readonly IInstrumentCatalogStore _catalogStore;
    private readonly ILogger<InstrumentEditorViewModel> _logger;
    private readonly double _referenceA4;
    private bool _isDirty;
    private bool _isSaving;
    private bool _isInitialized;
    private string? _statusMessage;
    private bool _statusIsError;

    public InstrumentEditorViewModel(
        IInstrumentCatalogProvider catalogProvider,
        IInstrumentCatalogStore catalogStore,
        ILogger<InstrumentEditorViewModel> logger)
    {
        _catalogProvider = catalogProvider;
        _catalogStore = catalogStore;
        _logger = logger;
        _referenceA4 = Preferences.Default.Get("TuningReferenceA4", 440d);
        InstrumentRows = [];
        PitchKinds = AvailablePitchKinds;
        AddInstrumentCommand = new Command(() => AddInstrument());
        ToggleExpandedCommand = new Command<InstrumentDraft>(instrument => instrument.IsExpanded = !instrument.IsExpanded);
        DeleteInstrumentCommand = new Command<InstrumentDraft>(instrument => DeleteInstrument(instrument));
        AddStringCommand = new Command<InstrumentDraft>(instrument => AddString(instrument));
        DeleteStringCommand = new Command<StringDraft>(stringDraft => DeleteString(stringDraft.Instrument, stringDraft));
        SaveCommand = new Command(async () => await SaveAsync(), () => !IsSaving);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? Saved;

    public event EventHandler<StringDraft>? NoteModeSelected;

    public ObservableCollection<InstrumentDraft> InstrumentRows { get; }

    public IReadOnlyList<string> PitchKinds { get; }

    public ICommand AddInstrumentCommand { get; }

    public ICommand ToggleExpandedCommand { get; }

    public ICommand DeleteInstrumentCommand { get; }

    public ICommand AddStringCommand { get; }

    public ICommand DeleteStringCommand { get; }

    public ICommand SaveCommand { get; }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
            {
                ((Command)SaveCommand).ChangeCanExecute();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasStatusMessage)));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return;
        }

        var result = await _catalogProvider.LoadAsync(cancellationToken);
        if (result.Error is not null || result.Instruments.Count == 0)
        {
            StatusIsError = true;
            StatusMessage = result.Error ?? "The catalog contains no tunings.";
            return;
        }

        ReplaceRows(result.Instruments);
        IsDirty = false;
        StatusMessage = null;
        _isInitialized = true;
    }

    public async Task ReloadDraftAsync(CancellationToken cancellationToken = default)
    {
        _isInitialized = false;
        await InitializeAsync(cancellationToken);
    }

    public void SetAccidental(StringDraft row, int cents)
    {
        var accidental = cents switch
        {
            -150 => "𝄵",
            -100 => "♭",
            -50 => "𝄳",
            50 => "𝄲",
            100 => "♯",
            150 => "𝄴",
            _ => throw new ArgumentOutOfRangeException(nameof(cents))
        };
        row.InsertAccidental(accidental);
    }

    public IReadOnlyList<InstrumentTuning> ParseImport(string json) => InstrumentCatalogJson.Deserialize(json);

    public void ReplaceDraft(IReadOnlyList<InstrumentTuning> imported)
    {
        ReplaceRows(imported);
        MarkDirty();
        SetStatus($"Replaced draft with {imported.Count} tuning(s).", false);
    }

    public async Task<bool> MergeDraftAsync(
        IReadOnlyList<InstrumentTuning> imported,
        Func<InstrumentTuning, Task<bool?>> resolveConflictAsync)
    {
        var current = BuildCatalog();
        var decisions = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
        foreach (var incoming in imported.Where(item => current.Any(existing => InstrumentCatalogMerge.HasSameIdentity(existing, item))))
        {
            var useImported = await resolveConflictAsync(incoming);
            if (useImported is null)
            {
                return false;
            }

            decisions[Identity(incoming)] = useImported;
        }

        var merged = InstrumentCatalogMerge.Merge(current, imported,
            (existing, incoming) => decisions[Identity(incoming)]);
        if (merged is null)
        {
            return false;
        }

        ReplaceRows(merged);
        MarkDirty();
        SetStatus($"Merged {imported.Count} tuning(s) into the draft.", false);
        return true;
    }

    public string ExportDraft() => InstrumentCatalogJson.Serialize(BuildCatalog());

    public void DeleteInstrument(InstrumentDraft instrument)
    {
        InstrumentRows.Remove(instrument);
        MarkDirty();
    }

    public void AddString(InstrumentDraft instrument)
    {
        var row = new StringDraft(this, instrument, $"String {instrument.Strings.Count + 1}", PitchRepresentation.Note, "C4");
        instrument.Strings.Add(row);
        SortStrings(instrument);
        MarkDirty();
    }

    public void DeleteString(InstrumentDraft instrument, StringDraft stringDraft)
    {
        instrument.Strings.Remove(stringDraft);
        MarkDirty();
    }

    public void NormalizeToneValue(StringDraft stringDraft) => stringDraft.NormalizeFrequencyValue();

    public void MarkDirty()
    {
        IsDirty = true;
        StatusMessage = null;
    }

    public void ReportError(string message) => SetStatus(message, true);

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (IsSaving)
        {
            return;
        }

        IsSaving = true;
        try
        {
            var catalog = BuildCatalog();
            var json = InstrumentCatalogJson.Serialize(catalog);
            _ = InstrumentCatalogJson.Deserialize(json);
            await _catalogStore.SaveAsync(json, cancellationToken);
            IsDirty = false;
            SetStatus("Catalog saved.", false);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Could not save instrument catalog draft.");
            SetStatus(exception.Message, true);
        }
        finally
        {
            IsSaving = false;
        }
    }

    public InstrumentDraft AddInstrument()
    {
        var row = CreateDraft(new InstrumentTuning(
            "New instrument",
            "New tuning",
            [new InstrumentString("String 1", 60, 0, NoteNameOverride: "C4", Representation: PitchRepresentation.Note)],
            "New instrument · New tuning"));
        row.IsExpanded = true;
        InstrumentRows.Add(row);
        MarkDirty();
        return row;
    }

    private List<InstrumentTuning> BuildCatalog()
    {
        if (InstrumentRows.Count == 0)
        {
            throw new InvalidDataException("Add at least one tuning before saving.");
        }

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<InstrumentTuning>(InstrumentRows.Count);
        foreach (var instrument in InstrumentRows)
        {
            if (string.IsNullOrWhiteSpace(instrument.DisplayName) ||
                string.IsNullOrWhiteSpace(instrument.InstrumentName) ||
                string.IsNullOrWhiteSpace(instrument.TuningName))
            {
                throw new InvalidDataException("Friendly name, instrument name, and tuning name are required.");
            }

            if (!identities.Add($"{instrument.InstrumentName.Trim()}\0{instrument.TuningName.Trim()}"))
            {
                throw new InvalidDataException($"Duplicate tuning identity '{instrument.InstrumentName} / {instrument.TuningName}'.");
            }

            if (instrument.Strings.Count == 0)
            {
                throw new InvalidDataException($"Tuning '{instrument.DisplayName}' must contain at least one string.");
            }

            var strings = new List<InstrumentString>(instrument.Strings.Count);
            for (var index = 0; index < instrument.Strings.Count; index++)
            {
                var draft = instrument.Strings[index];
                if (string.IsNullOrWhiteSpace(draft.Label))
                {
                    throw new InvalidDataException($"Every string in '{instrument.DisplayName}' needs a label.");
                }

                strings.Add(CreateString(draft, index));
            }

            result.Add(new InstrumentTuning(
                instrument.InstrumentName.Trim(),
                instrument.TuningName.Trim(),
                strings,
                instrument.DisplayName.Trim(),
                instrument.Metadata is null ? null : new Dictionary<string, string>(instrument.Metadata)));
        }

        return result;
    }

    private InstrumentString CreateString(StringDraft draft, int order)
    {
        var kind = ParseKind(draft.ToneKind);
        var frequency = PitchConversion.Resolve(kind, draft.ToneValue, _referenceA4);
        var midiValue = 69 + 12 * Math.Log2(frequency / 440);
        var midiNote = Math.Clamp((int)Math.Round(midiValue, MidpointRounding.AwayFromZero), 0, 127);
        double? frequencyOverride = kind == PitchRepresentation.Frequency
            ? Math.Round(frequency, 2, MidpointRounding.AwayFromZero)
            : null;
        string? noteOverride = kind == PitchRepresentation.Note ? draft.ToneValue.Trim() : null;
        var centsOffset = 0;
        if (noteOverride is not null)
        {
            (midiNote, centsOffset) = PitchConversion.ParseStrictNoteValue(noteOverride);
        }

        return new InstrumentString(draft.Label.Trim(), midiNote, order, frequencyOverride, noteOverride, centsOffset, kind);
    }

    private void ReplaceRows(IEnumerable<InstrumentTuning> instruments)
    {
        InstrumentRows.Clear();
        foreach (var instrument in instruments)
        {
            InstrumentRows.Add(CreateDraft(instrument));
        }
    }

    private InstrumentDraft CreateDraft(InstrumentTuning tuning)
    {
        var draft = new InstrumentDraft(this, tuning.DisplayName ?? $"{tuning.InstrumentName} · {tuning.TuningName}", tuning.InstrumentName, tuning.TuningName, tuning.Metadata);
        foreach (var item in tuning.Strings.OrderBy(item => item.Order))
        {
            var value = item.Representation switch
            {
                PitchRepresentation.MidiNote => item.MidiNote.ToString(CultureInfo.InvariantCulture),
                PitchRepresentation.Frequency => Math.Round(item.FrequencyOverride ?? item.TargetFrequency, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture),
                _ => item.NoteNameOverride ?? item.DisplayNote
            };
            draft.Strings.Add(new StringDraft(this, draft, item.Label, item.Representation, value));
        }

        SortStrings(draft);
        return draft;
    }

    private void OnPitchEdited(InstrumentDraft instrument, StringDraft stringDraft)
    {
        MarkDirty();
        try
        {
            _ = PitchConversion.Resolve(ParseKind(stringDraft.ToneKind), stringDraft.ToneValue, _referenceA4);
            stringDraft.SetValidationError(null);
            SortStrings(instrument);
        }
        catch (FormatException exception)
        {
            stringDraft.SetValidationError(exception.Message);
            MarkDirty();
        }
    }

    private void SortStrings(InstrumentDraft instrument)
    {
        var ordered = instrument.Strings
            .Select((item, previousIndex) =>
            {
                try
                {
                    return (Item: item, Index: previousIndex, Frequency: PitchConversion.Resolve(ParseKind(item.ToneKind), item.ToneValue, _referenceA4));
                }
                catch (FormatException)
                {
                    return (Item: item, Index: previousIndex, Frequency: double.NaN);
                }
            })
            .OrderBy(item => double.IsNaN(item.Frequency) ? 1 : 0)
            .ThenBy(item => double.IsNaN(item.Frequency) ? 0 : item.Frequency)
            .ThenBy(item => item.Index)
            .Select(item => item.Item)
            .ToArray();

        for (var index = 0; index < ordered.Length; index++)
        {
            var currentIndex = instrument.Strings.IndexOf(ordered[index]);
            if (currentIndex != index)
            {
                instrument.Strings.Move(currentIndex, index);
            }
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    private void NotifyNoteModeSelected(StringDraft row) => NoteModeSelected?.Invoke(this, row);

    private static string Identity(InstrumentTuning tuning) => $"{tuning.InstrumentName}\0{tuning.TuningName}";

    private static PitchRepresentation ParseKind(string kind) => kind switch
    {
        "Frequency" => PitchRepresentation.Frequency,
        "Note" => PitchRepresentation.Note,
        "MIDI" => PitchRepresentation.MidiNote,
        _ => throw new FormatException("Choose Frequency, Note, or MIDI.")
    };

    private static string KindName(PitchRepresentation kind) => kind switch
    {
        PitchRepresentation.Frequency => "Frequency",
        PitchRepresentation.Note => "Note",
        _ => "MIDI"
    };

    private static bool SetProperty<T>(object instance, ref T field, T value, PropertyChangedEventHandler? changed, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        changed?.Invoke(instance, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public sealed class InstrumentDraft : INotifyPropertyChanged
    {
        private readonly InstrumentEditorViewModel _owner;
        private string _displayName;
        private string _instrumentName;
        private string _tuningName;
        private bool _isExpanded;

        internal InstrumentDraft(
            InstrumentEditorViewModel owner,
            string displayName,
            string instrumentName,
            string tuningName,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            _owner = owner;
            _displayName = displayName;
            _instrumentName = instrumentName;
            _tuningName = tuningName;
            Metadata = metadata is null ? null : new Dictionary<string, string>(metadata);
            Strings = [];
            ToggleExpandedCommand = new Command(() => IsExpanded = !IsExpanded);
            DeleteCommand = new Command(() => owner.DeleteInstrument(this));
            AddStringCommand = new Command(() => owner.AddString(this));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<StringDraft> Strings { get; }

        public IReadOnlyDictionary<string, string>? Metadata { get; }

        public ICommand ToggleExpandedCommand { get; }

        public ICommand DeleteCommand { get; }

        public ICommand AddStringCommand { get; }

        public string DisplayName { get => _displayName; set { if (SetProperty(ref _displayName, value)) _owner.MarkDirty(); } }

        public string InstrumentName
        {
            get => _instrumentName;
            set
            {
                var followsIdentity = string.Equals(_displayName, FriendlyNameFor(_instrumentName, _tuningName), StringComparison.Ordinal);
                if (SetProperty(ref _instrumentName, value))
                {
                    if (followsIdentity) DisplayName = FriendlyNameFor(_instrumentName, _tuningName);
                    _owner.MarkDirty();
                }
            }
        }

        public string TuningName
        {
            get => _tuningName;
            set
            {
                var followsIdentity = string.Equals(_displayName, FriendlyNameFor(_instrumentName, _tuningName), StringComparison.Ordinal);
                if (SetProperty(ref _tuningName, value))
                {
                    if (followsIdentity) DisplayName = FriendlyNameFor(_instrumentName, _tuningName);
                    _owner.MarkDirty();
                }
            }
        }

        public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

        public string ExpandGlyph => IsExpanded ? "⌄" : "›";

        private static string FriendlyNameFor(string instrumentName, string tuningName) => $"{instrumentName} · {tuningName}";

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (!InstrumentEditorViewModel.SetProperty(this, ref field, value, PropertyChanged, propertyName)) return false;
            if (propertyName == nameof(IsExpanded)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExpandGlyph)));
            return true;
        }
    }

    public sealed class StringDraft : INotifyPropertyChanged
    {
        private readonly InstrumentEditorViewModel _owner;
        private readonly InstrumentDraft _instrument;
        private string _label;
        private string _toneKind;
        private string _toneValue;
        private string? _validationError;

        internal StringDraft(InstrumentEditorViewModel owner, InstrumentDraft instrument, string label, PitchRepresentation kind, string value)
        {
            _owner = owner;
            _instrument = instrument;
            _label = label;
            _toneKind = KindName(kind);
            _toneValue = value;
            DeleteCommand = new Command(() => owner.DeleteString(instrument, this));
            QuarterFlatCommand = new Command(() => _owner.SetAccidental(this, -50));
            QuarterSharpCommand = new Command(() => _owner.SetAccidental(this, 50));
            FlatCommand = new Command(() => _owner.SetAccidental(this, -100));
            SharpCommand = new Command(() => _owner.SetAccidental(this, 100));
            ThreeQuarterFlatCommand = new Command(() => _owner.SetAccidental(this, -150));
            ThreeQuarterSharpCommand = new Command(() => _owner.SetAccidental(this, 150));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ICommand DeleteCommand { get; }

        internal InstrumentDraft Instrument => _instrument;

        public bool IsNote => _toneKind == "Note";

        public ICommand QuarterFlatCommand { get; }

        public ICommand QuarterSharpCommand { get; }

        public ICommand FlatCommand { get; }

        public ICommand SharpCommand { get; }

        public ICommand ThreeQuarterFlatCommand { get; }

        public ICommand ThreeQuarterSharpCommand { get; }

        public string Label { get => _label; set { if (SetProperty(ref _label, value)) _owner.MarkDirty(); } }

        public string ToneKind
        {
            get => _toneKind;
            set
            {
                if (value == _toneKind) return;
                try
                {
                    var sourceValue = _toneKind == "Note" ? PitchConversion.NormalizeEditableNote(_toneValue) : _toneValue;
                    var converted = PitchConversion.Convert(ParseKind(_toneKind), sourceValue, ParseKind(value), _owner._referenceA4);
                    _toneKind = value;
                    _toneValue = converted;
                    _validationError = null;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToneKind)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToneValue)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNote)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValidationError)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasValidationError)));
                    _owner.OnPitchEdited(_instrument, this);
                    if (_toneKind == "Note")
                    {
                        _owner.NotifyNoteModeSelected(this);
                    }
                }
                catch (FormatException exception)
                {
                    SetValidationError(exception.Message);
                }
            }
        }

        public string ToneValue
        {
            get => _toneValue;
            set
            {
                if (SetProperty(ref _toneValue, value))
                {
                    _owner.OnPitchEdited(_instrument, this);
                }
            }
        }

        public string? ValidationError { get => _validationError; private set => SetProperty(ref _validationError, value); }

        public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationError);

        internal void SetValidationError(string? error)
        {
            ValidationError = error;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasValidationError)));
        }

        internal void NormalizeFrequencyValue()
        {
            if (_toneKind == "Note")
            {
                try
                {
                    ToneValue = PitchConversion.NormalizeEditableNote(_toneValue);
                }
                catch (FormatException exception)
                {
                    SetValidationError(exception.Message);
                }

                return;
            }

            if (_toneKind != "Frequency" ||
                !double.TryParse(_toneValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var frequency) ||
                !double.IsFinite(frequency) || frequency <= 0) return;

            ToneValue = Math.Round(frequency, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
        }

        internal void InsertAccidental(string accidental)
        {
            var runes = _toneValue.Trim().EnumerateRunes().ToArray();
            if (runes.Length < 2 || _toneKind != "Note")
            {
                SetValidationError("Enter a note letter and octave before choosing an accidental.");
                return;
            }

            var accidentalValues = new HashSet<int> { 0x266F, 0x266D, 0x1D132, 0x1D133, 0x1D134, 0x1D135 };
            var octaveStart = accidentalValues.Contains(runes[1].Value) ? 2 : 1;
            var octave = string.Concat(runes.Skip(octaveStart).Select(rune => rune.ToString()));
            ToneValue = $"{runes[0]}{accidental}{octave}";
        }

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) =>
            InstrumentEditorViewModel.SetProperty(this, ref field, value, PropertyChanged, propertyName);
    }
}