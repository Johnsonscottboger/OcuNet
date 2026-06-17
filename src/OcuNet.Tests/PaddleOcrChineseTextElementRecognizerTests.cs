using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OcuNet.Tests;

public sealed class PaddleOcrChineseTextElementRecognizerTests : BaseRecognizerTests, IDisposable
{
    private readonly PaddleOcrTextElementRecognizer _recognizer;

    public PaddleOcrChineseTextElementRecognizerTests()
    {
        this._recognizer = new PaddleOcrTextElementRecognizer(new DriverOptions
        {
            OcrEngine = OcrEngine.PaddleOCR,
            PaddleOcrModel = PaddleOcrModel.ChineseV5,
        });
    }

    [Fact]
    public async Task Recognize_ChineseHotSearch()
    {
        using var screenshot = await BitmapUtils.FromAssembly("OcuNet.Tests.images.chinese-search.png");
        using var cropped = screenshot.Crop(new Rectangle(800, 1200, 1100, 1330));
        var element = new TextElement("热门搜索", TextOptions.None);

        var result = await this._recognizer.Recognize(cropped, element, CancellationToken.None);

        AssertResult(result, new Point(123, 61));
    }

    public void Dispose()
    {
        this._recognizer?.Dispose();
    }
}
