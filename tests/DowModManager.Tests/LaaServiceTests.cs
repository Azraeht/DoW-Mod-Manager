using DoW_Mod_Manager.Services;
using Xunit;

namespace DowModManager.Tests
{
    public class LaaServiceTests : IDisposable
    {
        private readonly string _dir;
        private readonly ILaaService _service = new LaaService();

        public LaaServiceTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "dmm_laa_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }

        /// <summary>
        /// Builds a minimal but structurally valid PE image:
        /// MZ header, e_lfanew at 0x3C pointing to a PE signature, COFF header with a
        /// Characteristics field, and an optional header large enough to contain the
        /// checksum field at optionalHeaderStart + 0x40.
        /// </summary>
        private static byte[] BuildMinimalPe(bool laaSet)
        {
            // Layout: PE header at 0x80, leaving plenty of room.
            const int peHeaderPos = 0x80;
            // peHeaderPos + 0x18 = optional header start; checksum at +0x40; field is 4 bytes.
            // Allocate generously and pad to a multiple of 4 for the checksum walk.
            int size = peHeaderPos + 0x18 + 0x40 + 0x40;
            if (size % 4 != 0) size += 4 - (size % 4);

            var buf = new byte[size];

            // MZ header.
            buf[0] = 0x4D; // 'M'
            buf[1] = 0x5A; // 'Z'

            // e_lfanew at 0x3C -> peHeaderPos.
            BitConverter.GetBytes(peHeaderPos).CopyTo(buf, 0x3C);

            // PE signature "PE\0\0" at peHeaderPos.
            buf[peHeaderPos] = 0x50;     // 'P'
            buf[peHeaderPos + 1] = 0x45; // 'E'
            buf[peHeaderPos + 2] = 0x00;
            buf[peHeaderPos + 3] = 0x00;

            // COFF header machine + sections (unused values, leave as zero).

            // Optional header magic field starts at peHeaderPos + 0x18. The legacy code
            // reads "Size of Optional Header" at peHeaderPos + 0x14 but does not use it for
            // the checksum position, so we leave it zero.

            // Characteristics (LAA flag) is a 16-bit field at peHeaderPos + 0x16.
            short characteristics = (short)(laaSet ? 0x20 : 0x00);
            BitConverter.GetBytes(characteristics).CopyTo(buf, peHeaderPos + 0x16);

            return buf;
        }

        [Theory]
        [InlineData(false)] // Steam algorithm
        [InlineData(true)]  // GOG algorithm
        public void IsLargeAddressAware_ReflectsFlag(bool gog)
        {
            string set = Path.Combine(_dir, "set.exe");
            string clear = Path.Combine(_dir, "clear.exe");
            File.WriteAllBytes(set, BuildMinimalPe(laaSet: true));
            File.WriteAllBytes(clear, BuildMinimalPe(laaSet: false));

            Assert.True(_service.IsLargeAddressAware(set, gog));
            Assert.False(_service.IsLargeAddressAware(clear, gog));
        }

        [Theory]
        [InlineData(false)] // Steam algorithm
        [InlineData(true)]  // GOG algorithm
        public void ToggleLAA_FlipsFlag(bool gog)
        {
            string exe = Path.Combine(_dir, "toggle.exe");
            File.WriteAllBytes(exe, BuildMinimalPe(laaSet: false));

            Assert.False(_service.IsLargeAddressAware(exe, gog));

            bool nowSet = _service.ToggleLAA(exe, gog);
            Assert.True(nowSet);
            Assert.True(_service.IsLargeAddressAware(exe, gog));

            bool afterSecond = _service.ToggleLAA(exe, gog);
            Assert.False(afterSecond);
            Assert.False(_service.IsLargeAddressAware(exe, gog));
        }

        [Fact]
        public void IsLargeAddressAware_ReturnsFalse_ForNonPe()
        {
            string notExe = Path.Combine(_dir, "notexe.bin");
            File.WriteAllBytes(notExe, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 });

            Assert.False(_service.IsLargeAddressAware(notExe, gogVersion: false));
        }
    }
}
