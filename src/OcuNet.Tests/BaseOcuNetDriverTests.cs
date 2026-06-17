#pragma warning disable CS8618

using System.Threading.Tasks;
using Xunit;

namespace OcuNet.Tests;

public class BaseOcuNetDriverTests : IAsyncLifetime
{
    protected const string FakeFailuresScreenshotPath = "C:\\foo\\bar\\";

    internal FakeMonitorService MonitorService { get; private set; }

    internal FakeElementRecognizer ElementRecognizer { get; private set; }

    internal FakeScreenshotWriter FileWriter { get; private set; }

    internal FakeMouseController MouseController { get; private set; }

    internal FakeKeyboardController KeyboardController { get; private set; }

    public virtual async Task InitializeAsync()
    {
        using var screenshot = await BitmapUtils.FromAssembly("OcuNet.Tests.images.google-news.png");
        this.MonitorService = new FakeMonitorService(screenshot);

        this.ElementRecognizer = new FakeElementRecognizer();
        this.FileWriter = new FakeScreenshotWriter();

        this.MouseController = new FakeMouseController();
        this.KeyboardController = new FakeKeyboardController();
    }

    protected OcuNetDriver CreateDriver(DriverOptions? options = null)
    {
        options ??= new DriverOptions();

        return new OcuNetDriver(options, this.FileWriter, this.MonitorService, this.ElementRecognizer, this.MouseController, this.KeyboardController);
    }

    public virtual Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    protected static void AssertSearchResult(SearchResult expected, SearchResult actual, Rectangle? searchRect = null)
    {
        Assert.NotNull(actual);
        Assert.NotNull(expected);

        expected = expected.AdjustToSearchRectangle(searchRect);

        Assert.Equal(expected.Success, actual.Success);
        Assert.Equal(expected.Element, actual.Element);

        Assert.Equal(expected.Locations.Count, actual.Locations.Count);

        for (var i = 0; i < expected.Locations.Count; i++)
        {
            Assert.Equal(expected.Locations[i], actual.Locations[i]);
        }
    }
}
