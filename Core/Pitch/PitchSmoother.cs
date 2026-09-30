using StringTuner.Core.Domain;

namespace StringTuner.Core.Pitch;

public sealed class PitchSmoother(double smoothingFactor = 0.4, double resetThresholdCents = 150)
{
    private double? _smoothedFrequency;

    public double Update(double frequency)
    {
        if (!double.IsFinite(frequency) || frequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frequency));
        }

        if (_smoothedFrequency is not double previous ||
            Math.Abs(PitchMath.CentsFrom(frequency, previous)) > resetThresholdCents)
        {
            _smoothedFrequency = frequency;
            return frequency;
        }

        _smoothedFrequency = Math.Exp(Math.Log(previous) + smoothingFactor * (Math.Log(frequency) - Math.Log(previous)));
        return _smoothedFrequency.Value;
    }

    public void Reset() => _smoothedFrequency = null;
}