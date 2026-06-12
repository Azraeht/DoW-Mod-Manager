namespace DoW_Mod_Manager.Services
{
    /// <summary>
    /// Reads and toggles the PE <c>IMAGE_FILE_LARGE_ADDRESS_AWARE</c> flag of an executable.
    /// Pure byte/stream manipulation, fully cross-platform.
    /// </summary>
    public interface ILaaService
    {
        /// <summary>
        /// Returns true when the executable at <paramref name="exePath"/> has the
        /// Large-Address-Aware flag set.
        /// </summary>
        /// <param name="exePath">Path to a Windows PE executable.</param>
        /// <param name="gogVersion">
        /// True to use the legacy GOG algorithm (<c>LAATweaksOld</c>); false to use the
        /// Steam algorithm (<c>LAATweaks</c>).
        /// </param>
        bool IsLargeAddressAware(string exePath, bool gogVersion);

        /// <summary>
        /// Flips the Large-Address-Aware flag of the executable at <paramref name="exePath"/>
        /// and recomputes the PE checksum. Returns the new LAA state (true when now set).
        /// </summary>
        /// <param name="exePath">Path to a Windows PE executable.</param>
        /// <param name="gogVersion">See <see cref="IsLargeAddressAware(string,bool)"/>.</param>
        bool ToggleLAA(string exePath, bool gogVersion);
    }
}
