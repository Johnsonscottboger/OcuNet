using System;
using System.Threading.Tasks;
using Xunit;

namespace OcuNet.Tests;

public class OcuNetDriverTests_SearchRectangleClamping : BaseOcuNetDriverTests
{
    [Fact]
    public async Task WaitFor_MaximizedWindowBoundsExtendingBeyondMonitor_DoesNotThrowAndFinds()
    {
        // A maximized window keeps its invisible resize borders outside the monitor, so GetWindowRect
        // returns a rectangle that extends past the monitor edges ((-8, -8, 1928, 1088) on a 1920x1080
        // screen). The search area is clamped to the monitor, and since the monitor origin coincides with
        // the screen origin, the found location maps back to the screen unchanged.
        using var driver = this.CreateDriver();

        var needle = new FakeElement("needle");
        this.ElementRecognizer.AddExpectedResult(needle, new SearchResult(needle, new[] { new Rectangle(10, 40, 20, 50) }));

        var maximizedWindowBounds = new Rectangle(-8, -8, 1928, 1088);
        var result = await driver.WaitForAsync(needle, waitFor: TimeSpan.Zero, searchRect: maximizedWindowBounds);

        Assert.True(result.Success);
        Assert.Equal(new Rectangle(10, 40, 20, 50), result.Location);
        Assert.Equal(1, this.ElementRecognizer.RecognizeCallCount);
        Assert.Empty(this.FileWriter.SavedFailures);
    }

    [Fact]
    public async Task WaitFor_SearchRectPartiallyOffMonitor_ClampsToMonitorAndMapsLocations()
    {
        // Only the visible part of the search area is recognized: the found location is reported relative
        // to the clamped area (0, 100, 1900, 1080), so it shifts by that origin, not by the original
        // off-monitor one.
        using var driver = this.CreateDriver();

        var needle = new FakeElement("needle");
        this.ElementRecognizer.AddExpectedResult(needle, new SearchResult(needle, new[] { new Rectangle(10, 10, 20, 20) }));

        var offScreenSearchRect = new Rectangle(-8, 100, 1900, 1100);
        var result = await driver.WaitForAsync(needle, waitFor: TimeSpan.Zero, searchRect: offScreenSearchRect);

        Assert.True(result.Success);
        Assert.Equal(new Rectangle(10, 110, 20, 120), result.Location);
        Assert.Equal(1, this.ElementRecognizer.RecognizeCallCount);
        Assert.Empty(this.FileWriter.SavedFailures);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    public async Task IsVisible_SearchRectFullyOutsideMonitor_SkipsRecognitionAndReturnsFalse(int waitForMs)
    {
        // A search area with no visible intersection with the monitor is never recognized (no expected
        // result is registered above, so recognition would throw if it ran) and reports not found.
        using var driver = this.CreateDriver();

        var needle = new FakeElement("needle");
        var outsideMonitorRect = new Rectangle(1920, 0, 4000, 1080);
        var isVisible = await driver.IsVisibleAsync(needle, waitFor: TimeSpan.FromMilliseconds(waitForMs), searchRect: outsideMonitorRect);

        Assert.False(isVisible);
        Assert.Equal(0, this.ElementRecognizer.RecognizeCallCount);
        Assert.Empty(this.FileWriter.SavedFailures);
    }
}
