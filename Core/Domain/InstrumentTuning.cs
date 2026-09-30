namespace StringTuner.Core.Domain;

public sealed record InstrumentString(
    string Label,
    int MidiNote,
    int Order,
    double? FrequencyOverride = null,
    string? NoteNameOverride = null,
    int CentsOffset = 0)
{
    public int Octave => MidiNote / 12 - 1;

    public string NoteName => NoteNameOverride ?? PitchMath.GetNoteName(MidiNote);

    public double TargetFrequency => GetTargetFrequency();

    public double GetTargetFrequency(double referenceA4 = 440) =>
        FrequencyOverride ?? PitchMath.MidiToFrequency(MidiNote, referenceA4) * Math.Pow(2, CentsOffset / 1200d);

    public string DisplayNote => NoteNameOverride ?? $"{NoteName}{Octave}";

    public string DisplayName => $"{Label}  {DisplayNote}";
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