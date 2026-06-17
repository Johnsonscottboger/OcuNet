using System;
using Xunit;

namespace OcuNet.Tests;

public class DriverOptionsTests
{
    [Fact]
    public void Defaults()
    {
        var options = new DriverOptions();

        Assert.Equal(OcrEngine.PaddleOCR, options.OcrEngine);
        Assert.Equal(PaddleOcrModel.EnglishV5, options.PaddleOcrModel);
        Assert.Equal(PaddleOcrDevice.Mkldnn, options.PaddleOcrDevice);
        Assert.Equal(0.5f, options.PaddleOcrMinimumScore);
        Assert.False(options.PaddleOcrAllowRotateDetection);
        Assert.False(options.PaddleOcrEnable180Classification);
    }

    [Fact]
    public void CanSetPaddleOcrProperties()
    {
        var options = new DriverOptions
        {
            OcrEngine = OcrEngine.Tesseract,
            PaddleOcrModel = PaddleOcrModel.ChineseV5,
            PaddleOcrDevice = PaddleOcrDevice.Default,
            PaddleOcrMinimumScore = 0.75f,
            PaddleOcrAllowRotateDetection = true,
            PaddleOcrEnable180Classification = true,
        };

        Assert.Equal(OcrEngine.Tesseract, options.OcrEngine);
        Assert.Equal(PaddleOcrModel.ChineseV5, options.PaddleOcrModel);
        Assert.Equal(PaddleOcrDevice.Default, options.PaddleOcrDevice);
        Assert.Equal(0.75f, options.PaddleOcrMinimumScore);
        Assert.True(options.PaddleOcrAllowRotateDetection);
        Assert.True(options.PaddleOcrEnable180Classification);
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    public void PaddleOcrMinimumScore_OutOfRange_Throws(float value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DriverOptions { PaddleOcrMinimumScore = value });
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    public void PaddleOcrMinimumScore_InRange_Works(float value)
    {
        var options = new DriverOptions { PaddleOcrMinimumScore = value };
        Assert.Equal(value, options.PaddleOcrMinimumScore);
    }
}
