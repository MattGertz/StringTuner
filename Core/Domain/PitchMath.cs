namespace StringTuner.Core.Domain;

public static class PitchMath
{
    private static readonly string[] NoteNames = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];

    public static double MidiToFrequency(int midiNote, double referenceA4 = 440) =>
        referenceA4 * Math.Pow(2, (midiNote - 69) / 12d);

    public static double CentsFrom(double measuredFrequency, double targetFrequency) =>
        1200 * Math.Log2(measuredFrequency / targetFrequency);

    public static string GetNoteName(int midiNote) => NoteNames[((midiNote % 12) + 12) % 12];
}