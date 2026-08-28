using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OcuNet.Tests;

/// <summary>
/// The single-threaded capture loop is deterministic: each frame advances two scroll ticks (300px), so
/// frame counts and the stitched content are exact.
/// </summary>
public class ScrollScreenshotRunnerTests : BaseOcuNetDriverTests
{
    private const int Width = 640;
    private const int ViewportHeight = 480;
    private const int StepPixels = 150;

    [Fact]
    public async Task BottomMode_ScrollsToBottom_StitchesFullContent()
    {
        // Two ticks per capture -> 300px per frame; offsets 0,300,600,900,1120 (clamped at the content
        // bottom), then two identical frames stop the capture.
        var state = new ScrollState { MaxOffset = 1120 };
        var runner = this.CreateRunner(state);
        using var result = await runner.RunAsync(null);

        Assert.Equal(ScrollScreenshotStopReason.BottomReached, result.StopReason);
        Assert.Equal(5, result.ScrollSteps);
        Assert.Equal(5, result.FrameCount);
        Assert.Equal(1600, result.Image.Height);
        Assert.Equal(Width, result.Image.Width);
        Assert.Equal(0, result.TopBandRows);
        Assert.Equal(0, result.BottomBandRows);
        using var expected = ScrollTestUtils.CreateCanvas(Width, 1600, 0);
        ScrollTestUtils.AssertBitmapsEqual(expected, result.Image);
    }

    [Fact]
    public async Task BottomMode_WithStatusBarClock_StillDetectsBottom()
    {
        var state = new ScrollState { MaxOffset = 1120 };
        var clockValue = 0;
        var driver = this.CreateDriver();
        Func<Task<Bitmap>> capture = () => Task.FromResult(
            ScrollTestUtils.CreateFrameWithStatusBar(Width, ViewportHeight, state.Offset, statusBarHeight: 80, clockValue: clockValue++));

        var runner = this.CreateRunner(state, driver, capture);
        using var result = await runner.RunAsync(null);

        Assert.Equal(ScrollScreenshotStopReason.BottomReached, result.StopReason);
        Assert.Equal(5, result.ScrollSteps);
        Assert.Equal(5, result.FrameCount);
        Assert.Equal(1600, result.Image.Height);
        Assert.Equal(80, result.TopBandRows);
        Assert.Equal(0, result.BottomBandRows);
    }

    [Fact]
    public async Task BottomMode_WithLargeAnimatedRegion_StillDetectsBottom()
    {
        // A 35-row fixed-position region changes on every frame (larger than the block-level tolerance of
        // IsSameContent) but stays under the row-change ratio, so the bottom is still detected.
        var state = new ScrollState { MaxOffset = 1120 };
        var animationColor = 0;
        var driver = this.CreateDriver();
        Func<Task<Bitmap>> capture = () =>
        {
            var color = Color.FromArgb(255, (animationColor * 53) % 256, 120, 30);
            var frame = ScrollTestUtils.CreateCanvasWithRect(Width, ViewportHeight, state.Offset, 200, 100, 200, 35, color);
            animationColor++;
            return Task.FromResult(frame);
        };

        var runner = this.CreateRunner(state, driver, capture);
        using var result = await runner.RunAsync(null);

        Assert.Equal(ScrollScreenshotStopReason.BottomReached, result.StopReason);
        Assert.Equal(5, result.ScrollSteps);
        Assert.Equal(5, result.FrameCount);
        Assert.Equal(1600, result.Image.Height);
    }

    [Fact]
    public async Task BottomMode_WithScrollOverlay_StillScrollsToBottom()
    {
        // A fixed-position "scroll percentage" overlay changes a few rows on every frame: it must not stop
        // the capture while content scrolls, and the bottom must still be detected.
        var state = new ScrollState { MaxOffset = 1120 };
        var overlayValue = 0;
        var driver = this.CreateDriver();
        Func<Task<Bitmap>> capture = () =>
        {
            var overlayColor = Color.FromArgb(255, (overlayValue * 37) % 256, 30, 200);
            var frame = ScrollTestUtils.CreateCanvasWithRect(Width, ViewportHeight, state.Offset, 100, 100, 200, 30, overlayColor);
            overlayValue++;
            return Task.FromResult(frame);
        };

        var runner = this.CreateRunner(state, driver, capture);
        using var result = await runner.RunAsync(null);

        Assert.Equal(ScrollScreenshotStopReason.BottomReached, result.StopReason);
        Assert.Equal(5, result.ScrollSteps);
        Assert.Equal(5, result.FrameCount);
        Assert.Equal(1600, result.Image.Height);
    }

