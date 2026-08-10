using System.Windows;
using InstantTranslate.Settings;
using WpfMessageBox = System.Windows.MessageBox;

namespace InstantTranslate.Windows;

internal partial class SettingsWindow : Window
{
    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();

        EnabledCheckBox.IsChecked = settings.IsEnabled;
        SelectionDelayTextBox.Text = settings.SelectionDelayMilliseconds.ToString();
        SetComboText(SourceLanguageComboBox, settings.SourceLanguage);
        SetComboText(
            TargetLanguageComboBox,
            settings.TargetLanguageMode == "auto" ? "自动判断" : settings.TargetLanguage);
        SelectTheme(settings.ColorTheme);
        CustomAccentColorTextBox.Text = settings.CustomAccentColor;
        SelectProvider(settings.ProviderId);
        EndpointTextBox.Text = settings.DeepSeekEndpoint;
        SetComboText(ModelComboBox, settings.DeepSeekModel);
        ApiKeyPasswordBox.Password = settings.DeepSeekApiKey;
        UpdateProviderFields();
        UpdateThemePreview();
    }

    public AppSettings? ResultSettings { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SelectionDelayTextBox.Text, out var delay) || delay is < 0 or > 1000)
        {
            WpfMessageBox.Show(this, "选词等待时间必须是 0–1000 毫秒之间的整数。", "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            SelectionDelayTextBox.Focus();
            return;
        }

        var sourceLanguage = ReadComboText(SourceLanguageComboBox);
        var targetSelection = ReadComboText(TargetLanguageComboBox);
        if (string.IsNullOrWhiteSpace(sourceLanguage) || string.IsNullOrWhiteSpace(targetSelection))
        {
            WpfMessageBox.Show(this, "源语言和目标语言不能为空。", "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var providerId = ProviderComboBox.SelectedValue as string ?? "deepseek";
        var colorTheme = ColorThemeComboBox.SelectedValue as string ?? ThemeCatalog.DefaultThemeId;
        var customAccentColor = CustomAccentColorTextBox.Text.Trim();
        if (colorTheme == ThemeCatalog.CustomThemeId
            && !ThemeCatalog.TryNormalizeHexColor(customAccentColor, out customAccentColor))
        {
            WpfMessageBox.Show(this, "自定义颜色必须是 #RRGGBB 格式，例如 #2563EB。", "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            CustomAccentColorTextBox.Focus();
            return;
        }

        var endpointText = EndpointTextBox.Text.Trim();
        var model = ReadComboText(ModelComboBox);
        var apiKey = ApiKeyPasswordBox.Password.Trim();
        if (providerId == "deepseek")
        {
            if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint)
                || endpoint.Scheme is not ("http" or "https"))
            {
                WpfMessageBox.Show(this, "DeepSeek API Endpoint 必须是有效的 http 或 https 地址。", "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                EndpointTextBox.Focus();
                return;
            }

            endpointText = endpoint.ToString().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(model))
            {
                WpfMessageBox.Show(this, "DeepSeek Model 不能为空。", "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                ModelComboBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                WpfMessageBox.Show(this, "请选择 DeepSeek Provider 后填写 API Key。", "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                ApiKeyPasswordBox.Focus();
                return;
            }
        }

        ResultSettings = new AppSettings
        {
            IsEnabled = EnabledCheckBox.IsChecked == true,
            SelectionDelayMilliseconds = delay,
            SourceLanguage = sourceLanguage,
            TargetLanguageMode = targetSelection == "自动判断" ? "auto" : "fixed",
            TargetLanguage = targetSelection == "自动判断" ? "简体中文" : targetSelection,
            ColorTheme = colorTheme,
            CustomAccentColor = customAccentColor,
            ProviderId = providerId,
            DeepSeekEndpoint = endpointText,
            DeepSeekModel = model,
            DeepSeekApiKey = apiKey,
        };

        DialogResult = true;
    }

    private static string ReadComboText(System.Windows.Controls.ComboBox comboBox)
    {
        return comboBox.Text.Trim();
    }

    private static void SetComboText(System.Windows.Controls.ComboBox comboBox, string value)
    {
        comboBox.Text = value;
    }

    private void ProviderComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsInitialized)
        {
            UpdateProviderFields();
        }
    }

    private void SelectProvider(string providerId)
    {
        foreach (var item in ProviderComboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, providerId, StringComparison.OrdinalIgnoreCase))
            {
                ProviderComboBox.SelectedItem = item;
                return;
            }
        }

        ProviderComboBox.SelectedIndex = 0;
    }

    private void SelectTheme(string themeId)
    {
        foreach (var item in ColorThemeComboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, themeId, StringComparison.OrdinalIgnoreCase))
            {
                ColorThemeComboBox.SelectedItem = item;
                return;
            }
        }

        ColorThemeComboBox.SelectedIndex = 0;
    }

    private void ColorThemeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsInitialized)
        {
            UpdateThemePreview();
        }
    }

    private void CustomAccentColorTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            UpdateThemePreview();
        }
    }

    private void UpdateThemePreview()
    {
        var themeId = ColorThemeComboBox.SelectedValue as string ?? ThemeCatalog.DefaultThemeId;
        CustomAccentColorTextBox.IsEnabled = themeId == ThemeCatalog.CustomThemeId;
        var palette = ThemeCatalog.Resolve(themeId, CustomAccentColorTextBox.Text);
        ThemePreviewBorder.Background = ThemeManager.CreateBrush(palette.Accent);
    }

    private void UpdateProviderFields()
    {
        var isDeepSeek = (ProviderComboBox.SelectedValue as string) == "deepseek";
        EndpointTextBox.IsEnabled = isDeepSeek;
        ModelComboBox.IsEnabled = isDeepSeek;
        ApiKeyPasswordBox.IsEnabled = isDeepSeek;
    }
}
