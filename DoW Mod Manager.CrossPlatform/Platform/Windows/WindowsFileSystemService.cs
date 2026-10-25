using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DoW_Mod_Manager.Abstractions;
using DoW_Mod_Manager.Models;

namespace DoW_Mod_Manager.Platform.Windows;

/// <summary>
/// Windows file-system helpers. Ports:
/// <list type="bullet">
///   <item><c>OpenFolder</c> — <c>explorer.exe</c> (legacy <c>OpenFolderButton_Click</c>).</item>
///   <item><c>ShowFileProperties</c> — shell32 <c>ShellExecuteEx</c> "properties" verb
///         (legacy <c>WinApiCalls.ShowFileProperties</c>).</item>
///   <item><c>CreateShortcut</c> — a Desktop <c>.lnk</c> via the WScript.Shell COM object
///         (legacy <c>DownloadHelper.CreateShortcut</c> which used IWshRuntimeLibrary).</item>
/// </list>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFileSystemService : IFileSystemService
{
    public void OpenFolder(string path)
    {
        // Mirrors the legacy Process.Start("explorer.exe", path) behaviour.
        Process.Start("explorer.exe", path);
    }

    public void ShowFileProperties(string path)
    {
        ShowFilePropertiesNative(path);
    }

    public void CreateShortcut(ShortcutSpec spec)
    {
        // IWshRuntimeLibrary's strongly-typed COM interop is awkward on modern .NET (it needs a
        // generated interop assembly and BuiltInComInteropSupport which the project disables).
        // The most robust approach that keeps zero extra dependencies is to drive the same
        // underlying COM server (WScript.Shell) late-bound via its ProgID + reflection, which is
        // exactly what IWshShortcut does under the hood.
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string shortcutLocation = Path.Combine(desktop, spec.Name + ".lnk");

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
            throw new InvalidOperationException("WScript.Shell COM server is not available on this system.");

        object? shell = Activator.CreateInstance(shellType);
        if (shell == null)
            throw new InvalidOperationException("Could not create the WScript.Shell COM object.");

        object? shortcut = null;
        try
        {
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: new object[] { shortcutLocation });

            if (shortcut == null)
                throw new InvalidOperationException("WScript.Shell.CreateShortcut returned null.");

            Type shortcutType = shortcut.GetType();

            SetShortcutProperty(shortcutType, shortcut, "TargetPath", spec.TargetPath);

            if (!string.IsNullOrEmpty(spec.Arguments))
                SetShortcutProperty(shortcutType, shortcut, "Arguments", spec.Arguments!);

            string workingDir = spec.WorkingDirectory
                ?? Path.GetDirectoryName(spec.TargetPath)
                ?? string.Empty;
            if (!string.IsNullOrEmpty(workingDir))
                SetShortcutProperty(shortcutType, shortcut, "WorkingDirectory", workingDir);

            if (!string.IsNullOrEmpty(spec.Description))
                SetShortcutProperty(shortcutType, shortcut, "Description", spec.Description!);

            if (!string.IsNullOrEmpty(spec.IconPath))
                SetShortcutProperty(shortcutType, shortcut, "IconLocation", spec.IconPath!);

            shortcutType.InvokeMember(
                "Save",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shortcut,
                args: Array.Empty<object>());
        }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut))
                Marshal.FinalReleaseComObject(shortcut);
            if (Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }

    static void SetShortcutProperty(Type shortcutType, object shortcut, string property, string value)
    {
        shortcutType.InvokeMember(
            property,
            System.Reflection.BindingFlags.SetProperty,
            binder: null,
            target: shortcut,
            args: new object[] { value });
    }

    // --- shell32 ShellExecuteEx "properties" verb (ported from WinApiCalls) ---

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpVerb;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpFile;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpParameters;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    const int SW_SHOW = 5;
    const uint SEE_MASK_INVOKEIDLIST = 12;

    static bool ShowFilePropertiesNative(string filename)
    {
        SHELLEXECUTEINFO info = new SHELLEXECUTEINFO();
        info.cbSize = Marshal.SizeOf(info);
        info.lpVerb = "properties";
        info.lpFile = filename;
        info.nShow = SW_SHOW;
        info.fMask = SEE_MASK_INVOKEIDLIST;
        return ShellExecuteEx(ref info);
    }
}
