using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OcuNet;

/// <summary>
/// Scroll screenshots as a standalone API: scrolls the attached window and stitches the visible content
/// into a long image. All scroll-control parameters live on these functions; the driver stays a plain
/// input/recognition core.
/// </summary>
public static class ScrollScreenshot
{
    /// <summary>
    /// Default wheel delta sent per scroll tick. Smaller deltas scroll less per tick in apps that scale
    /// with the delta (browsers, modern UIs); some legacy apps only react to the full WHEEL_DELTA (120).
    /// </summary>
    internal const int DefaultWheelDelta = 60;

    /// <summary>
    /// Default pause between two scroll inputs, in milliseconds. A longer pause slows the scrolling and
    /// prevents apps from coalescing rapid events into large jumps.
    /// </summary>
    internal const int DefaultWheelTickIntervalMs = 120;

    /// <summary>
    /// Scrolls the attached window (see <see cref="OcuNetDriver.AttachWindowAsync(string, TimeSpan?, OcuNet.WindowTitleMatchMode, bool, double)"/>)
    /// and stitches the visible content into a long image. The window content is captured frame by frame
    /// while it scrolls; consecutive frames are stitched together using their measured overlap.
    /// </summary>
    /// <remarks>
    /// Scrolling stops when the user-provided <paramref name="stopCondition"/> returns true (evaluated before
    /// any scroll and after every captured frame; the frame in which it is met is included in the result), or,
    /// when no condition is provided, when the content stops producing new frames (bottom reached).
    /// Fixed rows such as status bars or sticky headers are handled automatically and reported through
    /// <see cref="ScrollScreenshotResult.TopBandRows"/> / <see cref="ScrollScreenshotResult.BottomBandRows"/>.
    /// The mouse is moved to the center of the window for wheel events and restored afterwards.
    /// Throws <see cref="WindowNotFoundException"/> when no window is attached.
    /// </remarks>
    /// <param name="driver">The driver whose attached window will be captured.</param>
    /// <param name="stopCondition">
    /// Optional user-defined stop condition receiving a <see cref="ScrollScreenshotContext"/>. Null scrolls
    /// to the bottom of the content.
    /// </param>
    /// <param name="wheelDelta">
    /// Wheel delta sent per tick (1..120, default <see cref="DefaultWheelDelta"/>), used in
    /// <see cref="ScrollMethod.Wheel"/> mode. Lower values scroll less per tick in delta-aware apps; apps
    /// that ignore sub-120 deltas need 120 (full wheel click).
    /// </param>
    /// <param name="wheelTickIntervalMs">
    /// Pause between two scroll inputs in milliseconds (default <see cref="DefaultWheelTickIntervalMs"/>).
    /// Increase it to slow the scrolling and stop apps from coalescing rapid inputs into large jumps.
    /// </param>
    /// <param name="scrollToCaptureDelayMs">
    /// Delay in milliseconds between the scroll inputs and the frame capture of each sample (default 0).
    /// Increase it when the app shows transient scroll animations (overlays, scroll indicators) that would
    /// otherwise cover the content in the captured frames.
    /// </param>
    /// <param name="scrollMethod">
    /// The input method used to scroll (default <see cref="ScrollMethod.Wheel"/>). Use
    /// <see cref="ScrollMethod.ArrowKeys"/> when the app ignores sub-120 wheel deltas or scrolls too far per
    /// wheel click: Down-arrow presses scroll one line each in most apps.
    /// </param>
    /// <param name="customScrollTick">
    /// Optional fully custom scroll action invoked once per capture sample. When provided it takes precedence
    /// over <paramref name="scrollMethod"/> — use it for environments that need their own input channel.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the capture loop.</param>
    public static async Task<ScrollScreenshotResult> CaptureAsync(
        OcuNetDriver driver,
        Func<ScrollScreenshotContext, Task<bool>>? stopCondition = null,
        int wheelDelta = DefaultWheelDelta,
        int wheelTickIntervalMs = DefaultWheelTickIntervalMs,
        int scrollToCaptureDelayMs = 0,
        ScrollMethod scrollMethod = ScrollMethod.Wheel,
        Func<Task>? customScrollTick = null,
        CancellationToken cancellationToken = default)
    {
        if (driver == null)
        {
            throw new ArgumentNullException(nameof(driver));
        }

        if (wheelDelta <= 0 || wheelDelta > MouseInterop.WheelDelta)
        {
            throw new ArgumentOutOfRangeException(nameof(wheelDelta));
        }

        if (wheelTickIntervalMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wheelTickIntervalMs));
        }

        if (scrollToCaptureDelayMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scrollToCaptureDelayMs));
        }

        var window = driver.CurrentWindow;
        if (window == null)
        {
            throw new WindowNotFoundException(
                Messages.OcuNetDriver_Throw_NoAttachedWindowForScreenshot,
                string.Empty,
                Array.Empty<WindowInfo>());
        }

        var captureRect = window.Bounds;
        await window.ActivateAsync().ConfigureAwait(false);

        var originalPosition = driver.GetMousePositionAsync();
        try
        {
            var anchor = captureRect.Center;
            await driver.MoveToAsync(anchor.X, anchor.Y).ConfigureAwait(false);

            Func<Task<Bitmap>> capture = () => driver.CaptureAttachedWindowAsync();
            Func<Task> scrollTick = customScrollTick ?? (scrollMethod == ScrollMethod.Wheel
                ? () => driver.ScrollDownByDeltaAsync(wheelDelta, wheelTickIntervalMs)
                : () => driver.ScrollDownByArrowKeysAsync(wheelTickIntervalMs));
            var runner = new ScrollScreenshotRunner(
                driver,
                captureRect,
                capture,
                scrollTick,
                cancellationToken,
                TimeSpan.FromMilliseconds(scrollToCaptureDelayMs));
            return await runner.RunAsync(stopCondition).ConfigureAwait(false);
        }
        finally
        {
            await driver.MoveToAsync(originalPosition.X, originalPosition.Y).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Saves the result of <see cref="CaptureAsync(OcuNetDriver, Func{ScrollScreenshotContext, Task{bool}}, int, int, int, ScrollMethod, Func{Task}, CancellationToken)"/>
    /// as a PNG image to <paramref name="destinationPath"/>.
    /// </summary>
#pragma warning disable RS0026 // The first parameter (string vs Stream) fully disambiguates these two overloads.
    public static async Task SaveAsync(
        OcuNetDriver driver,
        string destinationPath,
        Func<ScrollScreenshotContext, Task<bool>>? stopCondition = null,
        int wheelDelta = DefaultWheelDelta,
        int wheelTickIntervalMs = DefaultWheelTickIntervalMs,
        int scrollToCaptureDelayMs = 0,
        ScrollMethod scrollMethod = ScrollMethod.Wheel,
        Func<Task>? customScrollTick = null,
        CancellationToken cancellationToken = default)
    {
        using var result = await CaptureAsync(driver, stopCondition, wheelDelta, wheelTickIntervalMs, scrollToCaptureDelayMs, scrollMethod, customScrollTick, cancellationToken).ConfigureAwait(false);
        result.Image.Save(destinationPath, ImageFormat.Png);
    }

    /// <summary>
    /// Saves the result of <see cref="CaptureAsync(OcuNetDriver, Func{ScrollScreenshotContext, Task{bool}}, int, int, int, ScrollMethod, Func{Task}, CancellationToken)"/>
    /// as a PNG image to <paramref name="destinationStream"/>.
    /// </summary>
    public static async Task SaveAsync(
        OcuNetDriver driver,
        Stream destinationStream,
        Func<ScrollScreenshotContext, Task<bool>>? stopCondition = null,
        int wheelDelta = DefaultWheelDelta,
        int wheelTickIntervalMs = DefaultWheelTickIntervalMs,
        int scrollToCaptureDelayMs = 0,
        ScrollMethod scrollMethod = ScrollMethod.Wheel,
        Func<Task>? customScrollTick = null,
        CancellationToken cancellationToken = default)
    {
        using var result = await CaptureAsync(driver, stopCondition, wheelDelta, wheelTickIntervalMs, scrollToCaptureDelayMs, scrollMethod, customScrollTick, cancellationToken).ConfigureAwait(false);
        result.Image.Save(destinationStream, ImageFormat.Png);
    }
#pragma warning restore RS0026
}
