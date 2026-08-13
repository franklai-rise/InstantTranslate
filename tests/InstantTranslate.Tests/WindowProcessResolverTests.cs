using InstantTranslate.Services;

namespace InstantTranslate.Tests;

public sealed class WindowProcessResolverTests
{
    [Theory]
    [InlineData("cmd")]
    [InlineData("PowerShell")]
    [InlineData("pwsh")]
    [InlineData("WindowsTerminal")]
    [InlineData("conhost")]
    public void ClipboardFallbackRejectsTerminalProcesses(string processName)
    {
        Assert.False(WindowProcessResolver.IsClipboardFallbackProcessAllowed(processName));
    }

    [Theory]
    [InlineData("WeChat")]
    [InlineData("chrome")]
    [InlineData("notepad")]
    public void ClipboardFallbackAllowsOrdinaryProcesses(string processName)
    {
        Assert.True(WindowProcessResolver.IsClipboardFallbackProcessAllowed(processName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ClipboardFallbackRejectsMissingProcessNames(string? processName)
    {
        Assert.False(WindowProcessResolver.IsClipboardFallbackProcessAllowed(processName));
    }
}
