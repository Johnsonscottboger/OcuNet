using System;
using System.Collections.Generic;
using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace OcuNet;

internal static class TextPreprocessor
{
    public static Mat PreprocessToMat(Bitmap screenshot, TextOptions options, int upscalingRatio)
    {
        return screenshot.ToMat()
            .ConvertAndDispose(Upscale(upscalingRatio))
            .ConvertAndDispose(GetConverters(options));
    }

    public static Rectangle Downscale(Rectangle rectangle, int upscalingRatio)
    {
        return rectangle / (upscalingRatio, upscalingRatio);
    }

    private static Func<Mat, Mat> Upscale(int ratio)
    {
        return mat => mat.Resize(Multiply(mat.Size(), ratio), 0, 0, InterpolationFlags.Nearest);
    }

    private static IEnumerable<Func<Mat, Mat>> GetConverters(TextOptions options)
    {
        if (options == TextOptions.None)
        {
            yield break;
        }

        if (options.HasFlag(TextOptions.Grayscale))
        {
            yield return Grayscale;
        }

        if (options.HasFlag(TextOptions.BlackAndWhite))
        {
            yield return Binarize;
        }

        if (options.HasFlag(TextOptions.Negative))
        {
            yield return Negate;
        }
    }

    private static Mat Grayscale(Mat mat)
    {
        return mat.ToGrayscale();
    }

    private static Mat Binarize(Mat mat)
    {
        const float unusedThresholdOverridenByOtsuAlgorithm = 128;
        return mat.Threshold(unusedThresholdOverridenByOtsuAlgorithm, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
    }

    private static Mat Negate(Mat mat)
    {
        return mat.OnesComplement().ConvertAndDispose(x => x.ToMat());
    }

    private static OpenCvSharp.Size Multiply(OpenCvSharp.Size size, int factor)
    {
        return new OpenCvSharp.Size(size.Width * factor, size.Height * factor);
    }
}
