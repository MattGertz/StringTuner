using System.Text;
using CommunityToolkit.Maui.Storage;
using GuitarFlashcards.Core.Domain;
using GuitarFlashcards.Core.Persistence;
using Microsoft.Maui.Storage;

namespace GuitarFlashcards.Services;

public sealed class LocalConfigurationStore
{
    private readonly SplitConfigurationFileStore _store = new(
        Path.Combine(FileSystem.AppDataDirectory, "practice-settings.json"),
        Path.Combine(FileSystem.AppDataDirectory, "chord-library.json"));

    public Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
        _store.LoadAsync(cancellationToken);

    public Task SaveSettingsAsync(PracticeSettings settings, CancellationToken cancellationToken = default) =>
        _store.SaveSettingsAsync(settings, cancellationToken);

    public Task SaveChordsAsync(IReadOnlyList<ChordDefinition> chords, CancellationToken cancellationToken = default) =>
        _store.SaveChordsAsync(chords, cancellationToken);

    public static Task<bool> ExportPracticeSettingsAsync(PracticeSettings settings, CancellationToken cancellationToken = default) =>
        ExportJsonAsync(PracticeSettingsJson.Serialize(settings), "GuitarFlashcards-PracticeSettings.json", cancellationToken);

    public static Task<bool> ExportChordLibraryAsync(IReadOnlyList<ChordDefinition> chords, CancellationToken cancellationToken = default) =>
        ExportJsonAsync(ChordLibraryJson.Serialize(chords), "GuitarFlashcards-ChordLibrary.json", cancellationToken);

    public static async Task<PracticeSettings> ReadPracticeSettingsImportAsync(CancellationToken cancellationToken = default) =>
        PracticeSettingsJson.Deserialize(await ReadImportJsonAsync("Select practice settings JSON", cancellationToken));

    public static async Task<IReadOnlyList<ChordDefinition>> ReadChordLibraryImportAsync(CancellationToken cancellationToken = default) =>
        ChordLibraryJson.Deserialize(await ReadImportJsonAsync("Select chord library JSON", cancellationToken));

    private static async Task<bool> ExportJsonAsync(string json, string fileName, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await FileSaver.Default.SaveAsync(fileName, stream, cancellationToken);
        return result.IsSuccessful;
    }

    private static async Task<string> ReadImportJsonAsync(string pickerTitle, CancellationToken cancellationToken)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = pickerTitle,
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.WinUI] = [".json"],
                [DevicePlatform.Android] = ["application/json"]
            })
        });

        if (file is null)
        {
            throw new OperationCanceledException("Import was cancelled.", cancellationToken);
        }

        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}