    [Fact]
    public async Task BottomMode_WithFixedTopBand_TitleBarStitchedOnce()
    {
        // Window with a fixed 80px title bar: the stitched long image must contain the title bar exactly
        // once (at the top), the content must be complete, and the title-bar rows must not derail the
        // overlap measurement.
        var state = new ScrollState { MaxOffset = 1120 };
        var driver = this.CreateDriver();
        Func<Task<Bitmap>> capture = () => Task.FromResult(
            ScrollTestUtils.CreateCanvasWithFixedTopBand(Width, ViewportHeight, state.Offset, 80));

        var runner = this.CreateRunner(state, driver, capture);
        using var result = await runner.RunAsync(null);

        Assert.Equal(ScrollScreenshotStopReason.BottomReached, result.StopReason);
        Assert.Equal(5, result.ScrollSteps);
        Assert.Equal(5, result.FrameCount);
        Assert.Equal(1600, result.Image.Height);
        Assert.Equal(80, result.TopBandRows);
        using var expected = ScrollTestUtils.CreateCanvasWithFixedTopBand(Width, 1600, 0, 80);
        ScrollTestUtils.AssertBitmapsEqual(expected, result.Image);
    }

    [Fact]
    public async Task ConditionMode_StopsWhenPredicateTrue_AndIncludesTheFrame()
    {
        var state = new ScrollState();
        var runner = this.CreateRunner(state);
        using var result = await runner.RunAsync(async ctx => ctx.ScrollStep >= 3);

        Assert.Equal(ScrollScreenshotStopReason.ConditionMet, result.StopReason);
        Assert.Equal(3, result.ScrollSteps);
        Assert.Equal(4, result.FrameCount);
        Assert.Equal(ViewportHeight + (3 * 2 * StepPixels), result.Image.Height);
    }

    [Fact]
    public async Task ConditionMode_FirstFrameAlreadyMeetsCondition_ReturnsSingleFrame()
    {
        var state = new ScrollState();
        var runner = this.CreateRunner(state);
        using var result = await runner.RunAsync(async ctx => true);

        Assert.Equal(ScrollScreenshotStopReason.ConditionMet, result.StopReason);
        Assert.Equal(0, result.ScrollSteps);
        Assert.Equal(1, result.FrameCount);
        Assert.Equal(ViewportHeight, result.Image.Height);
    }

    [Fact]
    public async Task ConditionMode_StopsAtBottomWithNoProgress()
    {
        // The condition is never met: the capture scrolls to the bottom, then stops after the no-progress
        // tolerance instead of giving up on the first failed step.
        var state = new ScrollState { MaxOffset = 1120 };
        var runner = this.CreateRunner(state);
        using var result = await runner.RunAsync(async ctx => false);

        Assert.Equal(ScrollScreenshotStopReason.NoProgress, result.StopReason);
        Assert.Equal(5, result.ScrollSteps);
        Assert.Equal(5, result.FrameCount);
        Assert.Equal(1600, result.Image.Height);
    }

    [Fact]
    public async Task ConditionMode_EndlessContent_HitsFrameCap()
    {
        // The content never stops producing new frames (no bottom), so the frame cap stops the capture.
        // Small per-frame step (20px) so 200 frames stay memory-friendly.
        var state = new ScrollState();
        var driver = this.CreateDriver();
        var runner = this.CreateRunner(state, driver, scrollTick: () =>
        {
            state.Offset += 10;
            return Task.CompletedTask;
        });
        using var result = await runner.RunAsync(async ctx => false);

        Assert.Equal(ScrollScreenshotStopReason.MaxStepsReached, result.StopReason);
        Assert.Equal(ScrollScreenshotRunner.MaxScrollSteps, result.ScrollSteps);
        Assert.Equal(ScrollScreenshotRunner.MaxScrollSteps + 1, result.FrameCount);
        Assert.Equal(ViewportHeight + (ScrollScreenshotRunner.MaxScrollSteps * 20), result.Image.Height);
    }

