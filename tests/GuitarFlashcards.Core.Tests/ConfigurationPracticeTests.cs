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
        var settings = new PracticeSettings(1.25, 3.75, false, false)
        {
            StrumIntervalSeconds = 1.75,
            ShowNextChord = false,
            Mode = PracticeMode.Manual
        };
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
        Assert.True(legacy.Settings.ShowNextChord);
        Assert.Equal(PracticeMode.Timed, legacy.Settings.Mode);
        Assert.Throws<ArgumentException>(() => ConfigurationJson.Validate(AppConfiguration.Default with { SchemaVersion = 2 }));
    }

    [Fact]
    public async Task ManualPracticePreviewsNextChordAndAdvancesOnlyAfterManualSignal()
    {
        var first = new ChordDefinition(Guid.NewGuid(), "First", GuitarFingering.Open(), null);
        var second = new ChordDefinition(Guid.NewGuid(), "Second", GuitarFingering.Open(), null);
        var runtime = new FakePracticeRuntime(0.5);
        var engine = new PracticeEngine(runtime);
        IReadOnlyList<ChordDefinition> enabled = [first, second];
        var countdown = new List<(int Seconds, Guid? NextChord)>();
        var appearances = new List<(Guid Current, Guid? Next)>();
        var advances = 0;

        var result = await engine.RunWithPreviewAsync(
            () => enabled,
            new PracticeSettings { Mode = PracticeMode.Manual },
            (seconds, next) =>
            {
                countdown.Add((seconds, next?.Id));
                return ValueTask.CompletedTask;
            },
            (current, next, _) =>
            {
                appearances.Add((current.Id, next?.Id));
                return ValueTask.CompletedTask;
            },
            _ =>
            {
                advances++;
                if (advances == 2)
                {
                    enabled = [];
                }
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(PracticeEndReason.NoEnabledChords, result);
        Assert.Equal([(3, first.Id), (2, first.Id), (1, first.Id)], countdown);
        Assert.Equal([(first.Id, second.Id), (second.Id, first.Id)], appearances);
        Assert.Equal(2, advances);
        Assert.Equal(3, runtime.Delays.Count(delay => delay == TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task ManualPracticeWaitIsCancellableAndDoesNotAdvanceEarly()
    {
        var chord = new ChordDefinition(Guid.NewGuid(), "Only", GuitarFingering.Open(), null);
        var engine = new PracticeEngine(new FakePracticeRuntime(0.5));
        using var cancellation = new CancellationTokenSource();
        var appearances = 0;
        var waitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = engine.RunWithPreviewAsync(
            () => [chord],
            new PracticeSettings { Mode = PracticeMode.Manual },
            (_, _) => ValueTask.CompletedTask,
            (_, _, _) =>
            {
                appearances++;
                return ValueTask.CompletedTask;
            },
            async token =>
            {
                waitStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            },
            cancellation.Token);

        await waitStarted.Task;
        Assert.Equal(1, appearances);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, appearances);
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
    public async Task SplitFileStoreKeepsDocumentsIndependentAndIgnoresLegacyConfiguration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GuitarFlashcards", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(directory, "practice-settings.json");
        var chordsPath = Path.Combine(directory, "chord-library.json");
        var legacyPath = Path.Combine(directory, "guitar-flashcards.json");
        try
        {
            Directory.CreateDirectory(directory);
            var legacyChord = new ChordLibrary().Add("Legacy chord", GuitarFingering.Open());
            await File.WriteAllTextAsync(legacyPath, ConfigurationJson.Serialize(new AppConfiguration(
                1,
                [legacyChord],
                new PracticeSettings(5, 8))));

            var store = new GuitarFlashcards.Core.Persistence.SplitConfigurationFileStore(settingsPath, chordsPath);
            var initiallyLoaded = await store.LoadAsync();
            Assert.Empty(initiallyLoaded.Chords);
            Assert.Equal(new PracticeSettings(), initiallyLoaded.Settings);

            var settings = new PracticeSettings(1.5, 3.25, false, true)
            {
                StrumIntervalSeconds = 1.75,
                ShowNextChord = false,
                Mode = PracticeMode.Manual
            };
            await store.SaveSettingsAsync(settings);
            Assert.True(File.Exists(settingsPath));
            Assert.False(File.Exists(chordsPath));

            var chord = new ChordLibrary().Add("Open E", GuitarFingering.Open());
            await store.SaveChordsAsync([chord]);
            var restored = await store.LoadAsync();
            Assert.Equal(settings, restored.Settings);
            Assert.Equal(chord.Id, Assert.Single(restored.Chords).Id);

            var persistedSettings = await File.ReadAllTextAsync(settingsPath);
            var persistedChords = await File.ReadAllTextAsync(chordsPath);
            Assert.Throws<System.Text.Json.JsonException>(() => PracticeSettingsJson.Deserialize(persistedChords));
            Assert.Throws<System.Text.Json.JsonException>(() => ChordLibraryJson.Deserialize(persistedSettings));
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => store.ImportSettingsAsync("{ malformed"));
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => store.ImportChordsAsync("{ malformed"));

            Assert.Equal(persistedSettings, await File.ReadAllTextAsync(settingsPath));
            Assert.Equal(persistedChords, await File.ReadAllTextAsync(chordsPath));
            Assert.True(File.Exists(legacyPath));
        }
        finally
        {
            if (Directory.Exists(directory))
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

    [Fact]
    public async Task PracticePreviewShowsFirstChordDuringCountdownAndNextChordDuringRun()
    {
        var first = new ChordDefinition(Guid.NewGuid(), "First", GuitarFingering.Open(), null);
        var second = new ChordDefinition(Guid.NewGuid(), "Second", GuitarFingering.Open(), null);
        var engine = new PracticeEngine(new FakePracticeRuntime(0.5));
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<ChordDefinition> enabled = [first, second];
        var countdownPreviews = new List<(int Seconds, Guid? ChordId)>();
        var appearances = new List<(Guid Current, Guid? Next)>();

        var result = await engine.RunWithPreviewAsync(
            () => enabled,
            new PracticeSettings(),
            (seconds, next) =>
            {
                countdownPreviews.Add((seconds, next?.Id));
                return ValueTask.CompletedTask;
            },
            (current, next, _) =>
            {
                appearances.Add((current.Id, next?.Id));
                if (appearances.Count == 2)
                {
                    enabled = [];
                }
                return ValueTask.CompletedTask;
            },
            _ => Task.CompletedTask,
            cancellation.Token);

        Assert.Equal(PracticeEndReason.NoEnabledChords, result);
        Assert.All(countdownPreviews, preview => Assert.Equal(first.Id, preview.ChordId));
        Assert.Equal([3, 2, 1], countdownPreviews.Select(preview => preview.Seconds));
        Assert.Equal((first.Id, second.Id), appearances[0]);
        Assert.Equal((second.Id, first.Id), appearances[1]);
    }

    [Fact]
    public async Task ManualPracticeWaitsForAdvanceInsteadOfTimedDisplayDelay()
    {
        var first = new ChordDefinition(Guid.NewGuid(), "First", GuitarFingering.Open(), null);
        var second = new ChordDefinition(Guid.NewGuid(), "Second", GuitarFingering.Open(), null);
        var runtime = new FakePracticeRuntime(0.5);
        var engine = new PracticeEngine(runtime);
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<ChordDefinition> enabled = [first, second];
        var appearances = 0;
        var manualAdvances = 0;

        var result = await engine.RunWithPreviewAsync(
            () => enabled,
            new PracticeSettings { Mode = PracticeMode.Manual },
            (_, _) => ValueTask.CompletedTask,
            (current, _, _) =>
            {
                appearances++;
                if (appearances == 2)
                {
                    enabled = [];
                }
                return ValueTask.CompletedTask;
            },
            _ =>
            {
                manualAdvances++;
                return Task.CompletedTask;
            },
            cancellation.Token);

        Assert.Equal(PracticeEndReason.NoEnabledChords, result);
        Assert.Equal(2, appearances);
        Assert.Equal(2, manualAdvances);
        Assert.Equal(3, runtime.Delays.Count(delay => delay == TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task ManualPracticeWaitCanBeCancelledWhileHoldingCurrentChord()
    {
        var chord = new ChordDefinition(Guid.NewGuid(), "Only", GuitarFingering.Open(), null);
        var engine = new PracticeEngine(new FakePracticeRuntime(0.5));
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.RunWithPreviewAsync(
            () => [chord],
            new PracticeSettings { Mode = PracticeMode.Manual },
            (_, _) => ValueTask.CompletedTask,
            (_, _, _) =>
            {
                cancellation.Cancel();
                return ValueTask.CompletedTask;
            },
            token => Task.Delay(Timeout.Infinite, token),
            cancellation.Token));
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