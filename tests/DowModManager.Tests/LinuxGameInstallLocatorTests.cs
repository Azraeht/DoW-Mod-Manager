using DoW_Mod_Manager.Models;
using DoW_Mod_Manager.Platform.Linux;
using Xunit;

namespace DowModManager.Tests
{
    public class LinuxGameInstallLocatorTests : IDisposable
    {
        private readonly string _steamApps;
        private readonly LinuxGameInstallLocator _locator = new();

        public LinuxGameInstallLocatorTests()
        {
            _steamApps = Path.Combine(Path.GetTempPath(), "dmm_locator_" + Guid.NewGuid().ToString("N"), "steamapps");
            Directory.CreateDirectory(Path.Combine(_steamApps, "common"));
        }

        public void Dispose()
        {
            var root = Path.GetDirectoryName(_steamApps)!;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        private string CreateGame(string folderName, string exe)
        {
            var dir = Path.Combine(_steamApps, "common", folderName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, exe), "");
            return dir;
        }

        private void WriteManifest(int appId, string installDir) =>
            File.WriteAllText(Path.Combine(_steamApps, $"appmanifest_{appId}.acf"),
                $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{appId}\"\n\t\"installdir\"\t\t\"{installDir}\"\n}}\n");

        [Fact]
        public void IdentifyInstall_UsesAppIdFromMatchingManifest()
        {
            var dir = CreateGame("Dawn of War Definitive Edition", "W40k.exe");
            WriteManifest(9450, "Dawn of War Soulstorm");
            WriteManifest(3556750, "Dawn of War Definitive Edition");

            var install = _locator.IdentifyInstall(dir);

            Assert.NotNull(install);
            Assert.Equal(3556750, install!.SteamAppId);
            Assert.Equal(Path.Combine(_steamApps, "compatdata", "3556750"), install.ProtonPrefix);
        }

        [Fact]
        public void IdentifyInstall_FallsBackToVariantAppIdWithoutManifest()
        {
            var dir = CreateGame("Dawn of War Soulstorm", "Soulstorm.exe");

            var install = _locator.IdentifyInstall(dir);

            Assert.NotNull(install);
            Assert.Equal(GameVariant.Soulstorm, install!.Variant);
            Assert.Equal(9450, install.SteamAppId);
        }
    }
}
