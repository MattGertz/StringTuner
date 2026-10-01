using System.Globalization;

namespace StringTuner.Core.Domain;

public static class PitchConversion
{
    private static readonly (int PitchClass, string Name)[] NaturalNotes =
    [
        (0, "C"), (2, "D"), (4, "E"), (5, "F"), (7, "G"), (9, "A"), (11, "B")
    ];

    private static readonly (int Cents, string Accidental)[] Accidentals =
    [
        (-150, "𝄵"), (-100, "♭"), (-50, "𝄳"), (0, ""), (50, "𝄲"), (100, "♯"), (150, "𝄴")
    ];

    public static double Resolve(PitchRepresentation representation, string value, double referenceA4)
    {
        if (referenceA4 is < 400 or > 480)
        {
            throw new ArgumentOutOfRangeException(nameof(referenceA4));
        }

        return representation switch
        {
            PitchRepresentation.Frequency => ParseFrequency(value),
            PitchRepresentation.MidiNote => PitchMath.MidiToFrequency(ParseMidi(value), referenceA4),
            PitchRepresentation.Note => ResolveNote(value, referenceA4),
            _ => throw new ArgumentOutOfRangeException(nameof(representation))
        };
    }

    public static string Convert(PitchRepresentation from, string value, PitchRepresentation to, double referenceA4)
    {
        if (from == PitchRepresentation.Note)
        {
            value = NormalizeEditableNote(value);
        }

        if (from == PitchRepresentation.Note && to == PitchRepresentation.Note)
        {
            return value;
        }

        var frequency = Resolve(from, value, referenceA4);
        return to switch
        {
            PitchRepresentation.Frequency => Math.Round(frequency, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture),
            PitchRepresentation.MidiNote => Math.Clamp(
                (int)Math.Round(69 + 12 * Math.Log2(frequency / referenceA4), MidpointRounding.AwayFromZero),
                0, 127).ToString(CultureInfo.InvariantCulture),
            PitchRepresentation.Note => FrequencyToNearestQuarterTone(frequency, referenceA4),
            _ => throw new ArgumentOutOfRangeException(nameof(to))
        };
    }

    public static string NormalizeEditableNote(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("Enter a note letter and octave.");
        }

        var compact = string.Concat(value.Where(character => !char.IsWhiteSpace(character)));
        var parts = compact.Split('/');
        if (parts.Length > 2 || parts.Any(string.IsNullOrEmpty))
        {
            throw new FormatException("Use one note or an enharmonic pair separated by '/'.");
        }

        var normalizedParts = parts.Select(NormalizeSingleNote).ToArray();
        if (normalizedParts.Length == 2)
        {
            var left = ParseSingleNote(normalizedParts[0]);
            var right = ParseSingleNote(normalizedParts[1]);
            if (PitchPosition(left) != PitchPosition(right))
            {
                throw new FormatException("Both notes in an enharmonic pair must have the same pitch.");
            }

            if (IsNaturalNote(normalizedParts[0]))
            {
                return normalizedParts[0];
            }

            if (IsNaturalNote(normalizedParts[1]))
            {
                return normalizedParts[1];
            }

            return OrderEnharmonicPair(normalizedParts[0], normalizedParts[1]);
        }

        var parsed = ParseSingleNote(normalizedParts[0]);
        if (parsed.CentsOffset == 0)
        {
            return normalizedParts[0];
        }

        var alternative = FindEnharmonic(normalizedParts[0], parsed);
        if (alternative is null)
        {
            return normalizedParts[0];
        }

