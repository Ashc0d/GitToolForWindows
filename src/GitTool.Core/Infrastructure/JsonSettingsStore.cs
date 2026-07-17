using System.Text.Json;
using GitTool.Core.Models;

namespace GitTool.Core.Infrastructure;

public sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;

    public JsonSettingsStore(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_paths.SettingsFile))
            {
                return AppSettings.CreateDefault();
            }

            await using var stream = File.OpenRead(_paths.SettingsFile);
            return await JsonSerializer.DeserializeAsync<AppSettings>(
                       stream,
                       SerializerOptions,
                       cancellationToken)
                   .ConfigureAwait(false)
                ?? AppSettings.CreateDefault();
        }
        catch (JsonException)
        {
            return AppSettings.CreateDefault();
        }
        catch (IOException)
        {
            return AppSettings.CreateDefault();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.DataRoot);
        var temporaryFile = _paths.SettingsFile + ".tmp";

        await using (var stream = File.Create(temporaryFile))
        {
            await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(temporaryFile, _paths.SettingsFile, true);
    }
}
