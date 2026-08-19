<p align="center">
  <img src="src/InstantTranslate.App/Assets/AppLogo.png" width="128" alt="InstantTranslate logo">
</p>

<h1 align="center">InstantTranslate</h1>

<p align="center">Windows 全局划词翻译 · DeepSeek 流式响应 · 安静、快速、默认不留记录</p>

InstantTranslate 是一个个人自用、可开源的 Windows 划词翻译工具。在大多数可选择文字的应用中拖选文本，它会读取选区、调用 DeepSeek，并在鼠标附近显示不抢焦点的译文浮窗。

## 15 秒开始使用

1. 下载并解压 `InstantTranslate-v0.4.1-win-x64.zip`。
2. 运行 `InstantTranslate.exe`；首次启动会打开设置。
3. 填写 DeepSeek API Key，点击“Test connection”（切换中文后为“测试连接”），成功后保存。
4. 在记事本、浏览器或文档中用鼠标拖选文字。

程序常驻系统托盘。再次双击 EXE 不会重复安装鼠标钩子，而会唤起已有实例的设置页。

## v0.4 亮点

- 程序界面默认使用英文；设置页右上角的“中文 / English”按钮可以即时切换，保存后同步应用到浮窗提示、托盘菜单和通知。
- 非译文界面内嵌使用 Source Sans Pro，无需朋友的电脑另行安装字体。
- 英文译文和中文译文可以分别选择字体；英文包含 Times New Roman、Arial、Source Sans Pro、Georgia、Calibri、Cambria，中文包含黑体、微软雅黑、宋体和楷体。
- 可选“周围语境”：仅在主动开启后读取选区所在段落，帮助模型判断代词、一词多义和专业语境；默认关闭。
- 新增快速、均衡、精确三种翻译模式，以及自然、正式、简洁、学术、技术五种表达风格。
- 新增本机个人术语库，使用 `原词 => 指定译法` 的简单格式；请求时只发送当前选区实际命中的术语。

- 自动判断方向：纯中文译为英文；中英混合、英文和其他文本优先译为简体中文。
- DeepSeek OpenAI-compatible SSE 流式翻译；收到首段译文后才显示浮窗，不再先弹出旋转等待动画，后续内容合并刷新。
- 完成结果使用内存 LRU 缓存；相同配置和文本再次翻译可直接显示，不再次请求 API。
- 浮窗出现时不抢焦点，靠近选区并自动避让屏幕边缘；主动点击正文后可正常选字和复制。支持关闭、复制原文、复制译文、中英互换、保留、拖动、缩放和纵向滚动。
- 译文可像普通文本一样框选，右键支持复制和全选；在本程序内框选不会触发新的翻译。
- 英文译文默认使用 Times New Roman，中文默认使用黑体；两者均可在设置中独立修改。字号可在浮窗平滑调节，也可在设置中指定新窗口默认值。
- 接近 Apple 内容优先原则的浅色、不透明、无边框圆角界面；字号控制按需展开，复制操作形成统一分组，设置页使用安静的中性色与固定保存栏。
- 断网、鉴权、限流或超时不再让加载窗无故消失；错误会保留在原浮窗中，再次翻译失败时保留旧译文。
- 单实例运行，避免重复托盘、重复钩子和重复 API 费用。
- 自动取词默认不再改写系统剪贴板：先使用 UI Automation，再尝试标准 Win32/RichEdit/Scintilla 控件的窗口消息读取。只有在设置中主动打开“Compatibility clipboard fallback”时，才会为微信等自绘应用使用临时 `WM_COPY` 兼容回退。

## 微信和自绘应用

Windows 应用取词能力并不统一。InstantTranslate 按以下顺序尝试：

1. UI Automation `TextPattern.GetSelection()`；
2. 标准 Win32/RichEdit/Scintilla 控件的直接选区读取，不触碰剪贴板；
3. 仅在设置中主动开启兼容模式后，才使用窗口级 `WM_COPY`；
4. 用户主动复制后的手动翻译。

默认的自动取词链路不会发送 `WM_COPY`、模拟键盘或修改剪贴板，也不会把 `c` 输入当前编辑框。兼容模式是有意关闭的最后手段，因为任何 `WM_COPY` 方案都无法保证对第三方剪贴板格式、历史记录和监听器完全无副作用。

微信部分版本的消息区是自绘界面，既不公开 UI Automation 选区，也不响应标准 `WM_COPY`。这时使用稳定的手动方式：

1. 在微信中选中文字并由你自己按 `Ctrl+C`；
2. 按 `Ctrl+Shift+T`，或右键托盘图标选择“翻译剪贴板”。

这个入口不会注入任何按键，也可以在关闭“启用鼠标划词翻译”后单独使用。

## 浮窗操作

- “译为中文 / 译为英文”：把当前显示结果翻译为另一种语言，复用原窗口位置和尺寸。
- Copy 分组中的“原文 / Source”：复制最初选中的文字。
- Copy 分组中的“译文 / Translation”：优先复制你在译文中框选的部分，否则复制全部译文。
- `Aa`：按需展开字号滑杆，避免不调字号时持续占用工具条空间。
- 图钉：保留窗口。保留后可拖动文字外区域，并从任意边缘或角落调整尺寸；继续划词会创建另一临时浮窗，正在生成的保留结果也不会被新选词截断。
- ×：立即关闭当前浮窗。

