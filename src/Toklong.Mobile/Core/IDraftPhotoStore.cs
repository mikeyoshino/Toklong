namespace Toklong.Mobile.Core;

public interface IDraftPhotoStore
{
    Task<string> SaveAsync(
        Stream source,
        string originalFileName,
        CancellationToken cancellationToken = default);

    void Delete(string? path);
}

public sealed class DraftPhotoStore(string appDataDirectory) : IDraftPhotoStore
{
    public const long MaximumPhotoBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan StaleAge = TimeSpan.FromHours(24);
    private readonly string directory = Path.GetFullPath(
        Path.Combine(appDataDirectory, "offer-drafts"));

    public async Task<string> SaveAsync(
        Stream source,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var extension = SupportedExtension(originalFileName);
        Directory.CreateDirectory(directory);
        DeleteStaleFiles();

        var path = Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
        try
        {
            await using var destination = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(
                    buffer,
                    cancellationToken);
                if (read == 0)
                    break;
                total += read;
                if (total > MaximumPhotoBytes)
                    throw new ArgumentException(
                        "รูปต้องมีขนาดไม่เกิน 8 MB");
                await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
            }

            if (total == 0)
                throw new ArgumentException("รูปที่เลือกไม่มีข้อมูล");
            return path;
        }
        catch
        {
            Delete(path);
            throw;
        }
    }

    public void Delete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!IsManagedPath(fullPath) || !File.Exists(fullPath))
                return;
            File.Delete(fullPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string SupportedExtension(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" => ".jpg",
            ".jpeg" => ".jpeg",
            ".png" => ".png",
            ".webp" => ".webp",
            _ => throw new ArgumentException(
                "รองรับเฉพาะรูป JPG, PNG หรือ WebP")
        };

    private bool IsManagedPath(string path)
    {
        var prefix = directory.EndsWith(
            Path.DirectorySeparatorChar.ToString(),
            StringComparison.Ordinal)
            ? directory
            : $"{directory}{Path.DirectorySeparatorChar}";
        return path.StartsWith(prefix, StringComparison.Ordinal);
    }

    private void DeleteStaleFiles()
    {
        var cutoff = DateTime.UtcNow - StaleAge;
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                    Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
