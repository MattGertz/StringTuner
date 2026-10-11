using GuitarFlashcards.Core.Domain;
using GuitarFlashcards.Core.Practice;

namespace GuitarFlashcards.Core.Tests;

public sealed class ConfigurationPracticeTests
{
    [Fact]
    public void LibraryOrdersNamesAndPreservesIdAndEnabledStateDuringEdit()
    {
        var library = new ChordLibrary();
        var zed = library.Add("Zed", GuitarFingering.Open());
        var alpha = library.Add("alpha", GuitarFingering.Open());
        library.SetEnabled(zed.Id, false);

        var updated = library.Update(zed.Id, "Beta", GuitarFingering.Open());

        Assert.Equal(["alpha", "Beta"], library.Chords.Select(chord => chord.DisplayName));
        Assert.Equal(zed.Id, updated.Id);
        Assert.False(updated.Enabled);
        Assert.Single(library.EnabledChords);
        Assert.True(library.Delete(alpha.Id));
    }

    [Fact]
    public void ConfigurationRoundTripPreservesChordAndFractionalSettings()
    {
        var chord = new ChordLibrary().Add("Open E", GuitarFingering.Open());
        var settings = new PracticeSettings(1.25, 3.75, false, false) { StrumIntervalSeconds = 1.75 };
        var original = new AppConfiguration(1, [chord], settings);

        var restored = ConfigurationJson.Deserialize(ConfigurationJson.Serialize(original));

        Assert.Equal(original.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(settings, restored.Settings);
        var restoredChord = Assert.Single(restored.Chords);
        Assert.Equal(chord.Id, restoredChord.Id);
        Assert.Equal(chord.DisplayName, restoredChord.DisplayName);
        Assert.Equal(chord.Fingering.Strings, restoredChord.Fingering.Strings);
        Assert.Equal(chord.DerivedChordName, restoredChord.DerivedChordName);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 2)]
    [InlineData(4, 2)]
    public void ConfigurationRejectsInvalidDurations(double minimum, double maximum)
    {
        var configuration = AppConfiguration.Default with { Settings = new PracticeSettings(minimum, maximum) };

        Assert.Throws<ArgumentException>(() => ConfigurationJson.Validate(configuration));
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(1.0)]
    [InlineData(4.0)]
    public void ConfigurationAcceptsBoundaryStrumIntervals(double interval)
    {
        var configuration = AppConfiguration.Default with
        {
            Settings = new PracticeSettings { StrumIntervalSeconds = interval }
        };

        ConfigurationJson.Validate(configuration);
    }

    [Theory]
    [InlineData(0.249)]
    [InlineData(4.001)]
    [InlineData(double.NaN)]
    public void ConfigurationRejectsOutOfRangeStrumIntervals(double interval)
    {
        var configuration = AppConfiguration.Default with
        {
            Settings = new PracticeSettings { StrumIntervalSeconds = interval }
        };

        Assert.Throws<ArgumentException>(() => ConfigurationJson.Validate(configuration));
    }

    [Fact]
    public void ConfigurationRejectsDuplicateIdsAndInconsistentDerivedMetadata()
    {
        var chord = new ChordLibrary().Add("First", GuitarFingering.Open());
        var duplicateId = chord with { DisplayName = "Second" };
        Assert.Throws<ArgumentException>(() => ConfigurationJson.Validate(new AppConfiguration(1, [chord, duplicateId], new PracticeSettings())));

        var badMetadata = chord with { DerivedChordName = "G7" };
        Assert.Throws<ArgumentException>(() => ConfigurationJson.Validate(new AppConfiguration(1, [badMetadata], new PracticeSettings())));
    }

