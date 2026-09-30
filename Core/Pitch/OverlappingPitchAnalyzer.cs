namespace StringTuner.Core.Pitch;

public sealed class OverlappingPitchAnalyzer
{
    private readonly float[] _window;
    private readonly int _hopSize;
    private readonly YinPitchDetector _detector;
    private int _filled;

    public OverlappingPitchAnalyzer(int sampleRate = 44100, int windowSize = 8192, int hopSize = 4096)
    {
        if (sampleRate <= 0 || hopSize <= 0 || hopSize >= windowSize)
        {
            throw new ArgumentOutOfRangeException(nameof(hopSize));
        }

        SampleRate = sampleRate;
        _window = new float[windowSize];
        _hopSize = hopSize;
        _detector = new YinPitchDetector(windowSize);
    }

    public int SampleRate { get; }

    public IReadOnlyList<PitchEstimate?> AddSamples(ReadOnlySpan<float> samples)
    {
        List<PitchEstimate?>? estimates = null;
        foreach (var sample in samples)
        {
            _window[_filled++] = sample;
            if (_filled != _window.Length)
            {
                continue;
            }

            estimates ??= [];
            estimates.Add(_detector.Detect(_window, SampleRate));
            Array.Copy(_window, _hopSize, _window, 0, _window.Length - _hopSize);
            _filled = _window.Length - _hopSize;
        }

        return estimates ?? (IReadOnlyList<PitchEstimate?>)Array.Empty<PitchEstimate?>();
    }

    public void Reset() => _filled = 0;
}