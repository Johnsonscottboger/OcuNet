using System;
using System.Drawing;

namespace OcuNet;

/// <summary>
/// The result of a window scroll screenshot: the stitched long image plus metadata about the capture.
/// Disposing this object disposes the <see cref="Image"/>.
/// </summary>
public sealed class ScrollScreenshotResult : IDisposable
{
    internal ScrollScreenshotResult(
        Bitmap image,
        int frameCount,
        int scrollSteps,
        ScrollScreenshotStopReason stopReason,
        TimeSpan elapsed,
        Rectangle captureRect,
        int topBandRows,
        int bottomBandRows)
    {
        this.Image = image;
        this.FrameCount = frameCount;
        this.ScrollSteps = scrollSteps;
        this.StopReason = stopReason;
        this.Elapsed = elapsed;
        this.CaptureRect = captureRect;
        this.TopBandRows = topBandRows;
        this.BottomBandRows = bottomBandRows;
    }

    /// <summary>
    /// Gets the stitched long image.
    /// </summary>
    public Bitmap Image { get; }

    /// <summary>
    /// Gets the number of frames that were captured and stitched.
    /// </summary>
    public int FrameCount { get; }

    /// <summary>
    /// Gets the number of scroll steps that were performed (0 when the stop condition was met before any
    /// scroll).
    /// </summary>
    public int ScrollSteps { get; }

    /// <summary>
    /// Gets the reason why the scroll screenshot stopped.
    /// </summary>
    public ScrollScreenshotStopReason StopReason { get; }

    /// <summary>
    /// Gets the total duration of the scroll screenshot.
    /// </summary>
    public TimeSpan Elapsed { get; }

    /// <summary>
    /// Gets the captured region in absolute screen coordinates (the attached window bounds).
    /// </summary>
    public Rectangle CaptureRect { get; }

    /// <summary>
    /// Gets the number of fixed rows automatically detected at the top of the captured frames
    /// (status bar, sticky header, etc.). Informational only.
    /// </summary>
    public int TopBandRows { get; }

    /// <summary>
    /// Gets the number of fixed rows automatically detected at the bottom of the captured frames
    /// (status bar, sticky footer, etc.). Informational only.
    /// </summary>
    public int BottomBandRows { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Image.Dispose();
    }
}
