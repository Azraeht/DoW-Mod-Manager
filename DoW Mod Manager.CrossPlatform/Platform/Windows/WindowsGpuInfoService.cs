using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using DoW_Mod_Manager.Abstractions;
using DoW_Mod_Manager.Models;

namespace DoW_Mod_Manager.Platform.Windows;

/// <summary>
/// Enumerates installed GPUs via WMI <c>Win32_VideoController</c>. Ported from the legacy
/// <c>SettingsManagerForm.GetAllVideocards()</c>: only adapters with a non-null
/// <c>CurrentBitsPerPixel</c> (i.e. currently driving a display) are reported, matching the
/// original behaviour that filtered out inactive / mirror adapters.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGpuInfoService : IGpuInfoService
{
    public Task<IReadOnlyList<GpuInfo>> GetGpusAsync(CancellationToken ct = default)
    {
        // WMI is synchronous; run it off the calling thread so callers can await it.
        return Task.Run<IReadOnlyList<GpuInfo>>(() =>
        {
            var gpus = new List<GpuInfo>();

            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (ManagementBaseObject mbo in searcher.Get())
            {
                ct.ThrowIfCancellationRequested();

                PropertyData currentBitsPerPixel = mbo.Properties["CurrentBitsPerPixel"];
                PropertyData description = mbo.Properties["Description"];
                PropertyData? driverVersion = TryGetProperty(mbo, "DriverVersion");

                if (currentBitsPerPixel != null && description != null)
                {
                    if (currentBitsPerPixel.Value != null && description.Value != null)
                    {
                        gpus.Add(new GpuInfo
                        {
                            Description = description.Value.ToString() ?? "Unknown adapter",
                            Driver = driverVersion?.Value?.ToString()
                        });
                    }
                }

                mbo.Dispose();
            }

            return gpus;
        }, ct);
    }

    static PropertyData? TryGetProperty(ManagementBaseObject mbo, string name)
    {
        try { return mbo.Properties[name]; }
        catch (Exception) { return null; }
    }
}
