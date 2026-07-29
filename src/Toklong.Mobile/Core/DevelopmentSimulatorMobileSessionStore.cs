using System.Text.Json;

namespace Toklong.Mobile.Core;

public sealed class DevelopmentSimulatorMobileSessionStore(string path)
    : IMobileSessionStore
{
    private readonly string path =
        string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException(
                "Session path is required.",
                nameof(path))
            : Path.GetFullPath(path);

    public async Task<StoredMobileSession?> GetAsync()
    {
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<StoredMobileSession>(
            stream);
    }

    public async Task SaveAsync(StoredMobileSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "Session path must include a directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, session);
                await stream.FlushAsync();
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public void Clear()
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
