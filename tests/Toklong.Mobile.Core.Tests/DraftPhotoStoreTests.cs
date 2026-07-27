using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class DraftPhotoStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"toklong-draft-photo-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Selected_photo_is_copied_and_can_be_deleted()
    {
        var store = new DraftPhotoStore(root);
        await using var source = new MemoryStream([1, 2, 3, 4]);

        var path = await store.SaveAsync(source, "IMG_0005.jpeg");

        Assert.True(File.Exists(path));
        Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(path));
        store.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Oversized_photo_is_rejected_without_leaving_a_draft()
    {
        var store = new DraftPhotoStore(root);
        await using var source = new MemoryStream(
            new byte[DraftPhotoStore.MaximumPhotoBytes + 1]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveAsync(source, "large.jpg"));

        Assert.Empty(
            Directory.Exists(Path.Combine(root, "offer-drafts"))
                ? Directory.EnumerateFiles(
                    Path.Combine(root, "offer-drafts"))
                : []);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
