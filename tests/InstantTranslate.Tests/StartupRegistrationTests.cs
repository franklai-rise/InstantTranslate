using InstantTranslate.Settings;

namespace InstantTranslate.Tests;

public sealed class StartupRegistrationTests
{
    [Fact]
    public void BuildCommand_QuotesExecutablePath()
    {
        var command = StartupRegistration.BuildCommand(@"C:\Apps With Spaces\InstantTranslate.exe");

        Assert.Equal("\"C:\\Apps With Spaces\\InstantTranslate.exe\"", command);
    }

    [Fact]
    public void BuildCommand_RejectsEmbeddedQuote()
    {
        Assert.Throws<ArgumentException>(() => StartupRegistration.BuildCommand("bad\"path.exe"));
    }
}
