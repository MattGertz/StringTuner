using System.Text.Json;

namespace StringTuner.Core.Domain;

public static class InstrumentCatalogJson
{
    private const int SupportedSchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
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

                if (instrumentString.MidiNote is < 0 or > 127)
                {
                    throw new InvalidDataException($"String '{instrumentString.Label}' in '{tuning.TuningName}' has an invalid MIDI note.");
                }

                if (instrumentString.Order < 0 || !orders.Add(instrumentString.Order))
                {
                    throw new InvalidDataException($"Tuning '{tuning.TuningName}' has a negative or duplicate string order.");
                }
            }

            validated.Add(tuning with { Strings = tuning.Strings.OrderBy(item => item.Order).ToArray() });
        }

        return validated;
    }

    private sealed class InstrumentCatalogDocument
    {
        public int SchemaVersion { get; set; }

        public List<InstrumentTuning?>? Instruments { get; set; }
    }
}