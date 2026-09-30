using System.Text.Json;
using System.Globalization;

namespace StringTuner.Core.Domain;

public static class InstrumentCatalogJson
{
    private const int SupportedSchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<InstrumentTuning> Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var document = JsonSerializer.Deserialize<InstrumentCatalogDocument>(json, Options)
            ?? throw new JsonException("The instrument catalog document is empty.");

        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported instrument catalog schema version: {document.SchemaVersion}.");
        }

        if (document.Instruments is not { Count: > 0 })
        {
            throw new InvalidDataException("The catalog must contain at least one instrument tuning.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validated = new List<InstrumentTuning>(document.Instruments.Count);
        foreach (var tuning in document.Instruments)
        {
            if (tuning is null)
            {
                throw new InvalidDataException("The catalog contains a null tuning entry.");
            }

            if (string.IsNullOrWhiteSpace(tuning.InstrumentName) || string.IsNullOrWhiteSpace(tuning.TuningName))
            {
                throw new InvalidDataException("Each tuning must have an instrumentName and tuningName.");
            }

            if (!names.Add($"{tuning.InstrumentName}\0{tuning.TuningName}"))
            {
                throw new InvalidDataException($"Duplicate tuning '{tuning.InstrumentName} / {tuning.TuningName}'.");
            }

            if (tuning.Strings is not { Count: > 0 and <= 24 })
            {
                throw new InvalidDataException($"Tuning '{tuning.TuningName}' must contain between 1 and 24 strings.");
            }

            var orders = new HashSet<int>();
            var strings = new List<InstrumentString>(tuning.Strings.Count);
            foreach (var instrumentString in tuning.Strings)
            {
                if (instrumentString is null)
                {
                    throw new InvalidDataException($"Tuning '{tuning.TuningName}' contains a null string entry.");
                }

                if (string.IsNullOrWhiteSpace(instrumentString.Label))
                {
                    throw new InvalidDataException($"Tuning '{tuning.TuningName}' contains a string with no label.");
                }

                if (instrumentString.Order < 0 || !orders.Add(instrumentString.Order))
                {
                    throw new InvalidDataException($"Tuning '{tuning.TuningName}' has a negative or duplicate string order.");
                }

                strings.Add(ParseString(instrumentString, tuning.TuningName));
            }

            validated.Add(new InstrumentTuning(
                tuning.InstrumentName,
                tuning.TuningName,
                strings.OrderBy(item => item.Order).ToArray(),
                tuning.DisplayName,
                tuning.Metadata));
        }

        return validated;
    }

    private static InstrumentString ParseString(InstrumentStringDocument source, string tuningName)
    {
        var hasMidiNote = source.MidiNote.ValueKind != JsonValueKind.Undefined;
        var hasFrequency = source.Frequency.ValueKind != JsonValueKind.Undefined;
        var hasNote = source.Note.ValueKind != JsonValueKind.Undefined;
        if ((hasMidiNote ? 1 : 0) + (hasFrequency ? 1 : 0) + (hasNote ? 1 : 0) != 1)
        {
            throw new InvalidDataException($"String '{source.Label}' in '{tuningName}' must specify exactly one of midiNote, frequency, or note.");
        }

        int midiNote;
        var centsOffset = 0;
        double? frequencyOverride = null;
        string? noteNameOverride = null;

        if (hasMidiNote)
        {
            if (source.MidiNote.ValueKind != JsonValueKind.Number || !source.MidiNote.TryGetInt32(out midiNote))
            {
                throw new InvalidDataException($"String '{source.Label}' in '{tuningName}' has an invalid midiNote.");
            }
        }
        else if (hasFrequency)
        {
            if (!TryReadFrequency(source.Frequency, out var frequency))
            {
                throw new InvalidDataException($"String '{source.Label}' in '{tuningName}' has an invalid frequency; use a positive number of hertz.");
            }

            var midiValue = 69 + 12 * Math.Log2(frequency / 440);
            if (!double.IsFinite(midiValue) || midiValue < -0.5 || midiValue > 127.5)
            {
                throw new InvalidDataException($"String '{source.Label}' in '{tuningName}' has a frequency outside the supported MIDI range.");
            }

            midiNote = (int)Math.Round(midiValue, MidpointRounding.AwayFromZero);
            frequencyOverride = frequency;
        }
        else
        {
            if (source.Note.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"String '{source.Label}' in '{tuningName}' must have a string note such as E2 or Ab3.");
            }

            noteNameOverride = source.Note.GetString();
            if (!PitchMath.TryParseNote(noteNameOverride, out midiNote, out centsOffset))
            {
                throw new InvalidDataException($"String '{source.Label}' in '{tuningName}' has an invalid note; use a note such as E2, A♯3, A♭3, or a quarter-tone accidental.");
            }
        }

        if (midiNote is < 0 or > 127)
        {
            throw new InvalidDataException($"String '{source.Label}' in '{tuningName}' has a MIDI pitch outside 0 through 127.");
        }

        return new InstrumentString(source.Label!, midiNote, source.Order, frequencyOverride, noteNameOverride, centsOffset);
    }

    private static bool TryReadFrequency(JsonElement value, out double frequency)
    {
        frequency = default;
        var parsed = value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDouble(out frequency),
            JsonValueKind.String => double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out frequency),
            _ => false
        };

        return parsed && double.IsFinite(frequency) && frequency > 0;
    }

    private sealed class InstrumentCatalogDocument
    {
        public int SchemaVersion { get; set; }

        public List<InstrumentTuningDocument?>? Instruments { get; set; }
    }

    private sealed class InstrumentTuningDocument
    {
        public string? InstrumentName { get; set; }

        public string? TuningName { get; set; }

        public string? DisplayName { get; set; }

        public Dictionary<string, string>? Metadata { get; set; }

        public List<InstrumentStringDocument?>? Strings { get; set; }
    }

    private sealed class InstrumentStringDocument
    {
        public string? Label { get; set; }

        public JsonElement MidiNote { get; set; }

        public JsonElement Frequency { get; set; }

        public JsonElement Note { get; set; }

        public int Order { get; set; }
    }
}