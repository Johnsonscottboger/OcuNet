using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace OcuNet.Tests;

public sealed class PaddleOcrTextElementRecognizerTests : BaseRecognizerTests, IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly PaddleOcrTextElementRecognizer _recognizer;

    public PaddleOcrTextElementRecognizerTests(ITestOutputHelper output)
    {
        this._output = output;
        this._recognizer = new PaddleOcrTextElementRecognizer(new DriverOptions { OcrEngine = OcrEngine.PaddleOCR });
    }

    [Theory]
    [InlineData(0, 0, 120, 30, "Google News", TextOptions.BlackAndWhite, 79, 17)]
    [InlineData(380, 170, 880, 336, "Headlines", TextOptions.None, 62, 17)]
    [InlineData(380, 170, 880, 336, "mperatures in southern califor", TextOptions.None, 260, 148)]
    [InlineData(20, 810, 160, 860, "English (United States)", TextOptions.None, 66, 36)]
    [InlineData(0, 0, 1920, 1080, "historic miami-dade courthouse closed due to ", TextOptions.None, 667, 665)]
    public async Task Recognize_WhenSingleMatch_Works(int x1, int y1, int x2, int y2, string searched, TextOptions options, int expectedX, int expectedY)
    {
        using var screenshot = await BitmapUtils.FromAssembly("OcuNet.Tests.images.google-news.png");
        using var cropped = screenshot.Crop(new Rectangle(x1, y1, x2, y2));
        var element = new TextElement(searched, options);

        var result = await this._recognizer.Recognize(cropped, element, CancellationToken.None);

        AssertResult(result, new Point(expectedX, expectedY));
    }

    [Fact]
    public async Task Recognize_WhenMultipleMatches_Works()
    {
        using var screenshot = await BitmapUtils.FromAssembly("OcuNet.Tests.images.google-news.png");
        using var cropped = screenshot.Crop(new Rectangle(410, 300, 800, 550));
        var element = new TextElement("California") { IgnoreCase = false };

        var result = await this._recognizer.Recognize(cropped, element, CancellationToken.None);

        AssertResult(result, new Point(196, 19), new Point(199, 132));
    }

    public void Dispose()
    {
        this._recognizer?.Dispose();
    }
}
