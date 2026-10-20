using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using Flow.Launcher.Infrastructure.UserSettings;
using Microsoft.Win32;

namespace Flow.Launcher.Helper;

/// <summary>
/// Lightweight trace of the toggle hotkey path, written to Logs\&lt;version&gt;\hotkey-diag.log.
/// Used to find out why a hotkey press sometimes doesn't show the window.
/// Also watches the UI thread and records stalls, because a blocked UI thread delays WM_HOTKEY.
/// </summary>
internal static class HotkeyDiagnostics
{
    private const int StallThresholdMs = 300;
    private const int WatchdogIntervalMs = 250;

    private static readonly BlockingCollection<string> Queue = new(boundedCapacity: 1000);
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static string _filePath;
    private static int _started;

    public static void Start(Dispatcher dispatcher)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;

        _filePath = Path.Combine(DataLocation.VersionLogDirectory, "hotkey-diag.log");

        new Thread(WriterLoop) { IsBackground = true, Name = "HotkeyDiagWriter", Priority = ThreadPriority.BelowNormal }.Start();
        new Thread(() => WatchdogLoop(dispatcher)) { IsBackground = true, Name = "HotkeyDiagWatchdog" }.Start();

        // Logged so failures can be correlated with sleep/resume and lock/unlock
        SystemEvents.PowerModeChanged += (_, e) => Log($"system power: {e.Mode}");
        SystemEvents.SessionSwitch += (_, e) => Log($"session: {e.Reason}");

        Log("diagnostics started");
    }

    public static void Log(string message)
    {
        Queue.TryAdd($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [t{Environment.CurrentManagedThreadId}] {message}");
    }

    public static long Now => Clock.ElapsedMilliseconds;

    private static void WriterLoop()
    {
        foreach (var line in Queue.GetConsumingEnumerable())
        {
            try
            {
                File.AppendAllText(_filePath, line + Environment.NewLine);
            }
            catch
            {
                // Diagnostics must never break the app
            }
        }
    }

    private static void WatchdogLoop(Dispatcher dispatcher)
    {
        using var pong = new ManualResetEventSlim();
        while (!dispatcher.HasShutdownStarted)
        {
            pong.Reset();
            var sent = Now;
            dispatcher.BeginInvoke(pong.Set, DispatcherPriority.Input);

            if (!pong.Wait(StallThresholdMs))
            {
                pong.Wait();
                Log($"UI thread stalled for {Now - sent} ms (hotkey presses during this time are delayed)");
            }

            Thread.Sleep(WatchdogIntervalMs);
        }
    }
}
