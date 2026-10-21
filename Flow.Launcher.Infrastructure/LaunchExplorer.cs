using System;
using System.IO;
using System.Threading.Tasks;
using Flow.Launcher.Infrastructure.DialogJump.Models;
using Flow.Launcher.Infrastructure.Logger;

namespace Flow.Launcher.Infrastructure
{
    /// <summary>
    /// Remembers the File Explorer window that was in the foreground when Flow was shown,
    /// so results can be scoped to its folder and folders can be opened in that same window.
    /// </summary>
    public static class LaunchExplorer
    {
        private static readonly string ClassName = nameof(LaunchExplorer);

        private const string ExplorerWindowClass = "CabinetWClass";

        private static readonly object _lock = new();

        private static WindowsExplorerWindow _window;

        private static Task<string> _folderPathTask = Task.FromResult<string>(null);

        /// <summary>
        /// Captures <paramref name="hwnd"/> if it is a File Explorer window. Call before Flow takes the foreground.
        /// </summary>
        public static void Capture(nint hwnd)
        {
            lock (_lock)
            {
                if (hwnd == nint.Zero || Win32Helper.GetWindowClassName(hwnd) != ExplorerWindowClass)
                {
                    _window = null;
                    _folderPathTask = Task.FromResult<string>(null);
                    return;
                }

                var window = new WindowsExplorerWindow(hwnd);
                _window = window;
                // Reading the location goes through COM and can take a while, so do not block showing Flow
                _folderPathTask = Task.Run(() => ResolveFolderPath(window));
            }
        }

        public static bool IsCaptured
        {
            get
            {
                lock (_lock)
                {
                    return _window != null;
                }
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _window = null;
                _folderPathTask = Task.FromResult<string>(null);
            }
        }

        /// <summary>
        /// The file system folder shown by the captured window, or null if none was captured
        /// or it shows a virtual location such as This PC.
        /// </summary>
        public static Task<string> GetFolderPathAsync()
        {
            lock (_lock)
            {
                return _folderPathTask;
            }
        }

        /// <summary>
        /// Navigates the captured window to <paramref name="folderPath"/> and brings it to the foreground.
        /// </summary>
        /// <returns>False if no window was captured or it could not be navigated.</returns>
        public static bool TryNavigate(string folderPath, string selectPath = null)
        {
            WindowsExplorerWindow window;
            lock (_lock)
            {
                window = _window;
            }

            if (window == null || !Directory.Exists(folderPath)) return false;

            try
            {
                if (!window.Navigate(folderPath, selectPath)) return false;

                Win32Helper.SetForegroundWindow(window.Handle);
                return true;
            }
            catch (System.Exception e)
            {
                Log.Exception(ClassName, $"Failed to navigate explorer window to <{folderPath}>", e);
                return false;
            }
        }

        private static string ResolveFolderPath(WindowsExplorerWindow window)
        {
            try
            {
                var location = window.GetExplorerPath();
                if (string.IsNullOrEmpty(location) || location.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                    return null;

                var path = location.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(location).LocalPath
                    : location;

                return Directory.Exists(path) ? path : null;
            }
            catch (System.Exception e)
            {
                Log.Exception(ClassName, "Failed to read explorer window location", e);
                return null;
            }
        }
    }
}
