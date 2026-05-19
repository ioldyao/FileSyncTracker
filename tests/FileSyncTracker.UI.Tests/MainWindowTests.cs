using Avalonia.Headless;
using FileSyncTracker.UI.Views;
using Xunit;

namespace FileSyncTracker.UI.Tests;

public class MainWindowTests
{
    [Fact]
    public void MainWindow_CanCreate()
    {
        var window = new MainWindow();
        Assert.NotNull(window);
    }
}
