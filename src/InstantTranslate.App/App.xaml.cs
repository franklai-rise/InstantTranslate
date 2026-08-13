using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using InstantTranslate.Hooks;
using InstantTranslate.Interop;
using InstantTranslate.Models;
using InstantTranslate.Selection;
using InstantTranslate.Services;
using InstantTranslate.Settings;
using InstantTranslate.Translation;
using InstantTranslate.Windows;
using WpfMessageBox = System.Windows.MessageBox;
using WpfClipboard = System.Windows.Clipboard;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace InstantTranslate;

public partial class App : System.Windows.Application
{
    private readonly SettingsStore _settingsStore = new(new WindowsCredentialStore());
    private readonly StartupRegistration _startupRegistration = new();
    private volatile AppSettings _settings = AppSettings.Default;
    private GlobalMouseHook? _mouseHook;
    private GlobalHotkeyManager? _hotkeyManager;
    private SelectionTranslationCoordinator? _coordinator;
    private TranslationProviderFactory? _translationProviderFactory;
    private PopupManager? _popupManager;
    private TrayIconManager? _trayIcon;
    private SingleInstanceCoordinator? _singleInstance;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _smokeTestTimer;
    private bool _isExiting;
    private int _exitCode;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var isPopupSmokeTest = e.Args.Contains("--popup-smoke-test", StringComparer.OrdinalIgnoreCase);
        var isSmokeTest = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        var isPopupSnapshotTest = e.Args.Contains("--popup-snapshot-test", StringComparer.OrdinalIgnoreCase);
        var isSettingsSnapshotTest = e.Args.Contains("--settings-snapshot-test", StringComparer.OrdinalIgnoreCase);
        var isVisualTest = isPopupSmokeTest || isSmokeTest || isPopupSnapshotTest || isSettingsSnapshotTest;
        if (!isVisualTest)
        {
            _singleInstance = SingleInstanceCoordinator.Acquire();
            if (!_singleInstance.IsPrimary)
            {
                _singleInstance.Dispose();
                _singleInstance = null;
                Shutdown(0);
                return;
            }
        }

        _settings = _settingsStore.Load();
        ThemeManager.Apply(_settings);

        // Visual review modes render only our own WPF windows. They deliberately
        // skip global hooks, hotkeys, tray integration, and startup registration.
        if (isPopupSnapshotTest)
        {
            _popupManager = new PopupManager(
                () => new PopupAppearanceSettings(
                    16.5,
                    TranslationFontCatalog.DefaultEnglishFontFamily,
                    TranslationFontCatalog.DefaultChineseFontFamily,
                    UiLanguageCatalog.EnglishLanguageId));
            _ = RunPopupSnapshotTestAsync();
            return;
        }

        if (isSettingsSnapshotTest)
        {
            _ = RunSettingsSnapshotTestAsync();
            return;
        }

        try
        {
            _popupManager = CreatePopupManager();
            _mouseHook = new GlobalMouseHook();
            _translationProviderFactory = new TranslationProviderFactory();
            _coordinator = new SelectionTranslationCoordinator(
                _mouseHook,
                new SelectionReaderPipeline(
                    new UiaSelectionReader(),
                    new NativeSelectionReader(),
                    new ClipboardSelectionReader(Dispatcher, WindowProcessResolver.IsClipboardFallbackAllowedAt),
                    () => _settings.UseClipboardFallback),
                _translationProviderFactory,
                _popupManager,
                Dispatcher,
                () => _settings);
            _coordinator.TranslationFailed += OnTranslationFailed;

            _trayIcon = new TrayIconManager(_settings.IsEnabled, _settings.UiLanguage);
            _trayIcon.SettingsRequested += ShowSettings;
            _trayIcon.EnabledChanged += SetEnabled;
            _trayIcon.TranslateClipboardRequested += TranslateClipboard;
            _trayIcon.AboutRequested += ShowAbout;
            _trayIcon.ExitRequested += ExitApplication;

            _hotkeyManager = new GlobalHotkeyManager();
            _hotkeyManager.TranslateClipboardRequested += TranslateClipboard;
            try
            {
                _hotkeyManager.Start();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"InstantTranslate hotkey registration failed: {exception}");
                _hotkeyManager.TranslateClipboardRequested -= TranslateClipboard;
                _hotkeyManager.Dispose();
                _hotkeyManager = null;
                _trayIcon.ShowError(L(
                    "Could not register Ctrl+Shift+T. Another app may already use it.",
                    "无法注册 Ctrl+Shift+T，可能已被其他程序占用。"));
            }

