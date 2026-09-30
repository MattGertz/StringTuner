namespace StringTuner.Core.Domain;

public sealed record InstrumentString(string Label, int MidiNote, int Order)
{
    public int Octave => MidiNote / 12 - 1;

    public string NoteName => PitchMath.GetNoteName(MidiNote);

    public double TargetFrequency => PitchMath.MidiToFrequency(MidiNote);

    public string DisplayName => $"{Label}  {NoteName}{Octave}";
}

public sealed record InstrumentTuning(
    string InstrumentName,
    string TuningName,
    IReadOnlyList<InstrumentString> Strings,
    string? DisplayName = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public string Name => DisplayName ?? $"{InstrumentName} · {TuningName}";
}