using System.Windows;
using System.Windows.Threading;
using InstantTranslate.Hooks;
using InstantTranslate.Models;
using InstantTranslate.Selection;
using InstantTranslate.Services;
using InstantTranslate.Settings;
using InstantTranslate.Translation;
using InstantTranslate.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace InstantTranslate;

public partial class App : System.Windows.Application
{
    private readonly SettingsStore _settingsStore = new(new WindowsCredentialStore());
    private volatile AppSettings _settings = AppSettings.Default;
    private GlobalMouseHook? _mouseHook;
    private SelectionTranslationCoordinator? _coordinator;
    private TranslationProviderFactory? _translationProviderFactory;
    private PopupManager? _popupManager;
    private TrayIconManager? _trayIcon;
    private DispatcherTimer? _smokeTestTimer;
    private bool _isExiting;
    private int _exitCode;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _settings = _settingsStore.Load();
        ThemeManager.Apply(_settings);

        try
        {
            _popupManager = new PopupManager();
            _mouseHook = new GlobalMouseHook();
            _translationProviderFactory = new TranslationProviderFactory();
            _coordinator = new SelectionTranslationCoordinator(
                _mouseHook,
                new UiaSelectionReader(),
                _translationProviderFactory,
                _popupManager,
                Dispatcher,
                () => _settings);
            _coordinator.TranslationFailed += OnTranslationFailed;

            _trayIcon = new TrayIconManager(_settings.IsEnabled);
            _trayIcon.SettingsRequested += ShowSettings;
            _trayIcon.EnabledChanged += SetEnabled;
            _trayIcon.ExitRequested += ExitApplication;

            _mouseHook.Start();

            var isPopupSmokeTest = e.Args.Contains("--popup-smoke-test", StringComparer.OrdinalIgnoreCase);
            var isSmokeTest = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
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
            WpfMessageBox.Show(
                $"InstantTranslate 无法启动：{exception.Message}",
                "启动失败",
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

    protected override void OnExit(ExitEventArgs e)
    {
        DisposeServices();
        base.OnExit(e);
    }

    private void ShowSettings()
    {
        Dispatcher.Invoke(() =>
        {
            var settingsWindow = new SettingsWindow(_settings);
            if (settingsWindow.ShowDialog() != true || settingsWindow.ResultSettings is null)
            {
                return;
            }

            try
            {
                _settingsStore.Save(settingsWindow.ResultSettings);
                _settings = settingsWindow.ResultSettings;
                ThemeManager.Apply(_settings);
                _trayIcon?.SetEnabled(_settings.IsEnabled);
            }
            catch (Exception exception)
            {
                WpfMessageBox.Show(
                    settingsWindow,
                    $"设置保存失败：{exception.Message}",
                    "InstantTranslate",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        });
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
                _trayIcon?.SetEnabled(_settings.IsEnabled);
                WpfMessageBox.Show(
                    $"无法保存启用状态：{exception.Message}",
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

        if (_trayIcon is not null)
        {
            _trayIcon.SettingsRequested -= ShowSettings;
            _trayIcon.EnabledChanged -= SetEnabled;
            _trayIcon.ExitRequested -= ExitApplication;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _popupManager?.Dispose();
        _popupManager = null;
    }
}
