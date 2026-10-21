using System;
using System.Windows;
using System.Windows.Threading;
using ChefKeys;
using CommunityToolkit.Mvvm.DependencyInjection;
using Flow.Launcher.Infrastructure.Hotkey;
using Flow.Launcher.Infrastructure.DialogJump;
using Flow.Launcher.Infrastructure.UserSettings;
using Flow.Launcher.ViewModel;
using Microsoft.Win32;
using NHotkey;
using NHotkey.Wpf;

namespace Flow.Launcher.Helper;

internal static class HotKeyMapper
{
    private static readonly string ClassName = nameof(HotKeyMapper);

    private static Settings _settings;
    private static MainViewModel _mainViewModel;

    internal static void Initialize()
    {
        _mainViewModel = Ioc.Default.GetRequiredService<MainViewModel>();
        _settings = Ioc.Default.GetService<Settings>();

        RegisterToggleHotkey("startup");
        if (_settings.EnableDialogJump)
        {
            SetHotkey(_settings.DialogJumpHotkey, DialogJump.OnToggleHotkey);
        }
        LoadCustomPluginHotkey();

        // Take the hotkey again after sleep, lock and remote sessions, where Windows or another program may have taken it
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == PowerModes.Resume)
                RefreshToggleHotkey($"power {e.Mode}");
        };
        SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon
                or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
                RefreshToggleHotkey($"session {e.Reason}");
        };
    }

    #region Toggle hotkey guard

    private static readonly TimeSpan ToggleHotkeyRetryInterval = TimeSpan.FromSeconds(5);
    private static DispatcherTimer _toggleHotkeyRetryTimer;
    private static bool _toggleHotkeyFailureShown;

    private static void RefreshToggleHotkey(string reason)
    {
        // SystemEvents are raised on their own thread, the hotkey window belongs to the UI thread
        Application.Current?.Dispatcher.InvokeAsync(() => RegisterToggleHotkey(reason));
    }

    /// <summary>
    /// Registers the toggle hotkey. When another program holds it, keeps retrying quietly instead of giving up,
    /// so Flow takes it back as soon as it is released.
    /// </summary>
    private static void RegisterToggleHotkey(string reason)
    {
        var hotkey = new HotkeyModel(_settings.Hotkey);
        var hotkeyStr = hotkey.ToString();
        if (string.IsNullOrEmpty(hotkeyStr))
            return;

        if (hotkeyStr is "LWin" or "RWin")
        {
            // A keyboard hook, not a registration another program can take
            if (reason == "startup")
                SetWithChefKeys(hotkeyStr);
            return;
        }

        try
        {
            HotkeyManager.Current.AddOrReplace(hotkeyStr, hotkey.CharKey, hotkey.ModifierKeys, OnToggleHotkey);
            HotkeyDiagnostics.Log($"toggle hotkey {hotkeyStr} registered ({reason})");
            _toggleHotkeyRetryTimer?.Stop();
            _toggleHotkeyFailureShown = false;
        }
        catch (Exception e)
        {
            HotkeyDiagnostics.Log($"toggle hotkey {hotkeyStr} FAILED ({reason}): {e.Message}");
            App.API.LogError(ClassName, $"Failed to register toggle hotkey {hotkeyStr} ({reason}): {e.Message}");

            if (!_toggleHotkeyFailureShown)
            {
                _toggleHotkeyFailureShown = true;
                App.API.ShowMsg(Localize.registerHotkeyFailed(hotkeyStr), Localize.hotkeyRetrying());
            }

            if (_toggleHotkeyRetryTimer == null)
            {
                _toggleHotkeyRetryTimer = new DispatcherTimer { Interval = ToggleHotkeyRetryInterval };
                _toggleHotkeyRetryTimer.Tick += (_, _) => RegisterToggleHotkey("retry");
            }
            _toggleHotkeyRetryTimer.Start();
        }
    }

    #endregion

    internal static void OnToggleHotkey(object sender, HotkeyEventArgs args)
    {
        HandleToggleHotkey("RegisterHotKey");
    }

    internal static void OnToggleHotkeyWithChefKeys()
    {
        HandleToggleHotkey("ChefKeys");
    }

    private static void HandleToggleHotkey(string source)
    {
        if (_mainViewModel.ShouldIgnoreHotkeys())
        {
            HotkeyDiagnostics.Log($"hotkey ({source}) IGNORED: fullscreen={_settings.IgnoreHotkeysOnFullscreen} gameMode={_mainViewModel.GameModeStatus}");
            return;
        }

        HotkeyDiagnostics.Log($"hotkey ({source}) received");
        _mainViewModel.ToggleFlowLauncher();
    }

    private static void SetHotkey(string hotkeyStr, EventHandler<HotkeyEventArgs> action)
    {
        var hotkey = new HotkeyModel(hotkeyStr);
        SetHotkey(hotkey, action);
    }

    private static void SetWithChefKeys(string hotkeyStr)
    {
        try
        {
            ChefKeysManager.RegisterHotkey(hotkeyStr, hotkeyStr, OnToggleHotkeyWithChefKeys);
            ChefKeysManager.Start();
        }
        catch (Exception e)
        {
            App.API.LogError(ClassName,
                string.Format("|HotkeyMapper.SetWithChefKeys|Error registering hotkey: {0} \nStackTrace:{1}",
                              e.Message,
                              e.StackTrace));
            string errorMsg = Localize.registerHotkeyFailed(hotkeyStr);
            string errorMsgTitle = Localize.MessageBoxTitle();
            App.API.ShowMsgBox(errorMsg, errorMsgTitle);
        }
    }

    internal static void SetHotkey(HotkeyModel hotkey, EventHandler<HotkeyEventArgs> action)
    {
        string hotkeyStr = hotkey.ToString();
        try
        {
            if (hotkeyStr == "LWin" || hotkeyStr == "RWin")
            {
                SetWithChefKeys(hotkeyStr);
                return;
            }

            HotkeyManager.Current.AddOrReplace(hotkeyStr, hotkey.CharKey, hotkey.ModifierKeys, action);
        }
        catch (Exception e)
        {
            App.API.LogError(ClassName,
                string.Format("|HotkeyMapper.SetHotkey|Error registering hotkey {2}: {0} \nStackTrace:{1}",
                              e.Message,
                              e.StackTrace,
                              hotkeyStr));
            string errorMsg = Localize.registerHotkeyFailed(hotkeyStr);
            string errorMsgTitle = Localize.MessageBoxTitle();
            App.API.ShowMsgBox(errorMsg, errorMsgTitle);
        }
    }

    internal static void RemoveHotkey(string hotkeyStr)
    {
        try
        {
            if (hotkeyStr == "LWin" || hotkeyStr == "RWin")
            {
                RemoveWithChefKeys(hotkeyStr);
                return;
            }

            if (!string.IsNullOrEmpty(hotkeyStr))
                HotkeyManager.Current.Remove(hotkeyStr);
        }
        catch (Exception e)
        {
            App.API.LogError(ClassName,
                string.Format("|HotkeyMapper.RemoveHotkey|Error removing hotkey: {0} \nStackTrace:{1}",
                              e.Message,
                              e.StackTrace));
            string errorMsg = Localize.unregisterHotkeyFailed(hotkeyStr);
            string errorMsgTitle = Localize.MessageBoxTitle();
            App.API.ShowMsgBox(errorMsg, errorMsgTitle);
        }
    }

    private static void RemoveWithChefKeys(string hotkeyStr)
    {
        ChefKeysManager.UnregisterHotkey(hotkeyStr);
        ChefKeysManager.Stop();
    }

    internal static void LoadCustomPluginHotkey()
    {
        if (_settings.CustomPluginHotkeys == null)
            return;

        foreach (CustomPluginHotkey hotkey in _settings.CustomPluginHotkeys)
        {
            SetCustomQueryHotkey(hotkey);
        }
    }

    internal static void SetCustomQueryHotkey(CustomPluginHotkey hotkey)
    {
        SetHotkey(hotkey.Hotkey, (s, e) =>
        {
            if (_mainViewModel.ShouldIgnoreHotkeys())
                return;

            App.API.ShowMainWindow();
            // Make sure to go back to the query results page first since it can cause issues if current page is context menu
            App.API.BackToQueryResults();
            App.API.ChangeQuery(hotkey.ActionKeyword, true);
        });
    }

    internal static bool CheckAvailability(HotkeyModel currentHotkey)
    {
        try
        {
            HotkeyManager.Current.AddOrReplace("HotkeyAvailabilityTest", currentHotkey.CharKey, currentHotkey.ModifierKeys, (sender, e) => { });

            return true;
        }
        catch
        {
        }
        finally
        {
            HotkeyManager.Current.Remove("HotkeyAvailabilityTest");
        }

        return false;
    }
}
