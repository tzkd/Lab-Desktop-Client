using LabDesktop.Client.App.Infrastructure;

namespace LabDesktop.Client.Core.Tests;

public sealed class TurboVncInstallerServiceTests
{
    [Fact]
    public async Task RejectsAnyFileThatDoesNotMatchPinnedOfficialDigest()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "not-the-official-installer");

            Assert.False(await TurboVncInstallerService.HasExpectedSha256Async(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
