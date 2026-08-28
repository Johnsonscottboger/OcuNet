using System;
using System.Threading;

namespace OcuNet;

/// <summary>
/// Context passed to the user-provided stop condition of a window scroll screenshot
/// (see <see cref="ScrollScreenshot.CaptureAsync"/>).
/// </summary>
public sealed record ScrollScreenshotContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollScreenshotContext"/> class.
    /// </summary>
    /// <param name="driver">The driver performing the scroll capture.</param>
    /// <param name="scrollStep">The number of scroll steps already performed (0 = not scrolled yet).</param>
    /// <param name="elapsed">The elapsed time since the scroll capture started.</param>
    /// <param name="captureRect">The captured region in absolute screen coordinates.</param>
    /// <param name="cancellationToken">The cancellation token of the scroll capture.</param>
    public ScrollScreenshotContext(
        OcuNetDriver driver,
        int scrollStep,
        TimeSpan elapsed,
        Rectangle captureRect,
        CancellationToken cancellationToken)
    {
        this.Driver = driver;
        this.ScrollStep = scrollStep;
        this.Elapsed = elapsed;
        this.CaptureRect = captureRect;
        this.CancellationToken = cancellationToken;
    }

    /// <summary>
    /// Gets the driver performing the scroll capture. Use it to run any driver operation from the stop
    /// condition, for example the element visibility check.
    /// </summary>
    public OcuNetDriver Driver { get; }

    /// <summary>
    /// Gets the number of scroll steps already performed. 0 means the capture has not scrolled yet (the
    /// condition is evaluated on the very first frame before any scroll).
    /// </summary>
    public int ScrollStep { get; }

    /// <summary>
    /// Gets the elapsed time since the scroll capture started.
    /// </summary>
    public TimeSpan Elapsed { get; }

    /// <summary>
    /// Gets the captured region in absolute screen coordinates (the attached window bounds).
    /// </summary>
    public Rectangle CaptureRect { get; }

    /// <summary>
    /// Gets the cancellation token of the current scroll capture.
    /// </summary>
    public CancellationToken CancellationToken { get; }
}
