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
    public void CatalogParserAllowsCommentsAndTrailingCommas()
    {
        const string json = """
            {
              // A hand-editable catalog can use comments and a trailing comma.
              "schemaVersion": 1,
              "instruments": [{
                "instrumentName": "Test instrument",
                "tuningName": "Test tuning",
                "strings": [{ "label": "1", "midiNote": 69, "order": 0, }],
              }],
            }
            """;

        Assert.Single(InstrumentCatalogJson.Deserialize(json));
    }

    [Fact]
    public void CatalogParserReportsMalformedJson()
    {
        Assert.Throws<JsonException>(() => InstrumentCatalogJson.Deserialize("{ invalid"));
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