    [Fact]
    public void ConfigurationRejectsMissingRequiredJsonFieldsAndUnsupportedVersions()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => ConfigurationJson.Deserialize("{\"schemaVersion\":1,\"chords\":[],\"settings\":{\"minimumDurationSeconds\":2,\"maximumDurationSeconds\":4}}"));
        var legacy = ConfigurationJson.Deserialize("{\"schemaVersion\":1,\"chords\":[],\"settings\":{\"minimumDurationSeconds\":2,\"maximumDurationSeconds\":4,\"showFingering\":true,\"playChordDuringRun\":true}}");
        Assert.Equal(1.0, legacy.Settings.StrumIntervalSeconds);
        Assert.Throws<ArgumentException>(() => ConfigurationJson.Validate(AppConfiguration.Default with { SchemaVersion = 2 }));
    }

    [Fact]
    public async Task FileStorePersistsAcrossInstancesAndInvalidImportIsAtomic()
    {
        var path = Path.Combine(Path.GetTempPath(), "GuitarFlashcards", Guid.NewGuid().ToString("N"), "config.json");
        try
        {
            var chord = new ChordLibrary().Add("Open E", GuitarFingering.Open());
            var original = new AppConfiguration(1, [chord], new PracticeSettings(1.5, 3.25, false, true));
            var store = new GuitarFlashcards.Core.Persistence.ConfigurationFileStore(path);
            await store.SaveAsync(original);

            var restartedStore = new GuitarFlashcards.Core.Persistence.ConfigurationFileStore(path);
            var restored = await restartedStore.LoadAsync();
            Assert.Equal(chord.Id, Assert.Single(restored.Chords).Id);
            Assert.Equal(original.Settings, restored.Settings);
            var persistedJson = await File.ReadAllTextAsync(path);

            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => restartedStore.ImportAsync("{ malformed"));

            Assert.Equal(persistedJson, await File.ReadAllTextAsync(path));
        }
        finally
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PracticeCountsDownSelectsEnabledWithoutRepeatingAndHonorsDurationRange()
    {
        var first = new ChordDefinition(Guid.NewGuid(), "First", GuitarFingering.Open(), null);
        var second = new ChordDefinition(Guid.NewGuid(), "Second", GuitarFingering.Open(), null);
        var disabled = new ChordDefinition(Guid.NewGuid(), "Disabled", GuitarFingering.Open(), null, false);
        var runtime = new FakePracticeRuntime(0.25, 0.75);
        var engine = new PracticeEngine(runtime);
        using var cancellation = new CancellationTokenSource();
        var countdown = new List<int>();
        var appearances = new List<(Guid Id, double Duration)>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.RunAsync(
            () => [first, disabled, second],
            new PracticeSettings(2.5, 4.5),
            value => { countdown.Add(value); return ValueTask.CompletedTask; },
            (chord, duration) =>
            {
                appearances.Add((chord.Id, duration));
                if (appearances.Count == 2)
                {
                    cancellation.Cancel();
                }
                return ValueTask.CompletedTask;
            },
            cancellation.Token));

        Assert.Equal([3, 2, 1], countdown);
        Assert.Equal(3, runtime.Delays.Take(3).Count(delay => delay == TimeSpan.FromSeconds(1)));
        Assert.Equal(2, appearances.Count);
        Assert.NotEqual(appearances[0].Id, appearances[1].Id);
        Assert.DoesNotContain(appearances, appearance => appearance.Id == disabled.Id);
        Assert.Equal(3, appearances[0].Duration, precision: 8);
        Assert.Equal(4, appearances[1].Duration, precision: 8);
    }

    [Fact]
    public async Task PracticeRepeatsTheOnlyChordAndStopsWhenLibraryBecomesDisabled()
    {
        var chord = new ChordDefinition(Guid.NewGuid(), "Only", GuitarFingering.Open(), null);
        var runtime = new FakePracticeRuntime(0, 0.5, 0, 0.5);
        var engine = new PracticeEngine(runtime);
        var appearances = 0;
        IReadOnlyList<ChordDefinition> enabled = [chord];

        var result = await engine.RunAsync(
            () => enabled,
            new PracticeSettings(),
            _ => ValueTask.CompletedTask,
            (_, _) =>
            {
                appearances++;
                if (appearances == 2)
                {
                    enabled = [];
                }
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(PracticeEndReason.NoEnabledChords, result);
        Assert.Equal(2, appearances);
    }

    [Fact]
    public async Task PracticeCancellationDuringCountdownDoesNotShowAChord()
    {
        var chord = new ChordDefinition(Guid.NewGuid(), "Only", GuitarFingering.Open(), null);
        var engine = new PracticeEngine(new FakePracticeRuntime(0.5));
        using var cancellation = new CancellationTokenSource();
        var appearanceCount = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.RunAsync(
            () => [chord],
            new PracticeSettings(),
            value =>
            {
                if (value == 3)
                {
                    cancellation.Cancel();
                }
                return ValueTask.CompletedTask;
            },
            (_, _) =>
            {
                appearanceCount++;
                return ValueTask.CompletedTask;
            },
            cancellation.Token));

        Assert.Equal(0, appearanceCount);
    }

    private sealed class FakePracticeRuntime(params double[] fractions) : IPracticeRuntime
    {
        private int _fractionIndex;
        private long _elapsedTicks;

        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(duration);
            _elapsedTicks += duration.Ticks;
            return Task.CompletedTask;
        }

        public long GetTimestamp() => _elapsedTicks;

        public TimeSpan GetElapsedTime(long timestamp) => TimeSpan.FromTicks(_elapsedTicks - timestamp);

        public int NextIndex(int exclusiveMaximum) => 0;

        public double NextFraction() => fractions[_fractionIndex++ % fractions.Length];
    }
}