            if (!isVisualTest)
            {
                TryApplyStartupSetting();
                _singleInstance?.StartListening(
                    () => Dispatcher.BeginInvoke(ShowSettings, DispatcherPriority.Send));
            }

            _mouseHook.Start();

            if (isPopupSmokeTest)
            {
                _ = RunPopupSmokeTestAsync();
            }
            else if (isSmokeTest)
            {
                _smokeTestTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(750),
                };
                _smokeTestTimer.Tick += SmokeTestTimer_Tick;
                _smokeTestTimer.Start();
            }
            else if (_settings.ProviderId == "deepseek" && string.IsNullOrWhiteSpace(_settings.DeepSeekApiKey))
            {
                Dispatcher.BeginInvoke(ShowSettings, DispatcherPriority.ApplicationIdle);
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"InstantTranslate startup failed: {exception}");
            WpfMessageBox.Show(
                L(
                    "InstantTranslate could not start. Close other instances and try again.",
                    "InstantTranslate 无法启动，请关闭其他实例后重试。"),
                L("Startup failed", "启动失败"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _exitCode = 1;
            ExitApplication();
        }
    }

    private void OnTranslationFailed(string message)
    {
        Dispatcher.BeginInvoke(() => _trayIcon?.ShowError(message));
    }

    private async Task RunPopupSmokeTestAsync()
    {
        try
        {
            var anchor = new ScreenPoint(160, 140);
            _popupManager?.ShowLoading(long.MaxValue, anchor);
            await Task.Delay(250);
            _popupManager?.ShowTranslation(
                long.MaxValue,
                "这是用于界面验证的中文原文。",
                "This is Chinese source text used for UI verification.",
                LanguageDirectionResolver.English,
                anchor);
            await Task.Delay(900);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"InstantTranslate popup smoke test failed: {exception}");
            _exitCode = 1;
        }
        finally
        {
            ExitApplication();
        }
    }

    private async Task RunPopupSnapshotTestAsync()
    {
        const long requestId = long.MaxValue - 1;
        try
        {
            var anchor = new ScreenPoint(220, 170);
            _popupManager?.ShowTranslation(
                requestId,
                "简约不是减少功能，而是让每一次操作都更直接。",
                "Simplicity is not about removing capability; it is about making every action feel direct and effortless.",
                LanguageDirectionResolver.English,
                anchor);
            await Task.Delay(350);
            var window = _popupManager?.GetWindowForVisualTest(requestId)
                ?? throw new InvalidOperationException("无法创建浮窗预览。");
            VisualSnapshotRenderer.SavePng(window, GetSnapshotPath("popup"));

            window.ApplyUiLanguage(UiLanguageCatalog.SimplifiedChineseLanguageId);
            await Task.Delay(100);
            VisualSnapshotRenderer.SavePng(window, GetSnapshotPath("popup-zh"));
            window.ApplyUiLanguage(UiLanguageCatalog.EnglishLanguageId);

            window.SelectTextForVisualTest(0, 34);
            await Task.Delay(120);
            if (!window.HasActiveSelectionForVisualTest())
            {
                throw new InvalidOperationException("浮窗正文未进入正常文字选择状态。");
            }

            VisualSnapshotRenderer.SavePng(window, GetSnapshotPath("popup-selection"));

            const string longTranslation =
                "A restrained interface should remain comfortable when the content grows. "
                + "The translation area now uses the available viewport instead of expanding beyond the window. "
                + "Large type stays readable, while a vertical scrollbar keeps every paragraph accessible.\n\n"
                + "简洁不等于功能受限。固定窗口尺寸后，正文会在剩余空间中自然排版；当大字号和长文本超出可视区域时，滚动条会出现，边缘拖动也会继续调整可读空间。";
            _popupManager.ShowTranslation(
                requestId,
                "用于验证长文本、大字号和窗口缩放。",
                longTranslation,
                LanguageDirectionResolver.Chinese,
                anchor);
            window.ConfigureViewportForVisualTest(width: 560, height: 260, fontSize: 30);
            await Task.Delay(220);
            if (!window.HasVerticalOverflowForVisualTest())
            {
                throw new InvalidOperationException("长文本预览未形成可滚动区域。");
            }

            VisualSnapshotRenderer.SavePng(window, GetSnapshotPath("popup-overflow"));
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"InstantTranslate popup snapshot failed: {exception}");
            _exitCode = 1;
        }
        finally
        {
            ExitApplication();
        }
    }

    private async Task RunSettingsSnapshotTestAsync()
    {
        SettingsWindow? window = null;
        try
        {
            window = new SettingsWindow(AppSettings.Default with
            {
                UiLanguage = UiLanguageCatalog.EnglishLanguageId,
                ProviderId = "mock",
                DeepSeekApiKey = string.Empty,
            });
            window.Show();
            await Task.Delay(350);
            var visual = window.Content as FrameworkElement ?? window;
            VisualSnapshotRenderer.SavePng(visual, GetSnapshotPath("settings"));
            VisualSnapshotRenderer.SavePng(visual, GetSnapshotPath("settings-en"));
            if (window.FindName("SettingsScrollViewer") is System.Windows.Controls.ScrollViewer englishScrollViewer)
            {
                englishScrollViewer.ScrollToVerticalOffset(330);
                await Task.Delay(100);
                VisualSnapshotRenderer.SavePng(visual, GetSnapshotPath("settings-fonts-en"));
            }

            window.Close();
            window = new SettingsWindow(AppSettings.Default with
            {
                UiLanguage = UiLanguageCatalog.SimplifiedChineseLanguageId,
                ProviderId = "mock",
                DeepSeekApiKey = string.Empty,
            });
            window.Show();
            await Task.Delay(250);
            visual = window.Content as FrameworkElement ?? window;
            VisualSnapshotRenderer.SavePng(visual, GetSnapshotPath("settings-zh"));
            if (window.FindName("SettingsScrollViewer") is System.Windows.Controls.ScrollViewer chineseScrollViewer)
            {
                chineseScrollViewer.ScrollToVerticalOffset(330);
                await Task.Delay(100);
                VisualSnapshotRenderer.SavePng(visual, GetSnapshotPath("settings-fonts-zh"));
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"InstantTranslate settings snapshot failed: {exception}");
            _exitCode = 1;
        }
        finally
        {
            window?.Close();
            ExitApplication();
        }
    }

    private static string GetSnapshotPath(string name)
    {
        return System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"InstantTranslate-{name}-preview.png");
    }

    private PopupManager CreatePopupManager()
    {
        return new PopupManager(
            () => new PopupAppearanceSettings(
                _settings.DefaultTranslationFontSize,
                _settings.EnglishTranslationFontFamily,
                _settings.ChineseTranslationFontFamily,
                _settings.UiLanguage));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DisposeServices();
        base.OnExit(e);
    }

    private void ShowSettings()
    {
        Dispatcher.Invoke(() =>
        {
            if (_settingsWindow is not null)
            {
                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }

                _settingsWindow.Activate();
                return;
            }

            var settingsWindow = new SettingsWindow(_settings);
            _settingsWindow = settingsWindow;
            bool? result;
            try
            {
                result = settingsWindow.ShowDialog();
            }
            finally
            {
                _settingsWindow = null;
            }

            if (result != true || settingsWindow.ResultSettings is null)
            {
                return;
            }

            try
            {
                _startupRegistration.SetEnabled(settingsWindow.ResultSettings.StartWithWindows);
                _settingsStore.Save(settingsWindow.ResultSettings);
                _settings = settingsWindow.ResultSettings;
                ThemeManager.Apply(_settings);
                _popupManager?.ApplyAppearanceToOpenWindows();
                _trayIcon?.ApplyUiLanguage(_settings.UiLanguage);
                _trayIcon?.SetEnabled(_settings.IsEnabled);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"InstantTranslate settings save failed: {exception}");
                try
                {
                    _startupRegistration.SetEnabled(_settings.StartWithWindows);
                    _settingsStore.Save(_settings, persistApiKey: false);
                }
                catch (Exception rollbackException)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"InstantTranslate settings rollback failed: {rollbackException}");
                }

                WpfMessageBox.Show(
                    settingsWindow,
                    L(
                        "Could not save settings. Check file permissions and try again.",
                        "设置保存失败，请检查文件权限后重试。"),
                    "InstantTranslate",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        });
    }

    private void TranslateClipboard()
    {
        Dispatcher.BeginInvoke(
            () =>
            {
                try
                {
                    var text = WpfClipboard.ContainsText(WpfTextDataFormat.UnicodeText)
                        ? TextNormalizer.Normalize(WpfClipboard.GetText(WpfTextDataFormat.UnicodeText))
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        _trayIcon?.ShowInfo(
                            "InstantTranslate",
                            L("The clipboard does not contain text to translate.", "剪贴板中没有可翻译的文字。"));
                        return;
                    }

                    var anchor = NativeMethods.GetCursorPos(out var point)
                        ? point.ToScreenPoint()
                        : new ScreenPoint(160, 140);
                    _coordinator?.TranslateText(text, anchor);
                }
                catch (ExternalException)
                {
                    _trayIcon?.ShowError(L(
                        "The clipboard is temporarily unavailable. Copy the text and try again.",
                        "暂时无法读取剪贴板，请复制文字后重试。"));
                }
            },
            DispatcherPriority.Send);
    }

    private void ShowAbout()
    {
        Dispatcher.BeginInvoke(
            () =>
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                WpfMessageBox.Show(
                    $"InstantTranslate {version?.Major ?? 0}.{version?.Minor ?? 0}.{version?.Build ?? 0}\n\n"
                    + L(
                        "Translations are powered by DeepSeek. Source text and translations are not saved by default; repeated-translation cache stays in memory and is cleared on exit.",
                        "由 DeepSeek 提供翻译。默认不保存原文和译文；重复翻译缓存仅存在于内存，退出后即清空。"),
                    L("About InstantTranslate", "关于 InstantTranslate"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });
    }

    private void TryApplyStartupSetting()
    {
        try
        {
            _startupRegistration.SetEnabled(_settings.StartWithWindows);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"InstantTranslate startup registration failed: {exception}");
            _trayIcon?.ShowError(L(
                "Could not update the startup setting.",
                "无法更新开机启动设置。"));
        }
    }

    private void SetEnabled(bool isEnabled)
    {
        Dispatcher.Invoke(() =>
        {
            var updated = _settings with { IsEnabled = isEnabled };
            try
            {
                _settingsStore.Save(updated, persistApiKey: false);
                _settings = updated;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"InstantTranslate enabled-state save failed: {exception}");
                _trayIcon?.SetEnabled(_settings.IsEnabled);
                WpfMessageBox.Show(
                    L(
                        "Could not save the enabled state. Try again.",
                        "无法保存启用状态，请重试。"),
                    "InstantTranslate",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        });
    }

    private void ExitApplication()
    {
        Dispatcher.Invoke(() =>
        {
            if (_isExiting)
            {
                return;
            }

            _isExiting = true;
            DisposeServices();
            Shutdown(_exitCode);
        });
    }

    private void SmokeTestTimer_Tick(object? sender, EventArgs e)
    {
        if (_smokeTestTimer is not null)
        {
            _smokeTestTimer.Stop();
            _smokeTestTimer.Tick -= SmokeTestTimer_Tick;
            _smokeTestTimer = null;
        }

        ExitApplication();
    }

    private string L(string english, string chinese)
    {
        return _settings.UiLanguage == UiLanguageCatalog.SimplifiedChineseLanguageId
            ? chinese
            : english;
    }

    private void DisposeServices()
    {
        if (_smokeTestTimer is not null)
        {
            _smokeTestTimer.Stop();
            _smokeTestTimer.Tick -= SmokeTestTimer_Tick;
            _smokeTestTimer = null;
        }

        if (_coordinator is not null)
        {
            _coordinator.TranslationFailed -= OnTranslationFailed;
            _coordinator.Dispose();
            _coordinator = null;
        }

        _translationProviderFactory?.Dispose();
        _translationProviderFactory = null;

        _mouseHook?.Dispose();
        _mouseHook = null;

        if (_hotkeyManager is not null)
        {
            _hotkeyManager.TranslateClipboardRequested -= TranslateClipboard;
            _hotkeyManager.Dispose();
            _hotkeyManager = null;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.SettingsRequested -= ShowSettings;
            _trayIcon.EnabledChanged -= SetEnabled;
            _trayIcon.TranslateClipboardRequested -= TranslateClipboard;
            _trayIcon.AboutRequested -= ShowAbout;
            _trayIcon.ExitRequested -= ExitApplication;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _popupManager?.Dispose();
        _popupManager = null;

        _singleInstance?.Dispose();
        _singleInstance = null;
    }
}
