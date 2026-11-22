using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Flow.Launcher.Plugin.Explorer.Helper;

/// <summary>
/// Opens file results in PinDesk (https://github.com/1xmanMAX/PINDESK-) instead of their
/// default application, when the "Open files in PinDesk" setting is on and PinDesk is installed.
/// PinDesk is launched as <c>pindesk.exe --desde-flow "&lt;file&gt;"</c>: if it is already running,
/// that copy forwards the file to the running instance and exits (PinDesk is single-instance);
/// PinDesk itself opens with the file's normal application whatever it has no viewer for.
/// PinDesk is found the same way as its Flow Launcher plugin does: the per-user install folder
/// (<c>%LOCALAPPDATA%\Programs\PinDesk\pindesk.exe</c>, or the older PixPinMax one), and then the
/// "Open with" registration PinDesk writes for itself (portable copies).
/// </summary>
public static class PinDeskHelper
{
    /// <summary>The origin flag PinDesk understands (apps/pindesk/src/desde_flow.rs).</summary>
    public const string FromFlowArgument = "--desde-flow";

    /// <summary>
    /// Files that are never sent to PinDesk: programs, shortcuts and scripts must run as always.
    /// </summary>
    private static readonly HashSet<string> NeverInPinDesk = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".appinstaller",
        ".application", ".appref-ms", ".lnk", ".url", ".website", ".pif", ".cpl", ".msc", ".bat",
        ".cmd", ".ps1", ".psm1", ".psd1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta",
        ".reg", ".jar", ".ahk", ".sys", ".dll", ".inf"
    };

    private static readonly object Lock = new();
    private static string _cachedExe;
    private static DateTime _cachedAt = DateTime.MinValue;

    /// <summary>The PinDesk executable, or null if PinDesk is not installed.</summary>
    public static string FindExecutable()
    {
        lock (Lock)
        {
            // Looking costs a couple of file checks and a registry read: remember it for a while,
            // but notice soon enough when PinDesk is installed or removed.
            if (DateTime.UtcNow - _cachedAt < TimeSpan.FromSeconds(30))
                return _cachedExe;
            _cachedExe = Find();
            _cachedAt = DateTime.UtcNow;
            return _cachedExe;
        }
    }

    /// <summary>Whether PinDesk is installed on this PC.</summary>
    public static bool IsInstalled() => FindExecutable() != null;

    private static string Find()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                var programs = Path.Combine(localAppData, "Programs");
                var current = Path.Combine(programs, "PinDesk", "pindesk.exe");
                if (File.Exists(current))
                    return current;
                var old = Path.Combine(programs, "PixPinMax", "pixpinmax.exe");
                if (File.Exists(old))
                    return old;
            }

            foreach (var exeName in new[] { "pindesk.exe", "pixpinmax.exe" })
            {
                var fromRegistry = FromOpenWithRegistration(exeName);
                if (fromRegistry != null)
                    return fromRegistry;
            }
        }
        catch (Exception)
        {
            // Not being able to look is the same as not having it: files open as always.
        }

        return null;
    }

    /// <summary>
    /// The exe of PinDesk's "Open with" entry, <c>HKCU\Software\Classes\Applications\pindesk.exe</c>,
    /// whose command is <c>"C:\...\pindesk.exe" "%1"</c>.
    /// </summary>
    private static string FromOpenWithRegistration(string exeName)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\Applications\{exeName}\shell\open\command");
        if (key?.GetValue(null) is not string command)
            return null;
        var exe = ExecutableOfCommand(command);
        return exe != null && File.Exists(exe) ? exe : null;
    }

    /// <summary>The program of a shell command line: the first quoted part, or up to ".exe".</summary>
    internal static string ExecutableOfCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }

        var exeEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeEnd > 0 ? command[..(exeEnd + 4)] : null;
    }

    /// <summary>
    /// Whether this path should go to PinDesk: an existing file (not a folder) that is not a
    /// program, a shortcut or a script.
    /// </summary>
    public static bool ShouldOpenInPinDesk(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return false;
        var extension = Path.GetExtension(path);
        return !NeverInPinDesk.Contains(extension);
    }

    /// <summary>
    /// Opens <paramref name="filePath"/> in PinDesk. False if PinDesk is not installed, the file
    /// is not one for PinDesk, or PinDesk could not be started: then the caller opens it as always.
    /// </summary>
    public static bool TryOpen(string filePath)
    {
        if (!ShouldOpenInPinDesk(filePath))
            return false;
        var exe = FindExecutable();
        if (exe == null)
            return false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty
            };
            psi.ArgumentList.Add(FromFlowArgument);
            psi.ArgumentList.Add(filePath);
            using var process = Process.Start(psi);
            return process != null;
        }
        catch (Exception)
        {
            // Forget it so the next attempt looks again (it may have been moved or uninstalled).
            lock (Lock)
            {
                _cachedAt = DateTime.MinValue;
            }

            return false;
        }
    }
}
