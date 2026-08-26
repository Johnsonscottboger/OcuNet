using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace OcuNet;

internal sealed class ImageElementRecognizer : IElementRecognizer
{
    // Scale pyramid for matching templates captured at a different DPI scaling than
    // the current screen (e.g. a template captured on a 150%-scaled display being
    // matched on a 100% display, or vice versa). Only consulted when the native
    // scale (1.0) finds no match, so the common same-scale path keeps its exact
    // original behavior including multi-location flood-fill dedup.
    private static readonly float[] ScalePyramid = new[] { 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.1f, 1.25f, 1.5f, 1.75f, 2.0f };

    public async Task<RecognizerSearchResult> Recognize(Bitmap screenshot, IElement element, CancellationToken token)
    {
        RecognizerSearchResult RecognizeInternal()
        {
            var imageElement = (ImageElement)element;

            using var preprocessedScreenshotMat = screenshot.ToMat().ConvertAndDispose(x => NormalizeMatChannels(x, imageElement.Grayscale));

            if (token.IsCancellationRequested)
            {
                return RecognizerSearchResult.NotFound(preprocessedScreenshotMat.ToBitmap(), element);
            }

            using var elementTemplate = imageElement.ToBitmap()
                .ConvertAndDispose(x => x.ToMat())
                .ConvertAndDispose(x => NormalizeMatChannels(x, imageElement.Grayscale));

            if (token.IsCancellationRequested)
            {
                return RecognizerSearchResult.NotFound(preprocessedScreenshotMat.ToBitmap(), element);
            }

            // Native-scale pass (original behavior): multi-location detection with
            // flood-fill dedup.
            var locations = MatchAtNativeScale(preprocessedScreenshotMat, elementTemplate, (double)imageElement.Threshold, token);

            // Cross-scale pass: when the native scale finds nothing, retry across a
            // scale pyramid so templates captured at a different DPI scaling match.
            if (locations.Count == 0 && !token.IsCancellationRequested)
            {
                var crossScaleLocation = MatchAtBestScale(preprocessedScreenshotMat, elementTemplate, (double)imageElement.Threshold, token);
                if (crossScaleLocation != null)
                {
                    locations.Add(crossScaleLocation);
                }
            }

            return new RecognizerSearchResult(preprocessedScreenshotMat.ToBitmap(), element, locations);
        }

        // ReSharper disable once MethodSupportsCancellation
        return await Task.Run(RecognizeInternal).ConfigureAwait(false);
    }

    /// <summary>
    /// Template matching at the template's native scale, returning every match
    /// (flood-fill dedup), or an empty list when nothing reaches the threshold.
    /// </summary>
    private static List<Rectangle> MatchAtNativeScale(Mat screenshot, Mat template, double threshold, CancellationToken token)
    {
        var locations = new List<Rectangle>();

        // OpenCV template matching
        // https://stackoverflow.com/a/35346975/825695
        using var workingScreenshotMat = screenshot
            .MatchTemplate(template, TemplateMatchModes.CCoeffNormed)
            .ConvertAndDispose(x => x.Threshold(threshold, 1d, ThresholdTypes.Tozero));

        var loDiff = new Scalar(0.1);
        var upDiff = new Scalar(1.0);

        while (!token.IsCancellationRequested)
        {
            workingScreenshotMat.MinMaxLoc(out _, out var maxval, out _, out var maxloc);

            var notFound = maxval < threshold;
            if (notFound)
            {
                break;
            }

            locations.Add(new Rectangle(maxloc.X, maxloc.Y, maxloc.X + template.Width, maxloc.Y + template.Height));
            workingScreenshotMat.FloodFill(maxloc, new Scalar(0), out _, loDiff, upDiff);
        }

        return locations;
    }

    /// <summary>
    /// Returns the single best match across a scale pyramid of the template, or
    /// <c>null</c> when no scale reaches the threshold. The returned rectangle is
    /// expressed in screenshot coordinates (the top-left of a scaled-down/up
    /// template still pins the same screen position).
    /// </summary>
    private static Rectangle? MatchAtBestScale(Mat screenshot, Mat template, double threshold, CancellationToken token)
    {
        double bestVal = threshold;
        OpenCvSharp.Point? bestLocation = null;
        var bestRight = 0;
        var bestBottom = 0;

        foreach (var scale in ScalePyramid)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            var width = Math.Max(4, (int)Math.Round(template.Width * scale));
            var height = Math.Max(4, (int)Math.Round(template.Height * scale));

            // The template must not exceed the screenshot dimensions.
            if (width > screenshot.Width || height > screenshot.Height)
            {
                continue;
            }

            using var resizedTemplate = new Mat();
            Cv2.Resize(template, resizedTemplate, new OpenCvSharp.Size(width, height), 0, 0, InterpolationFlags.Linear);

            using var result = screenshot.MatchTemplate(resizedTemplate, TemplateMatchModes.CCoeffNormed);
            result.MinMaxLoc(out _, out var maxval, out _, out var maxloc);

            if (maxval > bestVal)
            {
                bestVal = maxval;
                bestLocation = maxloc;
                bestRight = maxloc.X + width;
                bestBottom = maxloc.Y + height;
            }
        }

        if (bestLocation == null)
        {
            return null;
        }

        return new Rectangle(bestLocation.Value.X, bestLocation.Value.Y, bestRight, bestBottom);
    }

    private static Mat NormalizeMatChannels(Mat mat, bool isGrayscale)
    {
        return isGrayscale ? mat.ToGrayscale() : mat.ToBGR();
    }
}
