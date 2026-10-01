using StringTuner.Core.Domain;
using StringTuner.Core.Pitch;
using System.Text.Json;

namespace StringTuner.Core.Tests;

public sealed class PitchAndTuningTests
{
    [Fact]
    public void MidiFrequencyUsesConfigurableA4Reference()
    {
        Assert.Equal(440, PitchMath.MidiToFrequency(69), precision: 8);
        Assert.Equal(82.4069, PitchMath.MidiToFrequency(40), precision: 3);
        Assert.Equal(442, PitchMath.MidiToFrequency(69, 442), precision: 8);
        Assert.Equal(82.4069, new InstrumentString("6", 40, 0).TargetFrequency, precision: 3);
    }

    [Fact]
    public void CentsDeviationHasCorrectSignAndMagnitude()
    {
        Assert.Equal(1200, PitchMath.CentsFrom(880, 440), precision: 8);
        Assert.Equal(-1200, PitchMath.CentsFrom(220, 440), precision: 8);
    }

    [Fact]
    public void CatalogContainsExpectedInstrumentConfigurations()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "instruments.json"));
        var catalog = InstrumentCatalogJson.Deserialize(json);

        Assert.Contains(catalog, tuning => tuning.InstrumentName == "Guitar" && tuning.Strings.Count == 6);
        Assert.Contains(catalog, tuning => tuning.InstrumentName == "Ukulele" && tuning.Strings.Count == 4);
        Assert.Contains(catalog, tuning => tuning.InstrumentName == "Mountain dulcimer" && tuning.Strings.Count == 3);
        Assert.Contains(catalog, tuning => tuning.InstrumentName == "Mountain dulcimer" && tuning.Strings.Count == 4);
        Assert.Equal(40, catalog[0].Strings[0].MidiNote);
        Assert.Equal(64, catalog[0].Strings[5].MidiNote);
    }

    [Fact]
    public void CatalogSchemaIsValidStrictJsonAndListsUnicodeAccidentals()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "instruments.schema.json")));
        var schemaText = schema.RootElement.GetProperty("$defs")
            .GetProperty("instrumentString")
            .GetProperty("properties")
            .GetProperty("note")
            .GetProperty("description")
            .GetString();

        Assert.Contains("U+1D132", schemaText);
        Assert.Contains("U+1D135", schemaText);
        Assert.Contains("ASCII # and b as typing aliases", schemaText);
    }

    [Fact]
    public void PitchConversionRoundsToNearestQuarterToneAndMidi()
    {
        var quarterSharpFrequency = 440 * Math.Pow(2, 50 / 1200d);
        var quarterSharpText = PitchConversion.Convert(
            PitchRepresentation.Frequency,
            quarterSharpFrequency.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            PitchRepresentation.Note,
            440);

        Assert.Equal("A𝄲4/B𝄵4", quarterSharpText);
        Assert.Equal("70", PitchConversion.Convert(PitchRepresentation.Note, "A𝄲4/B𝄵4", PitchRepresentation.MidiNote, 440));
        Assert.Equal(quarterSharpFrequency, PitchConversion.Resolve(PitchRepresentation.Note, quarterSharpText, 440), precision: 10);
        Assert.Equal(220, PitchConversion.Resolve(PitchRepresentation.Frequency, "220", 442), precision: 8);
        Assert.Equal("220.00", PitchConversion.Convert(PitchRepresentation.Note, "A3", PitchRepresentation.Frequency, 440));
    }

    [Fact]
    public void NoteConversionPreservesNaturalSpellingAndNormalizesTypedAliases()
    {
        Assert.Equal("C3", PitchConversion.Convert(PitchRepresentation.Note, "C3", PitchRepresentation.Note, 440));
        Assert.Equal("A♯4/B♭4", PitchConversion.NormalizeEditableNote("Bb4"));
        Assert.Equal("C♯4/D♭4", PitchConversion.NormalizeEditableNote("C#4"));
        Assert.Equal("466.16", PitchConversion.Convert(PitchRepresentation.Note, "Bb4", PitchRepresentation.Frequency, 440));
        Assert.Equal("C4", PitchConversion.NormalizeEditableNote("B#3"));
        Assert.Equal("A♯3/B♭3", PitchConversion.NormalizeEditableNote(" B b 3 "));
        Assert.Equal("C3", PitchConversion.NormalizeEditableNote(" C 3 "));
        Assert.Equal("C5", PitchConversion.NormalizeEditableNote("B#4/C5"));
        Assert.Throws<FormatException>(() => PitchConversion.NormalizeEditableNote("B#4/C4"));

        var c3Frequency = PitchMath.MidiToFrequency(48);
        Assert.Equal("C3", PitchConversion.Convert(PitchRepresentation.Frequency,
            c3Frequency.ToString("R", System.Globalization.CultureInfo.InvariantCulture), PitchRepresentation.Note, 440));
        Assert.Equal(PitchMath.MidiToFrequency(58, 440), PitchConversion.Resolve(PitchRepresentation.Note, "A♯3/B♭3", 440), precision: 8);
        Assert.Throws<FormatException>(() => PitchConversion.NormalizeEditableNote("A♯3/C4"));
    }

    [Fact]
    public void CatalogSerializationPreservesPitchRepresentationAndUnicodeNote()
    {
        const string json = """
            {"schemaVersion":1,"instruments":[{"instrumentName":"Test","tuningName":"Round trip","strings":[
              {"label":"Hz","frequency":220.5,"order":0},
              {"label":"Microtone","note":"A𝄲4","order":1},
              {"label":"MIDI","midiNote":69,"order":2}
            ]}]}
            """;
        var original = InstrumentCatalogJson.Deserialize(json);

        var serialized = InstrumentCatalogJson.Serialize(original);
        using var serializedDocument = JsonDocument.Parse(serialized);
        var serializedStrings = serializedDocument.RootElement.GetProperty("instruments")[0].GetProperty("strings");

        Assert.Equal(220.5, serializedStrings[0].GetProperty("frequency").GetDouble());
        Assert.Equal("A𝄲4/B𝄵4", serializedStrings[1].GetProperty("note").GetString());
        Assert.Equal(69, serializedStrings[2].GetProperty("midiNote").GetInt32());
        var roundTripped = Assert.Single(InstrumentCatalogJson.Deserialize(serialized)).Strings;
        Assert.Equal(original[0].Strings[0].TargetFrequency, roundTripped[0].TargetFrequency, precision: 8);
        Assert.Equal(original[0].Strings[0].Representation, roundTripped[0].Representation);
        Assert.Equal(original[0].Strings[1].DisplayNote, roundTripped[1].DisplayNote);
        Assert.Equal(original[0].Strings[1].CentsOffset, roundTripped[1].CentsOffset);
        Assert.Equal(original[0].Strings[2].MidiNote, roundTripped[2].MidiNote);
    }

    [Fact]
    public void CatalogMergeAddsAndResolvesConflictsWithoutPartialMutation()
    {
        var current = new[] { CreateTuning("Guitar", "Standard", "E2") };
        var imported = new[]
        {
            CreateTuning("guitar", "STANDARD", "D2"),
            CreateTuning("Ukulele", "Standard", "G4")
        };

        var keepCurrent = InstrumentCatalogMerge.Merge(current, imported, (_, _) => false);
        var useImported = InstrumentCatalogMerge.Merge(current, imported, (_, _) => true);
        var cancelled = InstrumentCatalogMerge.Merge(current, imported, (_, _) => null);

        Assert.NotNull(keepCurrent);
        Assert.Equal(2, keepCurrent.Count);
        Assert.Equal("E2", keepCurrent[0].Strings[0].DisplayNote);
        Assert.NotNull(useImported);
        Assert.Equal("D2", useImported[0].Strings[0].DisplayNote);
        Assert.Null(cancelled);
        Assert.Equal("E2", current[0].Strings[0].DisplayNote);
    }

    private static InstrumentTuning CreateTuning(string instrumentName, string tuningName, string note) =>
        new(instrumentName, tuningName, [new InstrumentString("String 1", 40, 0, NoteNameOverride: note, Representation: PitchRepresentation.Note)]);

    [Fact]
        public void CatalogParserRejectsCommentsAndTrailingCommas()
    {
                const string commentedJson = """
                        { // comments are not valid JSON
                            "schemaVersion": 1,
                            "instruments": [{"instrumentName":"Test","tuningName":"Test","strings":[{"label":"1","midiNote":69,"order":0}]}]
                        }
                        """;
                const string trailingCommaJson = """
                        {"schemaVersion":1,"instruments":[{"instrumentName":"Test","tuningName":"Test","strings":[{"label":"1","midiNote":69,"order":0,}]}]}
                        """;

                Assert.Throws<JsonException>(() => InstrumentCatalogJson.Deserialize(commentedJson));
                Assert.Throws<JsonException>(() => InstrumentCatalogJson.Deserialize(trailingCommaJson));
    }

        [Fact]
        public void CatalogAcceptsMidiFrequencyAndOctaveNotePitchForms()
        {
                const string json = """
                        {
                            "schemaVersion": 1,
                            "instruments": [{
                                "instrumentName": "Test",
                                "tuningName": "Pitch forms",
                                "strings": [
                                    { "label": "MIDI", "midiNote": 57, "order": 0 },
                                    { "label": "Numeric Hz", "frequency": 220, "order": 1 },
                                    { "label": "String Hz", "frequency": "220.5", "order": 2 },
                                    { "label": "Flat Unicode", "note": "A♭3", "order": 3 },
                                    { "label": "Sharp Unicode", "note": "C♯4", "order": 4 },
                                    { "label": "Flat Unicode", "note": "A♭3", "order": 5 },
                                    { "label": "Sharp Unicode", "note": "G♯4", "order": 6 }
                                ]
                            }]
                        }
                        """;

                var strings = Assert.Single(InstrumentCatalogJson.Deserialize(json)).Strings;

                Assert.Equal(7, strings.Count);
                Assert.Equal(221, strings[0].GetTargetFrequency(442), precision: 8);
                Assert.Equal(220, strings[1].GetTargetFrequency(442), precision: 8);
                Assert.Equal(220.5, strings[2].GetTargetFrequency(442), precision: 8);
                Assert.Equal("G♯3/A♭3", strings[3].DisplayNote);
                Assert.Equal("C♯4/D♭4", strings[4].DisplayNote);
                Assert.Equal("G♯3/A♭3", strings[5].DisplayNote);
                Assert.Equal("G♯4/A♭4", strings[6].DisplayNote);
                Assert.Equal(55, strings[3].MidiNote);
                Assert.Equal(55, strings[5].MidiNote);
                Assert.Equal(60, strings[4].MidiNote);
                Assert.Equal(67, strings[6].MidiNote);
        }

        [Theory]
        [InlineData("{\"midiNote\":69,\"note\":\"A4\"}")]
        [InlineData("{\"label\":\"No pitch\"}")]
        [InlineData("{\"frequency\":0}")]
        [InlineData("{\"frequency\":\"not Hz\"}")]
        [InlineData("{\"note\":\"H2\"}")]
        [InlineData("{\"note\":\"A♮4\"}")]
        [InlineData("{\"note\":\"C#4\"}")]
        [InlineData("{\"note\":\"Bb3\"}")]
        public void CatalogRejectsMissingAmbiguousOrInvalidPitchForms(string pitchProperties)
        {
                var json = $$"""
                        {"schemaVersion":1,"instruments":[{"instrumentName":"Test","tuningName":"Bad pitch","strings":[{"label":"X","order":0,{{pitchProperties.TrimStart('{')}}]}]}
                        """;

                Assert.Throws<InvalidDataException>(() => InstrumentCatalogJson.Deserialize(json));
        }

    [Fact]
    public void CatalogParserReportsMalformedJson()
    {
        Assert.Throws<JsonException>(() => InstrumentCatalogJson.Deserialize("{ invalid"));
    }

    [Fact]
    public void StrictCatalogReaderAcceptsEquivalentSlashPairAndRejectsMismatchedPair()
    {
        const string singleAccidental = """
            {"schemaVersion":1,"instruments":[{"instrumentName":"Test","tuningName":"Complete on read","strings":[{"label":"B-flat","note":"B♭3","order":0}]}]}
            """;
        const string valid = """
            {"schemaVersion":1,"instruments":[{"instrumentName":"Test","tuningName":"Pair","strings":[{"label":"B-flat","note":"A♯3/B♭3","order":0}]}]}
            """;
        const string invalid = """
            {"schemaVersion":1,"instruments":[{"instrumentName":"Test","tuningName":"Mismatch","strings":[{"label":"Bad","note":"A♯3/C4","order":0}]}]}
            """;

        var parsed = Assert.Single(InstrumentCatalogJson.Deserialize(valid));
        Assert.Equal("A♯3/B♭3", parsed.Strings[0].DisplayNote);
        Assert.Equal(PitchMath.MidiToFrequency(58), parsed.Strings[0].TargetFrequency, precision: 8);
        var completed = Assert.Single(InstrumentCatalogJson.Deserialize(singleAccidental));
        Assert.Equal("A♯3/B♭3", completed.Strings[0].DisplayNote);
        var redundant = InstrumentCatalogJson.Deserialize(valid.Replace("A♯3/B♭3", "B♯3/C4", StringComparison.Ordinal));
        Assert.Equal("C4", Assert.Single(redundant).Strings[0].DisplayNote);
        Assert.Throws<InvalidDataException>(() => InstrumentCatalogJson.Deserialize(invalid));
    }

    [Theory]
    [InlineData("♯", 100)]
    [InlineData("♭", -100)]
    [InlineData("\U0001D132", 50)]
    [InlineData("\U0001D133", -50)]
    [InlineData("\U0001D134", 150)]
    [InlineData("\U0001D135", -150)]
    public void UnicodeAccidentalsProduceExactCentOffsets(string accidental, int expectedCents)
    {
        var note = $"A{accidental}4";
        var json = $$"""
            {"schemaVersion":1,"instruments":[{"instrumentName":"Test","tuningName":"Microtonal","strings":[{"label":"A","note":"{{note}}","order":0}]}]}
            """;

        var instrumentString = Assert.Single(Assert.Single(InstrumentCatalogJson.Deserialize(json)).Strings);
        var expectedFrequency = 440 * Math.Pow(2, expectedCents / 1200d);

        Assert.Equal(69 * 100 + expectedCents, instrumentString.MidiNote * 100 + instrumentString.CentsOffset);
        Assert.Equal(PitchConversion.NormalizeEditableNote(note), instrumentString.DisplayNote);
        Assert.Equal(expectedFrequency, instrumentString.GetTargetFrequency(), precision: 10);
    }

    [Fact]
    public void CatalogParserRejectsUnsupportedSchemaAndInvalidMidiNotes()
    {
        const string unsupportedSchema = """{"schemaVersion":2,"instruments":[]}""";
        const string invalidNote = """
            {"schemaVersion":1,"instruments":[{"instrumentName":"Test","tuningName":"Bad","strings":[{"label":"X","midiNote":128,"order":0}]}]}
            """;

        Assert.Throws<InvalidDataException>(() => InstrumentCatalogJson.Deserialize(unsupportedSchema));
        Assert.Throws<InvalidDataException>(() => InstrumentCatalogJson.Deserialize(invalidNote));
    }

    [Theory]
    [InlineData(82.4069)]
    [InlineData(110.0)]
    [InlineData(329.6276)]
    public void YinFindsFundamentalWhenSecondHarmonicIsLouder(double fundamental)
    {
        const int sampleRate = 44100;
        var samples = new float[8192];
        for (var index = 0; index < samples.Length; index++)
        {
            var time = index / (double)sampleRate;
            samples[index] = (float)(
                0.18 * Math.Sin(2 * Math.PI * fundamental * time) +
                0.55 * Math.Sin(2 * Math.PI * fundamental * 2 * time) +
                0.12 * Math.Sin(2 * Math.PI * fundamental * 3 * time));
        }

        var estimate = new YinPitchDetector().Detect(samples, sampleRate);

        Assert.NotNull(estimate);
        Assert.InRange(Math.Abs(PitchMath.CentsFrom(estimate.Frequency, fundamental)), 0, 3);
        Assert.True(estimate.Confidence >= 0.75);
    }

    [Fact]
    public void YinRejectsLowLevelNoise()
    {
        var noise = new float[8192];
        var random = new Random(17);
        for (var index = 0; index < noise.Length; index++)
        {
            noise[index] = (float)((random.NextDouble() - 0.5) * 0.002);
        }

        Assert.Null(new YinPitchDetector().Detect(noise, 44100));
    }

    [Fact]
    public void OverlappingAnalyzerEmitsFramesAsSamplesArrive()
    {
        var analyzer = new OverlappingPitchAnalyzer();
        var tone = CreateTone(110, 8192, 44100);

        Assert.Empty(analyzer.AddSamples(tone.AsSpan(0, 4096)));
        Assert.Single(analyzer.AddSamples(tone.AsSpan(4096, 4096)));
        Assert.Empty(analyzer.AddSamples(tone.AsSpan(0, 2048)));
        Assert.Single(analyzer.AddSamples(tone.AsSpan(2048, 4096)));
    }

    [Fact]
    public void PitchSmootherDampensSmallFluctuationsAndResetsOnLargeJumps()
    {
        var smoother = new PitchSmoother();

        Assert.Equal(110, smoother.Update(110), precision: 8);
        Assert.InRange(smoother.Update(111), 110, 111);
        Assert.Equal(220, smoother.Update(220), precision: 8);
    }

    private static float[] CreateTone(double frequency, int length, int sampleRate)
    {
        var samples = new float[length];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = (float)(0.4 * Math.Sin(2 * Math.PI * frequency * index / sampleRate));
        }

        return samples;
    }
}
