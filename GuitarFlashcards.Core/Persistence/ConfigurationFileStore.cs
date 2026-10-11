using System.Text;
using GuitarFlashcards.Core.Domain;

namespace GuitarFlashcards.Core.Persistence;

public sealed class ConfigurationFileStore(string filePath)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath))
            {
                return AppConfiguration.Default;
            }

            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            return ConfigurationJson.Deserialize(json);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var json = ConfigurationJson.Serialize(configuration);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), cancellationToken);
                File.Move(temporaryPath, filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AppConfiguration> ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        var configuration = ConfigurationJson.Deserialize(json);
        await SaveAsync(configuration, cancellationToken);
        return configuration;
    }
}