    [Fact]
    public async Task ConditionPredicate_CanUseDriverElementVisibility()
    {
        var element = new TextElement("target");
        var recognizeCalls = 0;
        this.ElementRecognizer.AddExpectedResult(element, () =>
        {
            recognizeCalls++;
            if (recognizeCalls <= 2)
            {
                return Task.FromResult(SearchResult.NotFound(element));
            }

            return Task.FromResult(new SearchResult(element, new[] { new Rectangle(10, 10, 30, 20) }));
        });

        var state = new ScrollState();
        var driver = this.CreateDriver();
        var runner = this.CreateRunner(
            state,
            driver,
            capture: () => Task.FromResult(ScrollTestUtils.CreateCanvas(Width, ViewportHeight, state.Offset)));

        using var result = await runner.RunAsync(
            async ctx => await ctx.Driver.IsVisibleAsync(element, TimeSpan.Zero, new Rectangle(0, 0, Width, ViewportHeight)));

        Assert.Equal(ScrollScreenshotStopReason.ConditionMet, result.StopReason);
        Assert.Equal(2, result.ScrollSteps);
        Assert.Equal(3, recognizeCalls);
    }

    [Fact]
    public async Task ScrollToCaptureDelay_WaitsAfterScrollingBeforeCapturing()
    {
        // The app shows transient scroll animations that would cover the content: each frame must wait
        // the configured delay after the last scroll input before capturing.
        var state = new ScrollState { MaxOffset = 1120 };
        var driver = this.CreateDriver();
        var scrollTimes = new System.Collections.Generic.List<long>();
        var captureTimes = new System.Collections.Generic.List<long>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Func<Task<Bitmap>> capture = () =>
        {
            captureTimes.Add(clock.ElapsedMilliseconds);
            return Task.FromResult(ScrollTestUtils.CreateCanvas(Width, ViewportHeight, state.Offset));
        };
        Func<Task> scrollTick = () =>
        {
            scrollTimes.Add(clock.ElapsedMilliseconds);
            state.Offset = Math.Min(state.Offset + StepPixels, state.MaxOffset);
            return Task.CompletedTask;
        };

        var runner = new ScrollScreenshotRunner(
            driver,
            new Rectangle(0, 0, Width, ViewportHeight),
            capture,
            scrollTick,
            CancellationToken.None,
            TimeSpan.FromMilliseconds(120));
        using var result = await runner.RunAsync(null);

        Assert.True(result.FrameCount >= 2, $"frames={result.FrameCount}");
        for (var i = 1; i < captureTimes.Count; i++)
        {
            var lastScroll = 0L;
            foreach (var t in scrollTimes)
            {
                if (t <= captureTimes[i])
                {
                    lastScroll = t;
                }
            }

            Assert.True(captureTimes[i] - lastScroll >= 100, $"frame {i}: capture-lastScroll={captureTimes[i] - lastScroll}ms");
        }
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var state = new ScrollState();
        var runner = this.CreateRunner(state, cancellationToken: new CancellationToken(canceled: true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(null));
    }

    private ScrollScreenshotRunner CreateRunner(
        ScrollState state,
        OcuNetDriver? driver = null,
        Func<Task<Bitmap>>? capture = null,
        Func<Task>? scrollTick = null,
        CancellationToken cancellationToken = default)
    {
        driver ??= this.CreateDriver();
        capture ??= () => Task.FromResult(ScrollTestUtils.CreateCanvas(Width, ViewportHeight, state.Offset));
        scrollTick ??= () =>
        {
            state.Offset = Math.Min(state.Offset + StepPixels, state.MaxOffset);
            return Task.CompletedTask;
        };

        return new ScrollScreenshotRunner(
            driver,
            new Rectangle(0, 0, Width, ViewportHeight),
            capture,
            scrollTick,
            cancellationToken);
    }

    private sealed class ScrollState
    {
        public int Offset;

        public int MaxOffset = int.MaxValue;
    }
}