        return IsNaturalNote(alternative)
            ? alternative
            : OrderEnharmonicPair(normalizedParts[0], alternative);
    }

    public static (int MidiNote, int CentsOffset) ParseNoteValue(string value)
    {
        var normalized = NormalizeEditableNote(value);
        return ParseStrictNoteValue(normalized);
    }

    public static (int MidiNote, int CentsOffset) ParseStrictNoteValue(string value)
    {
        var parts = value.Split('/');
        if (parts.Length is < 1 or > 2 || parts.Any(string.IsNullOrEmpty))
        {
            throw new FormatException("Use one note or an enharmonic pair separated by '/'.");
        }

        var first = ParseSingleNote(parts[0]);
        if (parts.Length == 1)
        {
            return first;
        }

        var second = ParseSingleNote(parts[1]);
        if (PitchPosition(first) != PitchPosition(second))
        {
            throw new FormatException("Both notes in an enharmonic pair must have the same pitch.");
        }

        return first;
    }

    public static double ParseFrequency(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var frequency) ||
            !double.IsFinite(frequency) || frequency <= 0)
        {
            throw new FormatException("Enter a positive frequency in hertz.");
        }

        var midiValue = 69 + 12 * Math.Log2(frequency / 440);
        if (!double.IsFinite(midiValue) || midiValue < -0.5 || midiValue > 127.5)
        {
            throw new FormatException("Frequency is outside the supported pitch range.");
        }

        return frequency;
    }

    private static int ParseMidi(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var midiNote) || midiNote is < 0 or > 127)
        {
            throw new FormatException("Enter a MIDI note number from 0 through 127.");
        }

        return midiNote;
    }

    private static double ResolveNote(string value, double referenceA4)
    {
        var (midiNote, centsOffset) = ParseNoteValue(value);
        return PitchMath.MidiToFrequency(midiNote, referenceA4) * Math.Pow(2, centsOffset / 1200d);
    }

    private static string FrequencyToNearestQuarterTone(double frequency, double referenceA4)
    {
        var candidates = new List<(string Note, double ErrorCents, int AccidentalSize, int MidiAnchor)>();
        var targetMidi = 69 + 12 * Math.Log2(frequency / referenceA4);
        var firstAnchor = Math.Max(0, (int)Math.Floor(targetMidi) - 3);
        var lastAnchor = Math.Min(127, (int)Math.Ceiling(targetMidi) + 3);

        for (var anchor = firstAnchor; anchor <= lastAnchor; anchor++)
        {
            var pitchClass = anchor % 12;
            var natural = NaturalNotes.FirstOrDefault(note => note.PitchClass == pitchClass);
            if (natural.Name is null)
            {
                continue;
            }

            var octave = anchor / 12 - 1;
            foreach (var accidental in Accidentals)
            {
                var candidateFrequency = PitchMath.MidiToFrequency(anchor, referenceA4) * Math.Pow(2, accidental.Cents / 1200d);
                var error = Math.Abs(PitchMath.CentsFrom(frequency, candidateFrequency));
                candidates.Add(($"{natural.Name}{accidental.Accidental}{octave}", Math.Round(error, 8), Math.Abs(accidental.Cents), anchor));
            }
        }

        var nearest = candidates
            .OrderBy(candidate => candidate.ErrorCents)
            .ThenBy(candidate => candidate.AccidentalSize)
            .ThenBy(candidate => candidate.MidiAnchor)
            .First().Note;
        return NormalizeEditableNote(nearest);
    }

    private static string NormalizeSingleNote(string note)
    {
        var runes = note.EnumerateRunes().ToArray();
        if (runes.Length > 1)
        {
            var canonicalAccidental = runes[1].Value switch
            {
                '#' => "♯",
                'b' => "♭",
                _ => runes[1].ToString()
            };
            if (canonicalAccidental != runes[1].ToString())
            {
                runes[1] = canonicalAccidental.EnumerateRunes().Single();
            }
        }

        var normalized = string.Concat(runes.Select(rune => rune.ToString()));
        if (!PitchMath.TryParseNote(normalized, out _, out _))
        {
            throw new FormatException("Enter a note letter, optional accidental, and octave.");
        }

        return normalized;
    }

    private static (int MidiNote, int CentsOffset) ParseSingleNote(string note)
    {
        if (!PitchMath.TryParseNote(note, out var midiNote, out var centsOffset))
        {
            throw new FormatException("Enter a note letter, optional accidental, and octave.");
        }

        return (midiNote, centsOffset);
    }

    private static long PitchPosition((int MidiNote, int CentsOffset) note) => note.MidiNote * 100L + note.CentsOffset;

    private static string? FindEnharmonic(string original, (int MidiNote, int CentsOffset) pitch)
    {
        var target = PitchPosition(pitch);
        var candidates = new List<string>();
        for (var midi = Math.Max(0, pitch.MidiNote - 3); midi <= Math.Min(127, pitch.MidiNote + 3); midi++)
        {
            var natural = NaturalNotes.FirstOrDefault(item => item.PitchClass == midi % 12);
            if (natural.Name is null)
            {
                continue;
            }

            foreach (var accidental in Accidentals)
            {
                if (midi * 100L + accidental.Cents != target)
                {
                    continue;
                }

                var candidate = $"{natural.Name}{accidental.Accidental}{midi / 12 - 1}";
                if (candidate != original && candidate[0] != original[0])
                {
                    candidates.Add(candidate);
                }
            }
        }

        return candidates
            .OrderBy(candidate => LetterOrder(candidate[0]))
            .ThenBy(candidate => candidate, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string OrderEnharmonicPair(string first, string second) =>
        LetterOrder(first[0]) <= LetterOrder(second[0])
            ? $"{first}/{second}"
            : $"{second}/{first}";

    private static int LetterOrder(char letter) => Array.FindIndex(NaturalNotes, item => item.Name[0] == letter);

    private static bool IsNaturalNote(string note) => note.Length > 1 && (char.IsDigit(note[1]) || note[1] == '-');
}