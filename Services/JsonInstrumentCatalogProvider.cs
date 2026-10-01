using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using StringTuner.Core.Domain;

namespace StringTuner.Services;

public sealed class JsonInstrumentCatalogProvider(ILogger<JsonInstrumentCatalogProvider> logger) : IInstrumentCatalogProvider
{
    private const string FileName = "instruments.json";

    public async Task<InstrumentCatalogLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filePaths = new[]
        {
            Path.Combine(FileSystem.AppDataDirectory, FileName),
            Path.Combine(AppContext.BaseDirectory, FileName)
        };

        foreach (var path in filePaths)
        {
            if (!checkedPaths.Add(path) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken);
                return Parse(json, path);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not read instrument catalog from {CatalogPath}.", path);
                continue;
            }
        }

        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(FileName);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync(cancellationToken);
            return Parse(json, "packaged instruments.json");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not load the packaged instrument catalog.");
            return new([], $"Could not load instruments.json. {exception.Message}");
        }
    }

    private InstrumentCatalogLoadResult Parse(string json, string source)
    {
        try
        {
            return new(InstrumentCatalogJson.Deserialize(json));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            logger.LogError(exception, "Invalid instrument catalog at {CatalogSource}.", source);
            return new([], $"Invalid {source}: {exception.Message}");
        }
    }
}