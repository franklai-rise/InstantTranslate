using System.Windows;
using InstantTranslate.Settings;
using InstantTranslate.Translation;
using WpfMessageBox = System.Windows.MessageBox;

namespace InstantTranslate.Windows;

internal partial class SettingsWindow : Window
{
    private static readonly IReadOnlyDictionary<string, (string English, string Chinese)> LocalizedText =
        new Dictionary<string, (string English, string Chinese)>(StringComparer.Ordinal)
        {
            ["WindowTitle"] = ("InstantTranslate Settings", "InstantTranslate 设置"),
            ["HeaderTitle"] = ("Settings", "设置"),
            ["HeaderSubtitle"] = ("Translation behavior, appearance, and connection", "调整翻译行为、外观与服务连接"),
            ["InterfaceLanguage"] = ("Interface language", "界面语言"),
            ["GeneralTitle"] = ("General", "常规"),
            ["GeneralDescription"] = ("Control availability and selection behavior.", "控制应用状态与划词触发范围。"),
            ["SelectionTranslation"] = ("Selection translation", "划词翻译"),
            ["SelectionTranslationDescription"] = ("Translate text automatically after you select it.", "选中文字后自动读取并显示译文。"),
            ["StartWithWindows"] = ("Start with Windows", "开机启动"),
            ["StartWithWindowsDescription"] = ("Stay ready in the background after sign-in.", "登录 Windows 后在后台就绪。"),
            ["ClipboardFallback"] = ("Compatibility clipboard fallback", "兼容性剪贴板回退"),
            ["ClipboardFallbackDescription"] = ("Only use this for apps that expose no accessibility text; it temporarily changes the system clipboard.", "仅用于不提供无障碍文本的应用；启用后读取选区时会临时改变系统剪贴板。"),
            ["SelectionDelay"] = ("Trigger delay · ms", "触发等待 · 毫秒"),
            ["MaximumSelection"] = ("Selection limit · characters", "最大选区 · 字符"),
            ["TranslationAppearanceTitle"] = ("Translation & appearance", "翻译与外观"),
            ["TranslationAppearanceDescription"] = ("Choose the default language direction and reading experience.", "设置默认语言方向与浮窗阅读体验。"),
            ["SourceLanguage"] = ("Source language", "源语言"),
            ["TargetLanguage"] = ("Target language", "目标语言"),
            ["UseSelectionContext"] = ("Use surrounding context", "使用周围语境"),
            ["UseSelectionContextDescription"] = ("Use the surrounding paragraph to resolve ambiguity. Extra text is sent only when this is enabled.", "使用选区所在段落帮助消除歧义；只有开启后才会发送周围文字。"),
            ["TranslationMode"] = ("Translation mode", "翻译模式"),
            ["TranslationTone"] = ("Writing style", "表达风格"),
            ["PersonalGlossary"] = ("Personal glossary", "个人术语库"),
            ["PersonalGlossaryDescription"] = ("One term per line: source => preferred translation. Stored only on this device.", "每行一个术语：原词 => 指定译法。内容仅保存在本机。"),
            ["TranslationMemory"] = ("Saved translation memory", "已保存的翻译记忆"),
            ["TranslationMemoryDescription"] = ("Only corrections you save from the popup are encrypted for this Windows account. Up to 3 relevant pairs may guide future requests.", "仅保存你在浮窗中主动修正的译文，并为当前 Windows 账户加密；最多 3 组相关示例可辅助后续请求。"),
            ["ClearTranslationMemory"] = ("Clear", "清除"),
            ["TranslationMemoryEmpty"] = ("No saved corrections", "暂无已保存的修正"),
            ["TranslationMemoryCount"] = ("{0} encrypted correction(s) for this Windows account", "已为当前 Windows 账户加密保存 {0} 条修正"),
            ["ClearTranslationMemoryTitle"] = ("Clear translation memory?", "清除翻译记忆？"),
            ["ClearTranslationMemoryMessage"] = ("This permanently removes every source and corrected translation you explicitly saved.", "这会永久删除你主动保存的全部原文和修正译文。"),
            ["ClearTranslationMemoryFailed"] = ("Could not clear translation memory.", "无法清除翻译记忆。"),
            ["EnglishTranslationFont"] = ("English translation font", "英文译文字体"),
            ["ChineseTranslationFont"] = ("Chinese translation font", "中文译文字体"),
            ["AccentColor"] = ("Accent color", "强调色"),
            ["CustomColor"] = ("Custom color", "自定义颜色"),
            ["DefaultTranslationFontSize"] = ("Default translation size", "默认译文字号"),
            ["TranslationServiceTitle"] = ("Translation service", "翻译服务"),
            ["TranslationServiceDescription"] = ("Your API key is stored only in Windows Credential Manager on this device.", "API Key 仅保存在本机 Windows 凭据管理器中。"),
            ["Provider"] = ("Provider", "服务"),
            ["Model"] = ("Model", "模型"),
            ["ApiEndpoint"] = ("API endpoint", "API Endpoint"),
            ["ApiKey"] = ("API key", "API Key"),
            ["TestConnection"] = ("Test connection", "测试连接"),
            ["ClearKey"] = ("Clear key", "清空密钥"),
            ["FooterHint"] = ("Changes take effect immediately after saving", "更改将在保存后立即生效"),
            ["Cancel"] = ("Cancel", "取消"),
            ["SaveSettings"] = ("Save settings", "保存设置"),
            ["InvalidSettings"] = ("Invalid settings", "设置无效"),
            ["DelayError"] = ("The selection delay must be an integer from 0 to 1000 milliseconds.", "选词等待时间必须是 0–1000 毫秒之间的整数。"),
            ["MaximumSelectionError"] = ("The selection limit must be an integer from 100 to 20,000 characters.", "最大选区字符数必须是 100–20000 之间的整数。"),
            ["LanguageEmptyError"] = ("Source and target languages cannot be empty.", "源语言和目标语言不能为空。"),
            ["CustomColorError"] = ("The custom color must use #RRGGBB format, for example #2563EB.", "自定义颜色必须是 #RRGGBB 格式，例如 #2563EB。"),
            ["EndpointError"] = ("A remote DeepSeek endpoint must use HTTPS. HTTP is allowed only for local addresses.", "DeepSeek 远程 Endpoint 必须使用 HTTPS；仅本机地址允许 HTTP。"),
            ["ModelEmptyError"] = ("The DeepSeek model cannot be empty.", "DeepSeek Model 不能为空。"),
            ["ApiKeyEmptyError"] = ("Enter an API key when using the DeepSeek provider.", "请选择 DeepSeek Provider 后填写 API Key。"),
            ["FillConfiguration"] = ("Complete the configuration first", "请先填写完整配置"),
            ["Connecting"] = ("Connecting…", "正在连接…"),
            ["ConnectionSuccess"] = ("✓ Connected", "✓ 连接成功"),
            ["ConnectionTimeout"] = ("Connection timed out", "连接超时"),
            ["DeleteAfterSave"] = ("Credential will be deleted after saving", "保存后删除凭据"),
            ["NoTestTranslation"] = ("The service returned no test translation.", "服务未返回测试译文。"),
            ["SelectionDelayTooltip"] = ("Prevents accidental triggers from window dragging or brief selections; range 0–1000", "避免拖动窗口或短暂选择造成误触；范围 0–1000"),
            ["MaximumSelectionTooltip"] = ("Prevents accidental translation of an entire document; range 100–20000", "用于防止误选整篇文档；范围 100–20000"),
            ["CustomColorTooltip"] = ("Enter #RRGGBB, for example #2563EB", "输入 #RRGGBB，例如 #2563EB"),
        };