译文显示后，无论是否保留，都可以从任意边缘或角落调整尺寸；长文本或大字号超出当前高度时会在正文内部滚动。取消保留不会立即关闭窗口；下一次点击其他区域才按临时窗口规则收起。

## DeepSeek 设置

默认配置：

- Endpoint：`https://api.deepseek.com`
- 速度优先模型：`deepseek-v4-flash`
- 可选模型：`deepseek-v4-pro`
- API：`POST /chat/completions`、`stream: true`、`thinking: disabled`

远程 Endpoint 必须使用 HTTPS；只有 `localhost` 和回环地址允许 HTTP，避免 API Key 和选中文字被明文传输。模型与接口变化以 [DeepSeek 官方 API 文档](https://api-docs.deepseek.com/) 为准。

API Key 由密码框录入并保存在 Windows 凭据管理器中，不写入设置 JSON。设置页可以在不保存的情况下测试连接，也可清空密钥后保存以删除凭据。

## 托盘菜单

- 当前版本与启用状态
- 翻译剪贴板（`Ctrl+Shift+T`）
- 启用或暂停自动划词
- 设置
- 关于与版本
- 退出

设置页还可控制登录 Windows 后自动启动。启动项只写入当前用户；移动 EXE 后请重新运行并保存一次设置，以更新路径。

## 隐私与安全

- 使用 DeepSeek Provider 时，原文会发送到你配置的 Endpoint；Mock Provider 完全离线。
- “使用周围语境”默认关闭；开启后，选区所在段落会作为只读参考一并发送。个人术语库保存在本机设置中，请求时仅发送当前选区命中的术语对。
- 默认不持久化原文、译文或运行日志。
- 缓存只存在于当前进程内，默认最多 128 条、约 100 万字符预算、20 分钟有效；键中只保留原文指纹而非原文全文，退出即清空。
- API Key 保存在 Windows Credential Manager 的 `InstantTranslate/DeepSeekApiKey`。
- 自动取词默认不注入键盘、不修改剪贴板；终端应用始终禁用自动剪贴板回退。若确实需要兼容自绘应用，可在设置中主动打开兼容模式。
- 跨窗口或跨进程拖动会被拒绝，UI Automation 焦点候选也必须属于鼠标命中的同一进程。
- UI Automation 密码元素和带 Windows 密码样式的原生输入框会被直接跳过。
- 应用以普通用户权限运行，不请求管理员权限或 `uiAccess`。

## 从源码运行

要求 Windows 10/11 和 .NET 8 SDK：

```powershell
dotnet restore .\InstantTranslate.sln
dotnet build .\InstantTranslate.sln --configuration Release --no-restore
dotnet test .\InstantTranslate.sln --configuration Release --no-build --no-restore
dotnet run --project .\src\InstantTranslate.App\InstantTranslate.App.csproj
```

不调用 DeepSeek 的启动烟雾测试：

```powershell
dotnet run --project .\src\InstantTranslate.App\InstantTranslate.App.csproj --configuration Release --no-build -- --smoke-test
dotnet run --project .\src\InstantTranslate.App\InstantTranslate.App.csproj --configuration Release --no-build -- --popup-smoke-test
dotnet run --project .\src\InstantTranslate.App\InstantTranslate.App.csproj --configuration Release --no-build -- --popup-snapshot-test
dotnet run --project .\src\InstantTranslate.App\InstantTranslate.App.csproj --configuration Release --no-build -- --settings-snapshot-test
```

两个快照模式只渲染本应用自己的 WPF 窗口，用于检查浮窗选区、长文滚动和设置页布局，不读取桌面或其他应用画面。

## 打包

```powershell
.\scripts\Publish.ps1 -Version 0.4.1
```

脚本会依次恢复依赖、Release 构建、运行全部测试、发布自包含单文件程序、执行烟雾测试、生成 ZIP 和 SHA-256 校验文件。结果位于 `artifacts/release/`。

当前版本没有商业代码签名；在部分电脑上首次运行可能出现 Windows SmartScreen“未知发布者”提示。

## 已知限制

- 目前主要响应鼠标拖选；双击选词和纯键盘选区不会自动触发。
- 自绘画布、部分 Electron/微信版本和高权限窗口可能无法通过无剪贴板方式自动读取；默认请使用复制后 `Ctrl+Shift+T`，或在设置中明确打开兼容性剪贴板回退。
- 尚未加入 OCR、安装器、自动更新和 ARM64 包。
- 个别精简版 Windows 可能没有黑体、宋体或楷体；WPF 会使用系统可用字体回退，Source Sans Pro 界面字体则已随程序内嵌。

## 第三方字体

界面字体 Source Sans Pro 来自 Adobe Source Sans 项目，依据 SIL Open Font License 1.1 随程序分发；完整字体许可包含在成品的 `Assets/Fonts/LICENSE-SourceSans.md`。

## 代码结构

- `Hooks/`：全局鼠标钩子、窗口拖动抑制和全局快捷键。
- `Selection/`：UI Automation、原生控件取词、可选剪贴板回退和文本规范化。
- `Translation/`：DeepSeek 流式 Provider、语言方向、内存缓存与更新节流。
- `Services/`：单实例、请求会话、托盘和端到端协调。
- `Settings/`：设置、主题、开机启动与 Windows 凭据存储。
- `Windows/`：设置窗口和无焦点译文浮窗。

## License

[MIT](LICENSE)
