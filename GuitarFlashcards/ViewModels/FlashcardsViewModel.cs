using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using GuitarFlashcards.Core.Domain;
using GuitarFlashcards.Core.Practice;
using GuitarFlashcards.Services;

namespace GuitarFlashcards.ViewModels;

public sealed class FlashcardsViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly LocalConfigurationStore _store;
    private readonly FlashcardAudioPlayer _audioPlayer;
    private readonly PracticeEngine _practiceEngine = new();
    private readonly SemaphoreSlim _configurationGate = new(1, 1);
    private CancellationTokenSource? _runCancellation;
    private AppConfiguration _configuration = AppConfiguration.Default;
    private ChordDefinition? _practiceChord;
    private ChordDefinition? _selectedChord;
    private Guid? _playingChordId;
    private bool _isRunning;
    private bool _isInitialized;
    private string _countdownText = string.Empty;
    private string _statusText = "Add a chord in the library to begin.";
    private string? _errorMessage;
    private string _minimumDurationText = "2.0";
    private string _maximumDurationText = "4.0";

    public FlashcardsViewModel(LocalConfigurationStore store, FlashcardAudioPlayer audioPlayer)
    {
        _store = store;
        _audioPlayer = audioPlayer;
        _audioPlayer.PlaybackStateChanged += OnAudioPlaybackStateChanged;
        Chords = [];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ChordDefinition> Chords { get; }

    public AppConfiguration Configuration
    {
        get => _configuration;
        private set => SetProperty(ref _configuration, value);
    }

    public PracticeSettings Settings => Configuration.Settings;

    public ChordDefinition? PracticeChord
    {
        get => _practiceChord;
        private set
        {
            if (SetProperty(ref _practiceChord, value))
            {
                OnPropertyChanged(nameof(PracticeChordName));
                OnPropertyChanged(nameof(PracticeDerivedName));
                OnPropertyChanged(nameof(PracticeFingering));
                OnPropertyChanged(nameof(ShowPracticeFingering));
            }
        }
    }

    public ChordDefinition? SelectedChord
    {
        get => _selectedChord;
        private set => SetProperty(ref _selectedChord, value);
    }

    public Guid? PlayingChordId
    {
        get => _playingChordId;
        private set => SetProperty(ref _playingChordId, value);
    }

    public bool IsAudioPlaying => _audioPlayer.IsPlaying;

    private void OnAudioPlaybackStateChanged(object? sender, EventArgs eventArgs)
    {
        if (!_audioPlayer.IsPlaying)
        {
            PlayingChordId = null;
        }

        OnPropertyChanged(nameof(IsAudioPlaying));
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(StartButtonText));
                OnPropertyChanged(nameof(CanStartPractice));
                OnPropertyChanged(nameof(CanShowNoChordsMessage));
            }
        }
    }

    public string CountdownText
    {
        get => _countdownText;
        private set => SetProperty(ref _countdownText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string MinimumDurationText
    {
        get => _minimumDurationText;
        set => SetProperty(ref _minimumDurationText, value);
    }

    public string MaximumDurationText
    {
        get => _maximumDurationText;
        set => SetProperty(ref _maximumDurationText, value);
    }

    public double StrumIntervalSeconds => Settings.StrumIntervalSeconds;

    public bool ShowFingering => Settings.ShowFingering;

    public bool PlayChordDuringRun => Settings.PlayChordDuringRun;

    public bool HasEnabledChords => Configuration.Chords.Any(chord => chord.Enabled);

    public bool CanStartPractice => IsRunning || HasEnabledChords;

    public bool CanShowNoChordsMessage => !HasEnabledChords && !IsRunning;

    public string StartButtonText => IsRunning ? "Stop" : "Start practice";

    public string PracticeChordName => PracticeChord?.DisplayName ?? (HasEnabledChords ? "Ready" : "No enabled chords");

    public string PracticeDerivedName => PracticeChord?.DerivedChordName is string name
        ? $"Identified as {name}"
        : PracticeChord is null ? string.Empty : "Chord not identified";

    public GuitarFingering PracticeFingering => PracticeChord?.Fingering ?? GuitarFingering.Open();

    public bool ShowPracticeFingering => ShowFingering;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return;
        }

        Configuration = await _store.LoadAsync(cancellationToken);
        UpdateSettingsText();
        RefreshChords();
        _isInitialized = true;
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(ShowFingering));
        OnPropertyChanged(nameof(PlayChordDuringRun));
        OnPropertyChanged(nameof(HasEnabledChords));
        OnPropertyChanged(nameof(CanStartPractice));
        OnPropertyChanged(nameof(PracticeChordName));
    }

    public void SelectChord(ChordDefinition? chord) => SelectedChord = chord;

    public async Task<ChordDefinition> AddChordAsync(string displayName, GuitarFingering fingering, CancellationToken cancellationToken = default)
    {
        await _configurationGate.WaitAsync(cancellationToken);
        try
        {
            var library = new ChordLibrary(Configuration.Chords);
            var chord = library.Add(displayName, fingering);
            await PersistAsync(Configuration with { Chords = library.Chords }, cancellationToken);
            SelectedChord = Chords.FirstOrDefault(item => item.Id == chord.Id);
            return SelectedChord ?? chord;
        }
        finally
        {
            _configurationGate.Release();
        }
    }

    public async Task<ChordDefinition> UpdateChordAsync(Guid id, string displayName, GuitarFingering fingering, CancellationToken cancellationToken = default)
    {
        await _configurationGate.WaitAsync(cancellationToken);
        try
        {
            var library = new ChordLibrary(Configuration.Chords);
            var chord = library.Update(id, displayName, fingering);
            await PersistAsync(Configuration with { Chords = library.Chords }, cancellationToken);
            SelectedChord = Chords.FirstOrDefault(item => item.Id == chord.Id);
            return SelectedChord ?? chord;
        }
        finally
        {
            _configurationGate.Release();
        }
    }

    public async Task DeleteChordAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _configurationGate.WaitAsync(cancellationToken);
        try
        {
            var library = new ChordLibrary(Configuration.Chords);
            library.Delete(id);
            await PersistAsync(Configuration with { Chords = library.Chords }, cancellationToken);
            if (SelectedChord?.Id == id)
            {
                SelectedChord = null;
            }
        }
        finally
        {
            _configurationGate.Release();
        }
    }

    public async Task SetChordEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
    {
        await _configurationGate.WaitAsync(cancellationToken);
        try
        {
            var library = new ChordLibrary(Configuration.Chords);
            library.SetEnabled(id, enabled);
            await PersistAsync(Configuration with { Chords = library.Chords }, cancellationToken);
            if (IsRunning && !HasEnabledChords)
            {
                StatusText = "Practice stopped because no chords are enabled.";
                await StopPracticeAsync();
            }
        }
        finally
        {
            _configurationGate.Release();
        }
    }

    public async Task<string?> SaveSettingsAsync(
        string minimumText,
        string maximumText,
        bool showFingering,
        bool playChordDuringRun,
        double strumIntervalSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!double.TryParse(minimumText, NumberStyles.Float, CultureInfo.InvariantCulture, out var minimum)
            || !double.TryParse(maximumText, NumberStyles.Float, CultureInfo.InvariantCulture, out var maximum))
        {
            return "Enter valid numeric durations using a period for the decimal separator.";
        }

        var settings = new PracticeSettings(minimum, maximum, showFingering, playChordDuringRun)
        {
            StrumIntervalSeconds = strumIntervalSeconds
        };
        var candidate = Configuration with { Settings = settings };
        try
        {
            ConfigurationJson.Validate(candidate);
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }

        await _configurationGate.WaitAsync(cancellationToken);
        try
        {
            await PersistAsync(candidate, cancellationToken);
            MinimumDurationText = minimum.ToString("0.###", CultureInfo.InvariantCulture);
            MaximumDurationText = maximum.ToString("0.###", CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(StrumIntervalSeconds));
            OnPropertyChanged(nameof(ShowFingering));
            OnPropertyChanged(nameof(PlayChordDuringRun));
            OnPropertyChanged(nameof(ShowPracticeFingering));
            return null;
        }
        finally
        {
            _configurationGate.Release();
        }
    }

    public async Task ReplaceConfigurationAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ConfigurationJson.Validate(configuration);
        await _configurationGate.WaitAsync(cancellationToken);
        try
        {
            await PersistAsync(configuration, cancellationToken);
            SelectedChord = null;
            if (IsRunning && !HasEnabledChords)
            {
                StatusText = "Practice stopped because no chords are enabled.";
                await StopPracticeAsync();
            }
        }
        finally
        {
            _configurationGate.Release();
        }
    }

    public string ExportJson() => ConfigurationJson.Serialize(Configuration);

    public async Task PlayChordAsync(GuitarFingering fingering, CancellationToken cancellationToken = default)
    {
        await _audioPlayer.PlayAsync(fingering, Settings.StrumIntervalSeconds, cancellationToken);
        PlayingChordId = null;
        OnPropertyChanged(nameof(IsAudioPlaying));
    }

    public async Task ToggleChordPlaybackAsync(GuitarFingering fingering, Guid? chordId = null, CancellationToken cancellationToken = default)
    {
        if (_audioPlayer.IsPlaying && (chordId is null || chordId == PlayingChordId))
        {
            await _audioPlayer.StopAsync();
            PlayingChordId = null;
        }
        else
        {
            await _audioPlayer.PlayAsync(fingering, Settings.StrumIntervalSeconds, cancellationToken);
            PlayingChordId = chordId;
        }

        OnPropertyChanged(nameof(IsAudioPlaying));
    }

    public async Task StopAudioAsync()
    {
        await _audioPlayer.StopAsync();
        PlayingChordId = null;
        OnPropertyChanged(nameof(IsAudioPlaying));
    }

    public async Task TogglePracticeAsync()
    {
        if (IsRunning)
        {
            await StopPracticeAsync();
            return;
        }

        if (!HasEnabledChords)
        {
            return;
        }

        _runCancellation = new CancellationTokenSource();
        var cancellationToken = _runCancellation.Token;
        IsRunning = true;
        PracticeChord = null;
        CountdownText = string.Empty;
        StatusText = "Get ready";
        try
        {
            var result = await _practiceEngine.RunAsync(
                () => Configuration.Chords.Where(chord => chord.Enabled).ToArray(),
                Configuration.Settings,
                value =>
                {
                    CountdownText = value.ToString(CultureInfo.InvariantCulture);
                    return ValueTask.CompletedTask;
                },
                async (chord, _) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PracticeChord = chord;
                    CountdownText = string.Empty;
                    StatusText = chord.DerivedChordName ?? "Chord displayed";
                    if (Configuration.Settings.PlayChordDuringRun)
                    {
                        await _audioPlayer.PlayAsync(chord.Fingering, Settings.StrumIntervalSeconds, cancellationToken);
                        PlayingChordId = chord.Id;
                    }
                    else
                    {
                        await _audioPlayer.StopAsync();
                        PlayingChordId = null;
                    }
                    OnPropertyChanged(nameof(IsAudioPlaying));
                },
                cancellationToken);

            if (result == PracticeEndReason.NoEnabledChords)
            {
                StatusText = "Practice stopped because no chords are enabled.";
                PracticeChord = null;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = HasEnabledChords ? "Practice stopped." : "Enable at least one chord in the library to practice.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            StatusText = "Practice could not continue.";
        }
        finally
        {
            await _audioPlayer.StopAsync();
            PlayingChordId = null;
            OnPropertyChanged(nameof(IsAudioPlaying));
            _runCancellation?.Dispose();
            _runCancellation = null;
            CountdownText = string.Empty;
            IsRunning = false;
        }
    }

    public async Task StopPracticeAsync()
    {
        _runCancellation?.Cancel();
        await _audioPlayer.StopAsync();
        PlayingChordId = null;
        OnPropertyChanged(nameof(IsAudioPlaying));
    }

    public async ValueTask DisposeAsync()
    {
        await StopPracticeAsync();
        await _audioPlayer.DisposeAsync();
        _configurationGate.Dispose();
    }

    private async Task PersistAsync(AppConfiguration configuration, CancellationToken cancellationToken)
    {
        await _store.SaveAsync(configuration, cancellationToken);
        Configuration = configuration;
        RefreshChords();
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(StrumIntervalSeconds));
        OnPropertyChanged(nameof(ShowFingering));
        OnPropertyChanged(nameof(PlayChordDuringRun));
        OnPropertyChanged(nameof(HasEnabledChords));
        OnPropertyChanged(nameof(CanStartPractice));
        OnPropertyChanged(nameof(CanShowNoChordsMessage));
        OnPropertyChanged(nameof(PracticeChordName));
        OnPropertyChanged(nameof(ShowPracticeFingering));
    }

    private void RefreshChords()
    {
        Chords.Clear();
        foreach (var chord in Configuration.Chords
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(item => item.Id))
        {
            Chords.Add(chord);
        }
    }

    private void UpdateSettingsText()
    {
        MinimumDurationText = Configuration.Settings.MinimumDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        MaximumDurationText = Configuration.Settings.MaximumDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}