    private string _uiLanguage = "en";
    private string? _connectionStatusLocalizationKey;
    private readonly Func<int>? _getTranslationMemoryCount;
    private readonly Action? _clearTranslationMemory;

    public SettingsWindow(
        AppSettings settings,
        Func<int>? getTranslationMemoryCount = null,
        Action? clearTranslationMemory = null)
    {
        _getTranslationMemoryCount = getTranslationMemoryCount;
        _clearTranslationMemory = clearTranslationMemory;
        InitializeComponent();

        EnabledCheckBox.IsChecked = settings.IsEnabled;
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        ClipboardFallbackCheckBox.IsChecked = settings.UseClipboardFallback;
        SelectionDelayTextBox.Text = settings.SelectionDelayMilliseconds.ToString();
        MaximumSelectionTextBox.Text = settings.MaximumSelectionCharacters.ToString();
        UseSelectionContextCheckBox.IsChecked = settings.UseSelectionContext;
        SetComboText(SourceLanguageComboBox, settings.SourceLanguage);
        SetComboText(
            TargetLanguageComboBox,
            settings.TargetLanguageMode == "auto" ? "自动判断" : settings.TargetLanguage);
        SetComboText(
            TranslationModeComboBox,
            TranslationPreferenceCatalog.NormalizeMode(settings.TranslationMode));
        SetComboText(
            TranslationToneComboBox,
            TranslationPreferenceCatalog.NormalizeTone(settings.TranslationTone));
        PersonalGlossaryTextBox.Text = settings.PersonalGlossary;
        SelectTheme(settings.ColorTheme);
        CustomAccentColorTextBox.Text = settings.CustomAccentColor;
        DefaultFontSizeSlider.Value = Math.Clamp(settings.DefaultTranslationFontSize, 12, 34);
        SetComboText(
            EnglishTranslationFontComboBox,
            TranslationFontCatalog.NormalizeEnglish(settings.EnglishTranslationFontFamily));
        SetComboText(
            ChineseTranslationFontComboBox,
            TranslationFontCatalog.NormalizeChinese(settings.ChineseTranslationFontFamily));
        SelectProvider(settings.ProviderId);
        EndpointTextBox.Text = settings.DeepSeekEndpoint;
        SetComboText(ModelComboBox, settings.DeepSeekModel);
        ApiKeyPasswordBox.Password = settings.DeepSeekApiKey;
        ApplyUiLanguage(settings.UiLanguage);
        UpdateProviderFields();
        UpdateThemePreview();
        UpdateTranslationMemoryStatus();
    }

