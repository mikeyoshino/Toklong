using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class DevelopmentSimulatorMobileSessionStoreTests
{
    [Fact]
    public async Task Session_survives_app_process_restart_until_cleared()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"toklong-session-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "session.json");
        var session = new StoredMobileSession(
            "access",
            "refresh",
            DateTimeOffset.Parse("2026-07-28T16:00:00+07:00"));

        try
        {
            var firstProcess =
                new DevelopmentSimulatorMobileSessionStore(path);
            await firstProcess.SaveAsync(session);

            var restartedProcess =
                new DevelopmentSimulatorMobileSessionStore(path);

            Assert.Equal(session, await restartedProcess.GetAsync());
            restartedProcess.Clear();
            Assert.Null(await firstProcess.GetAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
