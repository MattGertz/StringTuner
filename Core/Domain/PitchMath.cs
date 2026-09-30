using System.Text;

namespace StringTuner.Core.Domain;

public static class PitchMath
{
    private const int Sharp = 0x266F;
    private const int Flat = 0x266D;
    private const int QuarterToneSharp = 0x1D132;
    private const int QuarterToneFlat = 0x1D133;
    private const int ThreeQuarterToneSharp = 0x1D134;
    private const int ThreeQuarterToneFlat = 0x1D135;
    private static readonly string[] NoteNames = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];

    public static double MidiToFrequency(int midiNote, double referenceA4 = 440) =>
        referenceA4 * Math.Pow(2, (midiNote - 69) / 12d);

    public static double CentsFrom(double measuredFrequency, double targetFrequency) =>
        1200 * Math.Log2(measuredFrequency / targetFrequency);

    public static string GetNoteName(int midiNote) => NoteNames[((midiNote % 12) + 12) % 12];

    public static bool TryParseNote(string? note, out int midiNote, out int centsOffset)
    {
        midiNote = default;
        centsOffset = default;
        if (string.IsNullOrWhiteSpace(note))
        {
            return false;
        }

        var runes = note.Trim().EnumerateRunes().ToArray();
        if (runes.Length < 2)
        {
            return false;
        }

        var letter = char.ToUpperInvariant((char)runes[0].Value);
        var pitchClass = letter switch
        {
            'C' => 0,
            'D' => 2,
            'E' => 4,
            'F' => 5,
            'G' => 7,
            'A' => 9,
            'B' => 11,
            _ => -1
        };
        if (pitchClass < 0)
        {
            return false;
        }

        var position = 1;
        if (position < runes.Length)
        {
            centsOffset = runes[position].Value switch
            {
                Sharp => 100,
                Flat => -100,
                QuarterToneSharp => 50,
                QuarterToneFlat => -50,
                ThreeQuarterToneSharp => 150,
                ThreeQuarterToneFlat => -150,
                _ => 0
            };
            if (centsOffset != 0)
            {
                position++;
            }
        }

        if (position == runes.Length || !int.TryParse(string.Concat(runes.Skip(position).Select(rune => rune.ToString())), out var octave))
        {
            return false;
        }

        var parsedMidi = (octave + 1) * 12 + pitchClass;
        if (parsedMidi is < 0 or > 127)
        {
            centsOffset = default;
            return false;
        }

        midiNote = parsedMidi;
        return true;
    }
}