using System.Text;
using CommunityToolkit.Maui.Storage;
using GuitarFlashcards.Core.Domain;
using GuitarFlashcards.Core.Persistence;
using Microsoft.Maui.Storage;

namespace GuitarFlashcards.Services;

public sealed class LocalConfigurationStore
{
    private readonly ConfigurationFileStore _store = new(Path.Combine(FileSystem.AppDataDirectory, "guitar-flashcards.json"));

    public Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
        _store.LoadAsync(cancellationToken);

    public Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default) =>
        _store.SaveAsync(configuration, cancellationToken);

    public static async Task<bool> ExportAsync(string json, string fileName, CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await FileSaver.Default.SaveAsync(fileName, stream, cancellationToken);
        return result.IsSuccessful;
    }

    public static async Task<AppConfiguration> ReadImportAsync(CancellationToken cancellationToken = default)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select a Guitar Flashcards configuration",
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
        var json = await reader.ReadToEndAsync(cancellationToken);
        return ConfigurationJson.Deserialize(json);
    }
}