using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Extensions.Logging;
using StringTuner.Audio;
using StringTuner.Core.Domain;
using StringTuner.Core.Pitch;
using StringTuner.Services;

namespace StringTuner.ViewModels;

public sealed class TunerViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IAudioCapture _audioCapture;
    private readonly IAudioToneGenerator _toneGenerator;
    private readonly ILogger<TunerViewModel> _logger;
    private readonly IInstrumentCatalogProvider _catalogProvider;
    private readonly SemaphoreSlim _audioModeLock = new(1, 1);
    private readonly OverlappingPitchAnalyzer _analyzer = new();
    private readonly PitchSmoother _pitchSmoother = new();
    private InstrumentTuning? _selectedInstrument;
    private InstrumentString? _selectedString;
    private double _referencePitch = 440;
    private double _toneVolume = 0.35;
    private double? _measuredFrequency;
    private double? _centsDeviation;
    private double _confidence;
    private bool _isListening;
    private bool _isTonePlaying;
    private bool _hasCatalogError;
    private bool _catalogInitialized;
    private string? _catalogError;
    private string _signalState = "MICROPHONE OFF";

    public TunerViewModel(
        IAudioCapture audioCapture,
        IAudioToneGenerator toneGenerator,
        IInstrumentCatalogProvider catalogProvider,
        ILogger<TunerViewModel> logger)
    {
        _audioCapture = audioCapture;
        _toneGenerator = toneGenerator;
        _catalogProvider = catalogProvider;
        _logger = logger;
        _referencePitch = Preferences.Default.Get("TuningReferenceA4", 440d);
        Instruments = [];
        StringOptions = [];
        ToggleListeningCommand = new Command(async () => await ToggleListeningAsync());
        ToggleToneCommand = new Command(async () => await ToggleToneAsync());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<InstrumentTuning> Instruments { get; private set; }

    public IReadOnlyList<InstrumentString> StringOptions { get; private set; }

    public ICommand ToggleListeningCommand { get; }

    public ICommand ToggleToneCommand { get; }

    public InstrumentTuning? SelectedInstrument
    {
        get => _selectedInstrument;
        set
        {
            if (!SetProperty(ref _selectedInstrument, value))
            {
                return;
            }

            StringOptions = value?.Strings.OrderBy(item => item.Order).ToArray() ?? [];
            OnPropertyChanged(nameof(StringOptions));
            SelectedString = StringOptions.FirstOrDefault();
            OnPropertyChanged(nameof(TuningName));
        }
    }

    public InstrumentString? SelectedString
    {
        get => _selectedString;
        set
        {
            if (SetProperty(ref _selectedString, value))
            {
                OnPropertyChanged(nameof(TargetNote));
                OnPropertyChanged(nameof(TargetFrequencyText));
                OnPropertyChanged(nameof(TargetOctave));
                RecalculateDeviation();
                _ = RestartToneIfPlayingAsync();
            }
        }
    }

    public double ReferencePitch
    {
        get => _referencePitch;
        set
        {
            var boundedValue = Math.Clamp(value, 430, 450);
            if (SetProperty(ref _referencePitch, boundedValue))
            {
                Preferences.Default.Set("TuningReferenceA4", boundedValue);
                OnPropertyChanged(nameof(ReferencePitchText));
                OnPropertyChanged(nameof(TargetFrequencyText));
                RecalculateDeviation();
                _ = RestartToneIfPlayingAsync();
            }
        }
    }

    public double ToneVolume
    {
        get => _toneVolume;
        set
        {
            if (SetProperty(ref _toneVolume, Math.Clamp(value, 0, 1)))
            {
                _toneGenerator.SetVolume(_toneVolume);
            }
        }
    }

    public double? MeasuredFrequency
    {
        get => _measuredFrequency;
        private set
        {
            if (SetProperty(ref _measuredFrequency, value))
            {
                OnPropertyChanged(nameof(MeasuredFrequencyText));
                RecalculateDeviation();
            }
        }
    }

    public double? CentsDeviation
    {
        get => _centsDeviation;
        private set
        {
            if (SetProperty(ref _centsDeviation, value))
            {
                OnPropertyChanged(nameof(CentsDeviationText));
                OnPropertyChanged(nameof(TuningState));
                OnPropertyChanged(nameof(TuningStateColor));
                OnPropertyChanged(nameof(GaugeCents));
            }
        }
    }

    public double Confidence
    {
        get => _confidence;
        private set
        {
            if (SetProperty(ref _confidence, value))
            {
                OnPropertyChanged(nameof(ConfidenceText));
            }
        }
    }

    public bool IsListening
    {
        get => _isListening;
        private set
        {
            if (SetProperty(ref _isListening, value))
            {
                OnPropertyChanged(nameof(ListeningButtonText));
            }
        }
    }

    public bool IsTonePlaying
    {
        get => _isTonePlaying;
        private set
        {
            if (SetProperty(ref _isTonePlaying, value))
            {
                OnPropertyChanged(nameof(ToneButtonText));
            }
        }
    }

    public string SignalState
    {
        get => _signalState;
        private set => SetProperty(ref _signalState, value);
    }

    public string? CatalogError
    {
        get => _catalogError;
        private set => SetProperty(ref _catalogError, value);
    }

    public bool HasCatalogError
    {
        get => _hasCatalogError;
        private set => SetProperty(ref _hasCatalogError, value);
    }

    public string TuningName => _selectedInstrument?.TuningName ?? string.Empty;

    public string TargetNote => _selectedString?.DisplayNote ?? "—";

    public string TargetOctave => _selectedString is null ? string.Empty : $"STRING {_selectedString.Label.ToUpperInvariant()}";

    public string ReferencePitchText => $"A4  {ReferencePitch:0} Hz";

    public string TargetFrequencyText => _selectedString is null
        ? "—"
        : $"{_selectedString.GetTargetFrequency(ReferencePitch):0.000} Hz target";

    public string MeasuredFrequencyText => MeasuredFrequency is double frequency ? $"{frequency:0.00}" : "—";

    public string CentsDeviationText => CentsDeviation is double cents ? $"{cents:+0.0;-0.0;0.0}¢" : "—¢";

    public string ConfidenceText => MeasuredFrequency is null ? "Waiting for a clear note" : $"Signal quality  {Confidence:P0}";

    public string ListeningButtonText => IsListening ? "Stop listening" : "Start listening";

    public string ToneButtonText => IsTonePlaying ? "Stop tone" : "Play reference tone";

    public string TuningState => CentsDeviation is not double cents
        ? "NO SIGNAL"
        : Math.Abs(cents) <= 5 ? "IN TUNE" : cents < 0 ? "FLAT" : "SHARP";

    public Color TuningStateColor => CentsDeviation is not double cents
        ? Color.FromArgb("#A8B3AE")
        : Math.Abs(cents) <= 5 ? Color.FromArgb("#B6DB77")
        : cents < 0 ? Color.FromArgb("#57C2D0") : Color.FromArgb("#FF936A");

    public double? GaugeCents => CentsDeviation is double cents ? Math.Clamp(cents, -50, 50) : null;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_catalogInitialized)
        {
            return;
        }

        _catalogInitialized = true;
        try
        {
            var result = await _catalogProvider.LoadAsync(cancellationToken);
            if (result.Error is not null || result.Instruments.Count == 0)
            {
                CatalogError = result.Error ?? "instruments.json contains no tunings.";
                HasCatalogError = true;
                SignalState = "TUNINGS UNAVAILABLE";
                return;
            }

            Instruments = result.Instruments;
            OnPropertyChanged(nameof(Instruments));
            SelectedInstrument = Instruments[0];
            CatalogError = null;
            HasCatalogError = false;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not initialize the instrument catalog.");
            CatalogError = $"Could not load instruments.json: {exception.Message}";
            HasCatalogError = true;
            SignalState = "TUNINGS UNAVAILABLE";
        }
    }

    public async Task StopAudioAsync()
    {
        await _audioModeLock.WaitAsync();
        try
        {
            await StopListeningAsync();
            if (IsTonePlaying)
            {
                await StopToneAsync();
            }
        }
        finally
        {
            _audioModeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAudioAsync();
        if (_audioCapture is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }

        _audioModeLock.Dispose();
    }

    private async Task ToggleListeningAsync()
    {
        await _audioModeLock.WaitAsync();
        try
        {
            if (_selectedString is null)
            {
                SignalState = "TUNINGS UNAVAILABLE";
                return;
            }

            if (IsListening)
            {
                await StopListeningAsync();
                return;
            }

            try
            {
                var permission = await Permissions.RequestAsync<Permissions.Microphone>();
                if (permission != PermissionStatus.Granted)
                {
                    SignalState = "MIC PERMISSION DENIED";
                    return;
                }

                if (!await StopToneAsync())
                {
                    return;
                }

                _analyzer.Reset();
                await _audioCapture.StartAsync(OnAudioSamples);
                IsListening = true;
                SignalState = "LISTENING · PLAY";
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not start microphone capture.");
                IsListening = false;
                SignalState = "MIC UNAVAILABLE";
            }
        }
        finally
        {
            _audioModeLock.Release();
        }
    }

    private async Task ToggleToneAsync()
    {
        await _audioModeLock.WaitAsync();
        try
        {
            if (IsTonePlaying)
            {
                await StopToneAsync();
                return;
            }

            if (_selectedString is null)
            {
                SignalState = "TUNINGS UNAVAILABLE";
                return;
            }

            if (!await StopListeningAsync())
            {
                return;
            }

            try
            {
                await _toneGenerator.StartAsync(TargetFrequency, ToneVolume);
                IsTonePlaying = true;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not start the reference tone.");
                SignalState = "TONE UNAVAILABLE";
            }
        }
        finally
        {
            _audioModeLock.Release();
        }
    }

    private async Task<bool> StopListeningAsync()
    {
        if (!IsListening)
        {
            return true;
        }

        try
        {
            await _audioCapture.StopAsync();
            IsListening = false;
            _analyzer.Reset();
            ClearMeasurement("MICROPHONE OFF");
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not stop microphone capture.");
            SignalState = "MIC STOP FAILED";
            return false;
        }
    }

    private async Task<bool> StopToneAsync()
    {
        if (!IsTonePlaying)
        {
            return true;
        }

        try
        {
            await _toneGenerator.StopAsync();
            IsTonePlaying = false;
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not stop the reference tone.");
            SignalState = "TONE STOP FAILED";
            return false;
        }
    }

    private async Task RestartToneIfPlayingAsync()
    {
        await _audioModeLock.WaitAsync();
        try
        {
            if (!IsTonePlaying || IsListening || _selectedString is null)
            {
                return;
            }

            await _toneGenerator.StartAsync(TargetFrequency, ToneVolume);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not retune the reference tone.");
            IsTonePlaying = false;
            SignalState = "TONE UNAVAILABLE";
        }
        finally
        {
            _audioModeLock.Release();
        }
    }

    private double TargetFrequency => _selectedString is null
        ? 440
        : _selectedString.GetTargetFrequency(ReferencePitch);

    private void OnAudioSamples(ReadOnlyMemory<float> samples)
    {
        foreach (var estimate in _analyzer.AddSamples(samples.Span))
        {
            MainThread.BeginInvokeOnMainThread(() => ApplyEstimate(estimate));
        }
    }

    private void ApplyEstimate(PitchEstimate? estimate)
    {
        if (!IsListening)
        {
            return;
        }

        if (estimate is null)
        {
            _pitchSmoother.Reset();
            ClearMeasurement("NO CLEAR PITCH");
            return;
        }

        MeasuredFrequency = _pitchSmoother.Update(estimate.Frequency);
        Confidence = estimate.Confidence;
        SignalState = "SIGNAL LOCKED";
    }

    private void RecalculateDeviation()
    {
        CentsDeviation = MeasuredFrequency is double measured && _selectedString is not null
            ? PitchMath.CentsFrom(measured, TargetFrequency)
            : null;
    }

    private void ClearMeasurement(string state)
    {
        _pitchSmoother.Reset();
        MeasuredFrequency = null;
        CentsDeviation = null;
        Confidence = 0;
        SignalState = state;
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