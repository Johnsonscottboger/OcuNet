using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace OcuNet;

/// <summary>
/// Orchestrates the scroll-capture loop on a single thread: it scrolls a few wheel ticks, then captures a
/// frame WHILE the smooth-scroll animation is still running (sampling the motion instead of waiting for it
/// to settle), and repeats until the optional stop condition is met or the content stops producing new
/// frames. The merge phase (ScrollStitcher.StitchAll) then measures the per-pair offsets and stitches the
/// frames into a long image. All inputs (capture and scroll functions) are injected so the loop can be
/// unit-tested without a real screen.
/// </summary>
internal sealed class ScrollScreenshotRunner
{
    /// <summary>
    /// Number of scroll inputs sent between two captures. Sampling during the animation means the content
    /// between two frames is fully covered (no skipped rows), and the per-frame displacement (a few wheel
    /// ticks) stays well above the no-progress change ratio.
    /// </summary>
    public const int ScrollTicksPerCapture = 2;

    /// <summary>
    /// Absolute safety cap on the number of captured frames (endless feeds).
    /// </summary>
    public const int MaxScrollSteps = 200;

    /// <summary>
    /// Number of consecutive no-progress frames required to declare the bottom of the content.
    /// </summary>
    public const int NoProgressTolerance = 2;

    private readonly OcuNetDriver _driver;
    private readonly Rectangle _captureRect;
    private readonly Func<Task<Bitmap>> _capture;
    private readonly Func<Task> _scrollTick;
    private readonly CancellationToken _cancellationToken;
    private readonly TimeSpan _scrollToCaptureDelay;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollScreenshotRunner"/> class.
    /// </summary>
    /// <param name="driver">The driver performing the scroll capture (used to build contexts).</param>
    /// <param name="captureRect">The captured region in absolute screen coordinates.</param>
    /// <param name="capture">Captures one frame of the region.</param>
    /// <param name="scrollTick">Scrolls down by ONE scroll input (the driver paces the inputs).</param>
    /// <param name="cancellationToken">Cancellation token for the capture loop.</param>
    /// <param name="scrollToCaptureDelay">
    /// Delay between the scroll inputs and the frame capture of each sample, so transient scroll
    /// animations (overlays, indicators) settle before the screenshot. Defaults to none.
    /// </param>
    public ScrollScreenshotRunner(
        OcuNetDriver driver,
        Rectangle captureRect,
        Func<Task<Bitmap>> capture,
        Func<Task> scrollTick,
        CancellationToken cancellationToken,
        TimeSpan? scrollToCaptureDelay = null)
    {
        this._driver = driver;
        this._captureRect = captureRect;
        this._capture = capture;
        this._scrollTick = scrollTick;
        this._cancellationToken = cancellationToken;
        this._scrollToCaptureDelay = scrollToCaptureDelay ?? TimeSpan.Zero;
    }

