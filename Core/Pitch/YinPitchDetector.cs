namespace StringTuner.Core.Pitch;

/// <summary>
/// YIN's normalized difference function is resistant to strong harmonics and also gives a periodicity score.
/// </summary>
public sealed class YinPitchDetector
{
    private readonly double[] _normalizedDifference;

    public YinPitchDetector(int windowSize = 8192, double minimumFrequency = 55, double maximumFrequency = 1000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumFrequency, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFrequency, minimumFrequency);
        _normalizedDifference = new double[windowSize / 2 + 1];
        MinimumFrequency = minimumFrequency;
        MaximumFrequency = maximumFrequency;
    }

    public double MinimumFrequency { get; }

    public double MaximumFrequency { get; }

    public PitchEstimate? Detect(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (sampleRate <= 0 || samples.Length < 1024 || samples.Length > (_normalizedDifference.Length - 1) * 2)
        {
            return null;
        }

        double sumSquares = 0;
        foreach (var sample in samples)
        {
            sumSquares += sample * sample;
        }

        var rms = Math.Sqrt(sumSquares / samples.Length);
        if (rms < 0.004 || rms > 0.65)
        {
            return null;
        }

        var minimumLag = Math.Max(2, (int)(sampleRate / MaximumFrequency));
        var maximumLag = Math.Min(samples.Length / 2, (int)(sampleRate / MinimumFrequency));
        if (maximumLag <= minimumLag || maximumLag >= _normalizedDifference.Length)
        {
            return null;
        }

        double cumulativeDifference = 0;
        _normalizedDifference[0] = 1;
        for (var lag = 1; lag <= maximumLag; lag++)
        {
            double difference = 0;
            var limit = samples.Length - lag;
            for (var index = 0; index < limit; index++)
            {
                var delta = samples[index] - samples[index + lag];
                difference += delta * delta;
            }

            cumulativeDifference += difference;
            _normalizedDifference[lag] = cumulativeDifference > 0
                ? difference * lag / cumulativeDifference
                : 1;
        }

        const double threshold = 0.15;
        var selectedLag = -1;
        for (var lag = minimumLag; lag <= maximumLag; lag++)
        {
            if (_normalizedDifference[lag] < threshold)
            {
                while (lag < maximumLag && _normalizedDifference[lag + 1] < _normalizedDifference[lag])
                {
                    lag++;
                }

                selectedLag = lag;
                break;
            }
        }

        if (selectedLag < 0)
        {
            var bestDifference = double.MaxValue;
            for (var lag = minimumLag; lag <= maximumLag; lag++)
            {
                if (_normalizedDifference[lag] < bestDifference)
                {
                    bestDifference = _normalizedDifference[lag];
                    selectedLag = lag;
                }
            }

            if (selectedLag < 0 || bestDifference > 0.25)
            {
                return null;
            }
        }

        var refinedLag = RefineLag(selectedLag, maximumLag);
        var frequency = sampleRate / refinedLag;
        var confidence = Math.Clamp(1 - _normalizedDifference[selectedLag], 0, 1);
        if (frequency < MinimumFrequency || frequency > MaximumFrequency || confidence < 0.75)
        {
            return null;
        }

        return new PitchEstimate(frequency, confidence, rms);
    }

    private double RefineLag(int lag, int maximumLag)
    {
        if (lag <= 1 || lag >= maximumLag)
        {
            return lag;
        }

        var left = _normalizedDifference[lag - 1];
        var center = _normalizedDifference[lag];
        var right = _normalizedDifference[lag + 1];
        var denominator = left - 2 * center + right;
        if (Math.Abs(denominator) < 1e-12)
        {
            return lag;
        }

        var offset = 0.5 * (left - right) / denominator;
        return lag + Math.Clamp(offset, -1, 1);
    }
}