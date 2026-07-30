namespace Toklong.Mobile.ViewModels;

internal static class Clipboard
{
    public static IClipboard Default { get; } = new NoOpClipboard();
}

internal interface IClipboard
{
    Task SetTextAsync(string text);
}

internal sealed class NoOpClipboard : IClipboard
{
    public Task SetTextAsync(string text) => Task.CompletedTask;
}

internal static class Share
{
    public static IShare Default { get; } = new NoOpShare();
}

internal interface IShare
{
    Task RequestAsync(object request);
}

internal sealed class NoOpShare : IShare
{
    public Task RequestAsync(object request) => Task.CompletedTask;
}

internal sealed class ShareTextRequest
{
    public string? Title { get; init; }
    public string? Text { get; init; }
}

internal sealed class ShareFileRequest
{
    public ShareFileRequest(string title, ShareFile file)
    {
        _ = title;
        _ = file;
    }
}

internal sealed class ShareFile
{
    public ShareFile(string path) => _ = path;
}

internal static class FileSystem
{
    public static string CacheDirectory => Path.GetTempPath();
}

internal static class MediaPicker
{
    public static IMediaPicker Default { get; } = new NoOpMediaPicker();
}

internal interface IMediaPicker
{
    Task<FileResult?> PickPhotoAsync();
}

internal sealed class NoOpMediaPicker : IMediaPicker
{
    public Task<FileResult?> PickPhotoAsync() => Task.FromResult<FileResult?>(null);
}

internal sealed class FileResult
{
    public string FileName => "";
    public string? ContentType => null;
    public Task<Stream> OpenReadAsync() =>
        Task.FromResult<Stream>(Stream.Null);
}