    /// <summary>
    /// Runs the scroll-capture loop. When <paramref name="stopCondition"/> is null, the loop scrolls until
    /// the content stops producing new frames (bottom reached). Otherwise it stops as soon as the
    /// condition returns true (it is also evaluated on the very first frame before any scroll), or when the
    /// content stops producing new frames before the condition is met.
    /// </summary>
    /// <param name="stopCondition">Optional user-defined stop condition.</param>
    public async Task<ScrollScreenshotResult> RunAsync(Func<ScrollScreenshotContext, Task<bool>>? stopCondition)
    {
        var frames = new List<Bitmap>();
        var stopwatch = Stopwatch.StartNew();
        Bitmap? pendingFrame = null;

        try
        {
            var previous = await this._capture().ConfigureAwait(false);
            frames.Add(previous);

            if (stopCondition != null)
            {
                var initialContext = this.MakeContext(0, stopwatch);
                if (await stopCondition(initialContext).ConfigureAwait(false))
                {
                    return this.CreateResult(frames, 0, ScrollScreenshotStopReason.ConditionMet, stopwatch);
                }
            }

            var noProgressCount = 0;

            for (var step = 1; step <= MaxScrollSteps; step++)
            {
                this._cancellationToken.ThrowIfCancellationRequested();
                for (var tick = 0; tick < ScrollTicksPerCapture; tick++)
                {
                    await this._scrollTick().ConfigureAwait(false);
                }

                // Wait for transient scroll animations (overlays, indicators) to settle before capturing.
                if (this._scrollToCaptureDelay > TimeSpan.Zero)
                {
                    await Task.Delay(this._scrollToCaptureDelay, this._cancellationToken).ConfigureAwait(false);
                }

                // Capture while the smooth-scroll animation is still running: the frame is a snapshot of
                // an intermediate position, which keeps the motion continuous and covers every row.
                var frame = await this._capture().ConfigureAwait(false);
                pendingFrame = frame;
                this._cancellationToken.ThrowIfCancellationRequested();

                var isSameContent = ScrollStitcher.IsSameContent(previous, frame);
                var hasNewContent = !isSameContent
                    && ScrollStitcher.CountChangedRows(previous, frame) > (int)(frame.Height * ScrollStitcher.MaxChangedRowRatio);

                if (stopCondition == null)
                {
                    // Bottom mode: stop when the content stops producing new frames.
                    noProgressCount = hasNewContent ? 0 : noProgressCount + 1;
                    if (noProgressCount >= NoProgressTolerance)
                    {
                        frame.Dispose();
                        pendingFrame = null;
                        return this.CreateResult(frames, step - 1, ScrollScreenshotStopReason.BottomReached, stopwatch);
                    }
                }
                else
                {
                    // Condition mode: same consecutive no-progress tolerance as the bottom mode, so local
                    // dynamics (overlays) cannot abort the capture. The condition is only evaluated when
                    // the content actually moved (an unchanged frame would give the same result).
                    noProgressCount = hasNewContent ? 0 : noProgressCount + 1;
                    if (noProgressCount >= NoProgressTolerance)
                    {
                        frame.Dispose();
                        pendingFrame = null;
                        return this.CreateResult(frames, step - 1, ScrollScreenshotStopReason.NoProgress, stopwatch);
                    }
                }

                if (hasNewContent)
                {
                    if (stopCondition != null)
                    {
                        var context = this.MakeContext(step, stopwatch);
                        if (await stopCondition(context).ConfigureAwait(false))
                        {
                            frames.Add(frame);
                            pendingFrame = null;
                            return this.CreateResult(frames, step, ScrollScreenshotStopReason.ConditionMet, stopwatch);
                        }
                    }

                    frames.Add(frame);
                    pendingFrame = null;
                    previous = frame;
                }
                else
                {
                    // No new content this step: the frame is not kept (it would duplicate rows) and the
                    // loop continues until the no-progress tolerance is reached.
                    frame.Dispose();
                    pendingFrame = null;
                }
            }

            return this.CreateResult(frames, MaxScrollSteps, ScrollScreenshotStopReason.MaxStepsReached, stopwatch);
        }
        catch
        {
            // Exception paths (cancellation, capture/scroll failures, condition exceptions) must not leak
            // the bitmaps captured so far. Disposing twice is harmless (Bitmap.Dispose is idempotent).
            pendingFrame?.Dispose();
            foreach (var frame in frames)
            {
                frame.Dispose();
            }

            throw;
        }
    }

    private ScrollScreenshotContext MakeContext(int scrollStep, Stopwatch stopwatch)
    {
        return new ScrollScreenshotContext(this._driver, scrollStep, stopwatch.Elapsed, this._captureRect, this._cancellationToken);
    }

    private ScrollScreenshotResult CreateResult(
        IReadOnlyList<Bitmap> frames,
        int scrollSteps,
        ScrollScreenshotStopReason stopReason,
        Stopwatch stopwatch)
    {
        Bitmap image;
        (int TopRows, int BottomRows) bands;
        try
        {
            bands = ScrollStitcher.DetectStaticBands(frames);
            image = ScrollStitcher.StitchAll(frames, bands.TopRows, bands.BottomRows);
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }

        return new ScrollScreenshotResult(image, frames.Count, scrollSteps, stopReason, stopwatch.Elapsed, this._captureRect, bands.TopRows, bands.BottomRows);
    }
}