    public AppSettings? ResultSettings { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SelectionDelayTextBox.Text, out var delay) || delay is < 0 or > 1000)
        {
            WpfMessageBox.Show(this, L("DelayError"), L("InvalidSettings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            SelectionDelayTextBox.Focus();
            return;
        }

        if (!int.TryParse(MaximumSelectionTextBox.Text, out var maximumSelectionCharacters)
            || maximumSelectionCharacters is < 100 or > 20000)
        {
            WpfMessageBox.Show(this, L("MaximumSelectionError"), L("InvalidSettings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            MaximumSelectionTextBox.Focus();
            return;
        }

        var sourceLanguage = ReadComboText(SourceLanguageComboBox);
        var targetSelection = ReadComboText(TargetLanguageComboBox);
        if (string.IsNullOrWhiteSpace(sourceLanguage) || string.IsNullOrWhiteSpace(targetSelection))
        {
            WpfMessageBox.Show(this, L("LanguageEmptyError"), L("InvalidSettings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var providerId = ProviderComboBox.SelectedValue as string ?? "deepseek";
        var colorTheme = ColorThemeComboBox.SelectedValue as string ?? ThemeCatalog.DefaultThemeId;
        var customAccentColor = CustomAccentColorTextBox.Text.Trim();
        if (colorTheme == ThemeCatalog.CustomThemeId
            && !ThemeCatalog.TryNormalizeHexColor(customAccentColor, out customAccentColor))
        {
            WpfMessageBox.Show(this, L("CustomColorError"), L("InvalidSettings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            CustomAccentColorTextBox.Focus();
            return;
        }

        var endpointText = EndpointTextBox.Text.Trim();
        var model = ReadComboText(ModelComboBox);
        var apiKey = ApiKeyPasswordBox.Password.Trim();
        if (providerId == "deepseek")
        {
            if (!TranslationProviderFactory.TryValidateEndpoint(endpointText, out var endpoint))
            {
                WpfMessageBox.Show(this, L("EndpointError"), L("InvalidSettings"), MessageBoxButton.OK, MessageBoxImage.Warning);
                EndpointTextBox.Focus();
                return;
            }

            endpointText = endpoint!.ToString().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(model))
            {
                WpfMessageBox.Show(this, L("ModelEmptyError"), L("InvalidSettings"), MessageBoxButton.OK, MessageBoxImage.Warning);
                ModelComboBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                WpfMessageBox.Show(this, L("ApiKeyEmptyError"), L("InvalidSettings"), MessageBoxButton.OK, MessageBoxImage.Warning);
                ApiKeyPasswordBox.Focus();
                return;
            }
        }

        ResultSettings = new AppSettings
        {
            IsEnabled = EnabledCheckBox.IsChecked == true,
            StartWithWindows = StartWithWindowsCheckBox.IsChecked == true,
            UseClipboardFallback = ClipboardFallbackCheckBox.IsChecked == true,
            SelectionDelayMilliseconds = delay,
            MaximumSelectionCharacters = maximumSelectionCharacters,
            SourceLanguage = sourceLanguage,
            TargetLanguageMode = targetSelection == "自动判断" ? "auto" : "fixed",
            TargetLanguage = targetSelection == "自动判断" ? "简体中文" : targetSelection,
            UseSelectionContext = UseSelectionContextCheckBox.IsChecked == true,
            TranslationMode = TranslationPreferenceCatalog.NormalizeMode(
                ReadComboText(TranslationModeComboBox)),
            TranslationTone = TranslationPreferenceCatalog.NormalizeTone(
                ReadComboText(TranslationToneComboBox)),
            PersonalGlossary = PersonalGlossaryTextBox.Text.Trim(),
            ColorTheme = colorTheme,
            CustomAccentColor = customAccentColor,
            DefaultTranslationFontSize = Math.Round(DefaultFontSizeSlider.Value, 1),
            UiLanguage = _uiLanguage,
            EnglishTranslationFontFamily = TranslationFontCatalog.NormalizeEnglish(ReadComboText(EnglishTranslationFontComboBox)),
            ChineseTranslationFontFamily = TranslationFontCatalog.NormalizeChinese(ReadComboText(ChineseTranslationFontComboBox)),
            ProviderId = providerId,
            DeepSeekEndpoint = endpointText,
            DeepSeekModel = model,
            DeepSeekApiKey = apiKey,
        };

        DialogResult = true;
    }

    private static string ReadComboText(System.Windows.Controls.ComboBox comboBox)
    {
        if (comboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item
            && item.Tag is string stableValue
            && !string.IsNullOrWhiteSpace(stableValue))
        {
            return stableValue.Trim();
        }

        return comboBox.Text.Trim();
    }

    private static void SetComboText(System.Windows.Controls.ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        if (comboBox.IsEditable)
        {
            comboBox.Text = value;
        }
        else
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private void UiLanguageButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyUiLanguage(
            _uiLanguage == UiLanguageCatalog.SimplifiedChineseLanguageId
                ? UiLanguageCatalog.EnglishLanguageId
                : UiLanguageCatalog.SimplifiedChineseLanguageId);
    }

    private void ApplyUiLanguage(string? language)
    {
        _uiLanguage = NormalizeUiLanguage(language);
        UiLanguageButton.Content = _uiLanguage == UiLanguageCatalog.SimplifiedChineseLanguageId
            ? "English"
            : "中文";
        Title = L("WindowTitle");
        ApplyLocalizedContent(this);

        System.Windows.Automation.AutomationProperties.SetName(
            EnabledCheckBox,
            L("SelectionTranslation"));
        System.Windows.Automation.AutomationProperties.SetName(
            StartWithWindowsCheckBox,
            L("StartWithWindows"));
        System.Windows.Automation.AutomationProperties.SetName(
            ClipboardFallbackCheckBox,
            L("ClipboardFallback"));
        System.Windows.Automation.AutomationProperties.SetName(
            UseSelectionContextCheckBox,
            L("UseSelectionContext"));
        System.Windows.Automation.AutomationProperties.SetName(
            PersonalGlossaryTextBox,
            L("PersonalGlossary"));
        System.Windows.Automation.AutomationProperties.SetName(
            ClearTranslationMemoryButton,
            L("ClearTranslationMemory"));

        SelectionDelayTextBox.ToolTip = L("SelectionDelayTooltip");
        MaximumSelectionTextBox.ToolTip = L("MaximumSelectionTooltip");
        CustomAccentColorTextBox.ToolTip = L("CustomColorTooltip");

        SetComboItemContent(SourceLanguageComboBox, "自动检测", _uiLanguage == "zh-CN" ? "自动检测" : "Auto detect");
        SetComboItemContent(SourceLanguageComboBox, "英语", _uiLanguage == "zh-CN" ? "英语" : "English");
        SetComboItemContent(SourceLanguageComboBox, "简体中文", _uiLanguage == "zh-CN" ? "简体中文" : "Simplified Chinese");

        SetComboItemContent(TargetLanguageComboBox, "自动判断", _uiLanguage == "zh-CN" ? "自动判断" : "Choose automatically");
        SetComboItemContent(TargetLanguageComboBox, "简体中文", _uiLanguage == "zh-CN" ? "简体中文" : "Simplified Chinese");
        SetComboItemContent(TargetLanguageComboBox, "英语", _uiLanguage == "zh-CN" ? "英语" : "English");
        SetComboItemContent(TargetLanguageComboBox, "日语", _uiLanguage == "zh-CN" ? "日语" : "Japanese");

        SetComboItemContent(TranslationModeComboBox, "fast", _uiLanguage == "zh-CN" ? "快速" : "Fast");
        SetComboItemContent(TranslationModeComboBox, "balanced", _uiLanguage == "zh-CN" ? "均衡" : "Balanced");
        SetComboItemContent(TranslationModeComboBox, "precise", _uiLanguage == "zh-CN" ? "精确" : "Precise");

        SetComboItemContent(TranslationToneComboBox, "natural", _uiLanguage == "zh-CN" ? "自然" : "Natural");
        SetComboItemContent(TranslationToneComboBox, "formal", _uiLanguage == "zh-CN" ? "正式" : "Formal");
        SetComboItemContent(TranslationToneComboBox, "concise", _uiLanguage == "zh-CN" ? "简洁" : "Concise");
        SetComboItemContent(TranslationToneComboBox, "academic", _uiLanguage == "zh-CN" ? "学术" : "Academic");
        SetComboItemContent(TranslationToneComboBox, "technical", _uiLanguage == "zh-CN" ? "技术" : "Technical");

        SetComboItemContent(ColorThemeComboBox, "ocean", _uiLanguage == "zh-CN" ? "深海蓝" : "Ocean blue");
        SetComboItemContent(ColorThemeComboBox, "violet", _uiLanguage == "zh-CN" ? "紫罗兰" : "Violet");
        SetComboItemContent(ColorThemeComboBox, "emerald", _uiLanguage == "zh-CN" ? "翡翠绿" : "Emerald");
        SetComboItemContent(ColorThemeComboBox, "sunset", _uiLanguage == "zh-CN" ? "暖橙色" : "Warm orange");
        SetComboItemContent(ColorThemeComboBox, "rose", _uiLanguage == "zh-CN" ? "玫瑰红" : "Rose");
        SetComboItemContent(ColorThemeComboBox, "custom", _uiLanguage == "zh-CN" ? "自定义" : "Custom");

        SetComboItemContent(ProviderComboBox, "deepseek", "DeepSeek API");
        SetComboItemContent(ProviderComboBox, "mock", _uiLanguage == "zh-CN" ? "Mock · 离线测试" : "Mock · offline test");

        SetComboItemContent(ChineseTranslationFontComboBox, "SimHei", _uiLanguage == UiLanguageCatalog.SimplifiedChineseLanguageId ? "黑体 (SimHei)" : "SimHei (Heiti)");
        SetComboItemContent(ChineseTranslationFontComboBox, "Microsoft YaHei UI", _uiLanguage == UiLanguageCatalog.SimplifiedChineseLanguageId ? "微软雅黑 (Microsoft YaHei UI)" : "Microsoft YaHei UI");
        SetComboItemContent(ChineseTranslationFontComboBox, "SimSun", _uiLanguage == UiLanguageCatalog.SimplifiedChineseLanguageId ? "宋体 (SimSun)" : "SimSun (Songti)");
        SetComboItemContent(ChineseTranslationFontComboBox, "KaiTi", _uiLanguage == UiLanguageCatalog.SimplifiedChineseLanguageId ? "楷体 (KaiTi)" : "KaiTi");

        if (_connectionStatusLocalizationKey is not null)
        {
            ConnectionStatusText.Text = L(_connectionStatusLocalizationKey);
        }

        UpdateTranslationMemoryStatus();
    }

    private void ClearTranslationMemoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_clearTranslationMemory is null || (_getTranslationMemoryCount?.Invoke() ?? 0) == 0)
        {
            return;
        }

        var result = WpfMessageBox.Show(
            this,
            L("ClearTranslationMemoryMessage"),
            L("ClearTranslationMemoryTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _clearTranslationMemory();
            UpdateTranslationMemoryStatus();
        }
        catch (Exception exception) when (exception is System.IO.IOException
            or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"InstantTranslate could not clear translation memory: {exception}");
            WpfMessageBox.Show(
                this,
                L("ClearTranslationMemoryFailed"),
                L("ClearTranslationMemoryTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void UpdateTranslationMemoryStatus()
    {
        if (TranslationMemoryStatusText is null || ClearTranslationMemoryButton is null)
        {
            return;
        }

        var count = _getTranslationMemoryCount?.Invoke() ?? 0;
        TranslationMemoryStatusText.Text = count == 0
            ? L("TranslationMemoryEmpty")
            : string.Format(System.Globalization.CultureInfo.CurrentCulture, L("TranslationMemoryCount"), count);
        ClearTranslationMemoryButton.IsEnabled = count > 0 && _clearTranslationMemory is not null;
    }

    private void ApplyLocalizedContent(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject)
            {
                continue;
            }

            if (dependencyObject is FrameworkElement element
                && element.Tag is string tag
                && tag.StartsWith("loc:", StringComparison.Ordinal))
            {
                var localized = L(tag[4..]);
                switch (element)
                {
                    case System.Windows.Controls.TextBlock textBlock:
                        textBlock.Text = localized;
                        break;
                    case System.Windows.Controls.ContentControl contentControl:
                        contentControl.Content = localized;
                        break;
                }
            }

            ApplyLocalizedContent(dependencyObject);
        }
    }

    private static void SetComboItemContent(
        System.Windows.Controls.ComboBox comboBox,
        string stableValue,
        string displayText)
    {
        foreach (var item in comboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, stableValue, StringComparison.OrdinalIgnoreCase))
            {
                item.Content = displayText;
                return;
            }
        }
    }

    private string L(string key)
    {
        if (!LocalizedText.TryGetValue(key, out var text))
        {
            return key;
        }

        return _uiLanguage == UiLanguageCatalog.SimplifiedChineseLanguageId ? text.Chinese : text.English;
    }

    private static string NormalizeUiLanguage(string? language)
    {
        return UiLanguageCatalog.Normalize(language);
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
        TestConnectionButton.IsEnabled = true;
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        var providerId = ProviderComboBox.SelectedValue as string ?? "deepseek";
        var endpointText = EndpointTextBox.Text.Trim();
        var model = ReadComboText(ModelComboBox);
        var apiKey = ApiKeyPasswordBox.Password.Trim();
        if (providerId == "deepseek"
            && (!TranslationProviderFactory.TryValidateEndpoint(endpointText, out _)
                || string.IsNullOrWhiteSpace(model)
                || string.IsNullOrWhiteSpace(apiKey)))
        {
            SetLocalizedConnectionStatus("FillConfiguration", "DangerBrush");
            return;
        }

        TestConnectionButton.IsEnabled = false;
        SetLocalizedConnectionStatus("Connecting", "AppMutedTextBrush");
        try
        {
            using var factory = new TranslationProviderFactory();
            var provider = factory.Create(new AppSettings
            {
                ProviderId = providerId,
                DeepSeekEndpoint = endpointText,
                DeepSeekModel = model,
                DeepSeekApiKey = apiKey,
                TranslationMode = TranslationPreferenceCatalog.NormalizeMode(
                    ReadComboText(TranslationModeComboBox)),
                TranslationTone = TranslationPreferenceCatalog.NormalizeTone(
                    ReadComboText(TranslationToneComboBox)),
                PersonalGlossary = PersonalGlossaryTextBox.Text.Trim(),
            });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var receivedText = false;
            await foreach (var chunk in provider.TranslateAsync(
                               new TranslationRequest(
                                   "hello",
                                   "英语",
                                   "简体中文",
                                   Mode: TranslationPreferenceCatalog.NormalizeMode(
                                       ReadComboText(TranslationModeComboBox)),
                                   Tone: TranslationPreferenceCatalog.NormalizeTone(
                                       ReadComboText(TranslationToneComboBox)),
                                   PersonalGlossary: PersonalGlossaryTextBox.Text.Trim()),
                               timeout.Token))
            {
                if (!string.IsNullOrWhiteSpace(chunk.TextDelta))
                {
                    receivedText = true;
                    break;
                }
            }

            if (!receivedText)
            {
                throw new TranslationProviderException(L("NoTestTranslation"));
            }

            SetLocalizedConnectionStatus("ConnectionSuccess", "SuccessBrush");
        }
        catch (OperationCanceledException)
        {
            SetLocalizedConnectionStatus("ConnectionTimeout", "DangerBrush");
        }
        catch (TranslationProviderException exception)
        {
            var message = UiLanguageCatalog.LocalizeProviderError(_uiLanguage, exception.Message);
            SetConnectionStatus(
                message.Length <= 58 ? message : message[..58] + "…",
                "DangerBrush");
        }
        catch (Exception)
        {
            SetConnectionStatus(
                _uiLanguage == UiLanguageCatalog.SimplifiedChineseLanguageId
                    ? "连接失败，请检查配置"
                    : "Connection failed. Check the configuration.",
                "DangerBrush");
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    private void ClearApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyPasswordBox.Clear();
        SetLocalizedConnectionStatus("DeleteAfterSave", "AppMutedTextBrush");
        ApiKeyPasswordBox.Focus();
    }

    private void SetConnectionStatus(string text, string brushResourceKey)
    {
        _connectionStatusLocalizationKey = null;
        ConnectionStatusText.SetResourceReference(
            System.Windows.Controls.TextBlock.ForegroundProperty,
            brushResourceKey);
        ConnectionStatusText.Text = text;
    }

    private void SetLocalizedConnectionStatus(string localizationKey, string brushResourceKey)
    {
        _connectionStatusLocalizationKey = localizationKey;
        ConnectionStatusText.SetResourceReference(
            System.Windows.Controls.TextBlock.ForegroundProperty,
            brushResourceKey);
        ConnectionStatusText.Text = L(localizationKey);
    }

    private void DefaultFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DefaultFontSizeValueText is not null)
        {
            DefaultFontSizeValueText.Text = e.NewValue.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
