using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace OcuNet.Tests;

/// <summary>
/// Tests for the standalone <see cref="ScrollScreenshot"/> API: scroll screenshots are a function of the
/// driver, not a driver method — all scroll-control parameters live on the function itself.
/// They use a real window (needed for window attachment) but a fake scrolling monitor and a fake mouse, so
/// the whole capture loop runs deterministically without touching the real screen.
/// </summary>
public class ScrollScreenshotIntegrationTests
{
    private const int PixelsPerWheelTick = 50;
    private const int MaxScrollOffset = 800;
    private const int MonitorWidth = 1920;
    private const int MonitorHeight = 1080;

    private ScrollingMouseController? _mouse;

    [Fact]
    public async Task CaptureAsync_WithoutAttachedWindow_Throws()
    {
        var driver = this.CreateScrollingDriver(new ScrollState());

        await Assert.ThrowsAsync<WindowNotFoundException>(() => ScrollScreenshot.CaptureAsync(driver));
    }

    [Fact]
    public async Task CaptureAsync_ScrollsToBottom_AndStitchesFullContent()
    {
        var state = new ScrollState { MaxOffset = MaxScrollOffset };
        using var testWindow = TestWindow.Create("OcuNet Scroll Capture Test");
        var driver = this.CreateScrollingDriver(state);
        await driver.AttachWindowAsync("Scroll Capture");
        var bounds = testWindow.Bounds;
        var mouse = this.GetMouse();

        using var result = await ScrollScreenshot.CaptureAsync(driver, wheelTickIntervalMs: 0);

        Assert.Equal(ScrollScreenshotStopReason.BottomReached, result.StopReason);
        Assert.Equal(bounds.Width, result.Image.Width);

        // Single-threaded capture: each frame advances two ticks of 25px (delta 60 on the fake app), so
        // the stitched height is exact.
        Assert.Equal(MaxScrollOffset + bounds.Height, result.Image.Height);
        Assert.Equal(0, result.TopBandRows);
        Assert.Equal(0, result.BottomBandRows);

        // The mouse must have been moved to the window center for the wheel events, then restored to its
        // original position (Point.Empty in the fake).
        Assert.Equal("Move(0, 0, Fast)", mouse.Actions[mouse.Actions.Count - 1]);
    }

    [Fact]
    public async Task CaptureAsync_StopsWhenConditionMet()
    {
        var state = new ScrollState();
        using var testWindow = TestWindow.Create("OcuNet Scroll Condition Test");
        var driver = this.CreateScrollingDriver(state);
        await driver.AttachWindowAsync("Scroll Condition");

        using var result = await ScrollScreenshot.CaptureAsync(driver, async ctx => ctx.ScrollStep >= 2);

        Assert.Equal(ScrollScreenshotStopReason.ConditionMet, result.StopReason);
        Assert.InRange(result.ScrollSteps, 2, 20);
    }

    [Fact]
    public async Task CaptureAsync_ArrowKeysMode_ScrollsToBottom()
    {
        // Fallback scroll method for apps that ignore sub-120 wheel deltas or scroll too far per wheel
        // click: Down-arrow presses scroll one line each, giving fine-grained speed control.
        var state = new ScrollState { MaxOffset = MaxScrollOffset };
        using var testWindow = TestWindow.Create("OcuNet Scroll Arrow Keys Test", 100, 100, 400, 400);
        var driver = this.CreateScrollingDriver(state, keyboard: new ScrollingKeyboardController(state));
        await driver.AttachWindowAsync("Scroll Arrow Keys");
        var bounds = testWindow.Bounds;

        using var result = await ScrollScreenshot.CaptureAsync(
            driver,
            scrollMethod: ScrollMethod.ArrowKeys,
            wheelTickIntervalMs: 0);

        Assert.Equal(ScrollScreenshotStopReason.BottomReached, result.StopReason);
        Assert.Equal(MaxScrollOffset + bounds.Height, result.Image.Height);
    }

    [Fact]
    public async Task CaptureAsync_CustomScrollTick_ScrollsToBottom()
    {
        // Fully custom scroll action (e.g. adb swipe for an Android app inside an emulator): the injected
        // action advances the fake content, proving the capture loop uses it for scrolling.
        var state = new ScrollState { MaxOffset = MaxScrollOffset };
        using var testWindow = TestWindow.Create("OcuNet Scroll Custom Tick Test", 100, 100, 400, 400);
        var driver = this.CreateScrollingDriver(state);
        await driver.AttachWindowAsync("Scroll Custom Tick");
        var bounds = testWindow.Bounds;

        using var result = await ScrollScreenshot.CaptureAsync(
            driver,
            customScrollTick: () =>
            {
                state.Offset = Math.Min(state.Offset + 60, state.MaxOffset);
                return Task.CompletedTask;
            });

        Assert.Equal(ScrollScreenshotStopReason.BottomReached, result.StopReason);
        Assert.Equal(MaxScrollOffset + bounds.Height, result.Image.Height);
    }

