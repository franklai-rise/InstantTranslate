<p align="center">
  <img src="src/InstantTranslate.App/Assets/AppLogo.png" width="132" alt="InstantTranslate logo">
</p>

<h1 align="center">InstantTranslate</h1>

<p align="center">Windows 全局划词翻译工具 · DeepSeek 流式响应 · 无焦点浮窗</p>

InstantTranslate 是一个 Windows 全局划词翻译工具。它在鼠标拖选文本后，通过 Windows UI Automation 读取选区，调用 DeepSeek 官方 API 流式翻译，并在鼠标附近显示不抢焦点的置顶译文浮窗。

## 当前能力

- 低级全局鼠标钩子识别左键拖选，普通点击不触发翻译。
- 优先使用 UI Automation `TextPattern.GetSelection()` 读取选中文字。
- 自动跳过 InstantTranslate 自身的设置窗口和浮窗。
- 鼠标释放后立即显示无焦点加载浮窗，首个流式分片到达后原位切换为译文。
- 自动判断翻译方向：纯中文译为英文；中英混合、英文及其他文本优先译为简体中文。
- 译文浮窗提供切换语言、复制原文、复制译文和置顶图标按钮，并通过悬停提示说明用途。
- 置顶会把当前结果保留为可拖动的独立快照；继续划词会打开新的临时浮窗，不覆盖已置顶内容。
- 内置深海蓝、紫罗兰、翡翠绿、暖橙色和玫瑰红主题，也可输入 `#RRGGBB` 自定义强调色。
- 无焦点、无任务栏入口的临时浮窗会在下一次外部鼠标按下时隐藏。
- 递增请求版本与 `CancellationToken`：连续选词时只允许最后一次结果显示。
- DeepSeek 官方 OpenAI-compatible SSE 流式 Provider；默认使用低延迟 `deepseek-v4-flash` 并关闭思考模式。
- 可在设置中切换到完全离线的 Mock Provider。
- 系统托盘菜单：设置、启用/停用、退出。
- 普通设置保存到 `%LOCALAPPDATA%\InstantTranslate\settings.json`。
- DeepSeek API Key 通过密码框录入并保存到 Windows 凭据管理器，不写入 JSON。
- 默认不记录选词和译文，不使用剪贴板取词，不模拟 `Ctrl+C`；只有用户点击复制按钮时才写入剪贴板。

## 环境与运行

普通用户可从 GitHub Releases 下载 `InstantTranslate-v0.1.0-win-x64.zip`，解压后直接运行 `InstantTranslate.exe`，无需另装 .NET。

源码开发要求 Windows 10/11 和 .NET 8 SDK：

```powershell
dotnet restore .\InstantTranslate.sln
dotnet build .\InstantTranslate.sln --configuration Debug
dotnet test .\InstantTranslate.sln --configuration Debug --no-build
dotnet run --project .\src\InstantTranslate.App\InstantTranslate.App.csproj
```

程序启动后常驻系统托盘。首次运行会打开设置页；填写 DeepSeek API Key 并保存即可使用。右键托盘图标可再次打开设置、切换启用状态或退出。

设置页中的“颜色风格”可选择五套内置主题；选择“自定义”后可输入类似 `#2563EB` 的颜色值。保存后，新主题会立即应用到浮窗。

DeepSeek 默认配置：

- Endpoint：`https://api.deepseek.com`
- Model：`deepseek-v4-flash`（速度优先）
- 可选模型：`deepseek-v4-pro`
- API：`POST /chat/completions`，`stream: true`，`thinking: disabled`

模型和接口信息以 [DeepSeek 官方 API 文档](https://api-docs.deepseek.com/) 为准。旧模型名 `deepseek-chat` 和 `deepseek-reasoner` 已不作为默认选项。

开发环境可用以下命令验证托盘、WPF Dispatcher 和全局钩子是否能完成启动与释放；程序会在约 750 ms 后自行退出，且不会调用 DeepSeek：

```powershell
dotnet run --project .\src\InstantTranslate.App\InstantTranslate.App.csproj --no-build -- --smoke-test
```

## MVP 验收方法

1. 启动程序，从托盘设置中选择 `DeepSeek API`，填写 API Key，并保留默认 `deepseek-v4-flash`。
2. 在 Windows 记事本中输入并拖选一段英文。
3. 鼠标释放后应立即出现“正在翻译…”动画，随后原位切换并流式显示译文；原窗口不应丢失键盘焦点。
4. 在 Edge 普通网页正文中重复测试。
5. 单击或在 InstantTranslate 设置窗口内拖动时不应出现译文。
6. 快速连续拖选时，只应显示最后一次选词结果，前一次 HTTP 流应被取消。
7. 下一次鼠标按下应立即隐藏现有浮窗。
8. 点击置顶图标后，可按住译文区域拖动浮窗；继续划词时旧结果应保留，新结果应出现在另一个临时浮窗中。
9. 中文原文默认译为英文；中英混合默认整体译为中文；语言按钮可对同一原文反向重译。

如果暂时不想消耗 API 额度，可在设置中切换到 Mock Provider。Mock 包含 `Hello world` → `你好，世界`、`Good morning` → `早上好`；其他非空输入固定返回“这是模拟译文。”。

## 打包

运行以下脚本会生成 Windows x64 自包含单文件程序和 ZIP 压缩包：

```powershell
.\scripts\Publish.ps1 -Version 0.1.0
```

输出位于 `artifacts/release/`。由于当前版本未购买代码签名证书，Windows SmartScreen 首次运行时可能显示未知发布者提示。

## 代码结构

- `Hooks/`：全局鼠标钩子与拖选判定。
- `Selection/`：UI Automation 取词与文本规范化。
- `Translation/`：流式 Provider 契约、DeepSeek SSE 实现、Provider 工厂和 Mock 实现。
- `Services/`：请求版本控制、端到端协调器、托盘与自身进程判断。
- `Settings/`：基础设置、JSON 存储与 Windows 凭据管理器。
- `Windows/`：设置窗口和无焦点译文浮窗。

`DeepSeekStreamingProvider` 通过 `IAsyncEnumerable<TranslationChunk>` 逐段产出 `content`，忽略推理字段，并严格响应取消令牌。`TranslationProviderFactory` 复用一个带连接池的 `HttpClient`，避免每次选词重新建连。

## 已知限制

- 仅响应鼠标拖选；暂不支持双击选词和纯键盘选区。
- 只支持提供 UI Automation TextPattern 的应用。终端、画布渲染文本、部分 Electron 应用或高权限窗口可能无法取词。
- 不包含剪贴板回退，因此 UIA 不可用时会静默跳过。
- 普通权限进程不能读取以管理员权限运行的应用；MVP 不请求提权。
- 缓存、开机启动、安装包、自动更新和请求用量统计尚未实现。

## 隐私与安全

- 选择 DeepSeek Provider 时，选中文字会发送到配置的 Endpoint；选择 Mock Provider 时完全离线。
- 不持久化选词、译文或运行日志。
- API Key 使用 Windows 凭据管理器条目 `InstantTranslate/DeepSeekApiKey`。
- 应用以当前用户普通权限运行，不启用 `uiAccess`；只有明确点击复制按钮时才修改剪贴板。

## License

[MIT](LICENSE)