    [Fact]
    public async Task SaveAsync_WritesPngFile()
    {
        var state = new ScrollState { MaxOffset = MaxScrollOffset };
        using var testWindow = TestWindow.Create("OcuNet Scroll Save File Test");
        var driver = this.CreateScrollingDriver(state);
        await driver.AttachWindowAsync("Scroll Save File");
        var bounds = testWindow.Bounds;

        var targetPath = Path.Combine(Path.GetTempPath(), $"ocunet-scroll-shot-{Guid.NewGuid():N}.png");
        try
        {
            await ScrollScreenshot.SaveAsync(driver, targetPath);

            using var saved = new Bitmap(targetPath);
            Assert.Equal(bounds.Width, saved.Width);
            Assert.Equal(MaxScrollOffset + bounds.Height, saved.Height);
        }
        finally
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
        }
    }

    [Fact]
    public async Task SaveAsync_WritesPngStream()
    {
        var state = new ScrollState { MaxOffset = MaxScrollOffset };
        using var testWindow = TestWindow.Create("OcuNet Scroll Save Stream Test");
        var driver = this.CreateScrollingDriver(state);
        await driver.AttachWindowAsync("Scroll Save Stream");

        using var stream = new MemoryStream();
        await ScrollScreenshot.SaveAsync(driver, stream);

        Assert.True(stream.Length > 0);
    }

    private OcuNetDriver CreateScrollingDriver(ScrollState state, IKeyboardController? keyboard = null)
    {
        this._mouse = new ScrollingMouseController(state);
        return new OcuNetDriver(
            new DriverOptions(),
            new FakeScreenshotWriter(),
            new ScrollingMonitorService(state),
            new FakeElementRecognizer(),
            this._mouse,
            keyboard ?? new FakeKeyboardController());
    }

    private ScrollingMouseController GetMouse()
    {
        return this._mouse ?? throw new InvalidOperationException("Driver not created by CreateScrollingDriver.");
    }

    private sealed class ScrollingKeyboardController : IKeyboardController
    {
        private readonly ScrollState _state;

        public ScrollingKeyboardController(ScrollState state)
        {
            this._state = state;
        }

        public Task TypeText(string text)
        {
            return Task.CompletedTask;
        }

        public Task KeyPress(params VirtualKeyCode[] keyCodes)
        {
            foreach (var key in keyCodes)
            {
                if (key == VirtualKeyCode.DownArrow)
                {
                    // One line per Down press, like most scrollable apps.
                    this._state.Offset = Math.Min(this._state.Offset + 20, this._state.MaxOffset);
                }
            }

            return Task.CompletedTask;
        }

        public Task KeyDown(params VirtualKeyCode[] keyCodes)
        {
            return Task.CompletedTask;
        }

        public Task KeyUp(params VirtualKeyCode[] keyCodes)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ScrollState
    {
        public int Offset;

        public int MaxOffset = int.MaxValue;
    }

    private sealed class ScrollingMonitorService : IMonitorService
    {
        private readonly ScrollState _state;

        public ScrollingMonitorService(ScrollState state)
        {
            this._state = state;
        }

        public Task<MonitorDescription[]> GetMonitors()
        {
            return Task.FromResult(FakeMonitorService.Monitors);
        }

        public Task<MonitorDescription> GetMonitor(int index)
        {
            return Task.FromResult(FakeMonitorService.Monitors[index]);
        }

        public Task<Bitmap> GetScreenshot(MonitorDescription monitor)
        {
            // The whole monitor is rendered from the virtual canvas starting at the current scroll offset,
            // so the window crop shows successive slices of the content as the offset advances.
            return Task.FromResult(ScrollTestUtils.CreateCanvas(MonitorWidth, MonitorHeight, this._state.Offset));
        }
    }

    private sealed class ScrollingMouseController : IMouseController
    {
        private readonly ScrollState _state;
        private readonly List<string> _actions = new List<string>();

        public ScrollingMouseController(ScrollState state)
        {
            this._state = state;
        }

        public IReadOnlyList<string> Actions
        {
            get => this._actions;
        }

        public Point GetCurrentPosition()
        {
            return Point.Empty;
        }

        public Task Move(int x, int y, MouseSpeed speed)
        {
            this._actions.Add($"Move({x}, {y}, {speed})");
            return Task.CompletedTask;
        }

        public async Task WheelDown()
        {
            this._actions.Add("WheelDown()");
            await Task.Delay(100);
        }

        public Task WheelDown(int delta)
        {
            // Scale the delta like a delta-aware app (delta 60 -> half a wheel click).
            this._state.Offset = Math.Min(this._state.Offset + (int)Math.Round(delta / 120.0 * PixelsPerWheelTick), this._state.MaxOffset);
            this._actions.Add($"WheelDown({delta})");
            return Task.CompletedTask;
        }

        public Task SingleClick(int x, int y, MouseSpeed speed) => Task.CompletedTask;

        public Task DoubleClick(int x, int y, MouseSpeed speed) => Task.CompletedTask;

        public Task TripleClick(int x, int y, MouseSpeed speed) => Task.CompletedTask;

        public Task RightClick(int x, int y, MouseSpeed speed) => Task.CompletedTask;

        public Task DragFrom(int x, int y, MouseSpeed speed) => Task.CompletedTask;

        public Task DropTo(int x, int y, MouseSpeed speed) => Task.CompletedTask;

        public Task WheelUp() => Task.CompletedTask;
    }
}
