using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OcuNet.Tests;

public class WindowAttachIntegrationTests : BaseOcuNetDriverTests
{
    [Fact]
    public async Task Attach_FindsWindowBySubstring_AndDetaches()
    {
        using var testWindow = TestWindow.Create("OcuNet Attach Test App");
        var driver = this.CreateDriver();

        var window = await driver.AttachWindowAsync("Attach Test");

        Assert.NotNull(driver.CurrentWindow);
        Assert.Same(window, driver.CurrentWindow);
        Assert.Equal(testWindow.Title, window.Title);
        Assert.Equal(testWindow.Bounds, window.Bounds);
        Assert.True(window.IsValid);

        await driver.DetachWindowAsync();

        Assert.Null(driver.CurrentWindow);
    }

    [Fact]
    public async Task Attach_UsingStatement_DetachesOnDispose()
    {
        using var testWindow = TestWindow.Create("OcuNet Using Test App");
        var driver = this.CreateDriver();

        using (var window = await driver.AttachWindowAsync("Using Test"))
        {
            Assert.NotNull(window);
            Assert.Same(window, driver.CurrentWindow);
        }

        Assert.Null(driver.CurrentWindow);
    }

    [Fact]
    public async Task Attach_RegexMode_MatchesAnchoredPattern()
    {
        using var testWindow = TestWindow.Create("OcuNet Regex Test App");
        var driver = this.CreateDriver();

        var window = await driver.AttachWindowAsync("^OcuNet.*App$", matchMode: WindowTitleMatchMode.Regex);

        Assert.NotNull(window);
        Assert.Equal(testWindow.Title, window.Title);
    }

    [Fact]
    public async Task Attach_FuzzyMode_HandlesTypo()
    {
        using var testWindow = TestWindow.Create("OcuNet Fuzzy Application");
        var driver = this.CreateDriver();

        var window = await driver.AttachWindowAsync("OcuNet Fuzzy Apllication", matchMode: WindowTitleMatchMode.Fuzzy);

        Assert.NotNull(window);
        Assert.Equal(testWindow.Title, window.Title);
    }

    [Fact]
    public async Task Attach_FuzzyMode_BelowThreshold_Throws()
    {
        using var testWindow = TestWindow.Create("OcuNet Fuzzy Application");
        var driver = this.CreateDriver();

        var exception = await Assert.ThrowsAsync<WindowNotFoundException>(() =>
            driver.AttachWindowAsync("CompletelyDifferent", matchMode: WindowTitleMatchMode.Fuzzy, fuzzyThreshold: 0.9));

        Assert.Contains(exception.CandidateWindows, window => window.Title == testWindow.Title);
    }

    [Fact]
    public async Task Attach_NotMatching_ThrowsWithCandidates()
    {
        using var testWindow = TestWindow.Create("OcuNet Not Found Test");
        var driver = this.CreateDriver();

        var exception = await Assert.ThrowsAsync<WindowNotFoundException>(() =>
            driver.AttachWindowAsync("Definitely Not There"));

        Assert.Equal("Definitely Not There", exception.TitlePattern);
        Assert.NotEmpty(exception.CandidateWindows);
        Assert.Contains(exception.CandidateWindows, window => window.Title == testWindow.Title);
    }

    [Fact]
    public async Task Attach_MultipleMatches_Throws()
    {
        using var firstWindow = TestWindow.Create("OcuNet Multiple Alpha");
        using var secondWindow = TestWindow.Create("OcuNet Multiple Beta");
        var driver = this.CreateDriver();

        var exception = await Assert.ThrowsAsync<WindowNotFoundException>(() =>
            driver.AttachWindowAsync("OcuNet Multiple"));

        Assert.Equal(2, exception.CandidateWindows.Count);
    }

    [Fact]
    public async Task Attach_EmptyPattern_ThrowsArgumentException()
    {
        var driver = this.CreateDriver();

        await Assert.ThrowsAsync<ArgumentException>(() => driver.AttachWindowAsync("  "));
    }

    [Fact]
    public async Task Attach_InvalidRegex_ThrowsArgumentException()
    {
        var driver = this.CreateDriver();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            driver.AttachWindowAsync("[", matchMode: WindowTitleMatchMode.Regex));
    }

    [Fact]
    public async Task Attach_WaitsForWindowToAppear()
    {
        var driver = this.CreateDriver();
        TestWindow? delayedWindow = null;

        try
        {
            var createTask = Task.Run(async () =>
            {
                await Task.Delay(700);
                return TestWindow.Create("OcuNet Delayed Window");
            });

            var window = await driver.AttachWindowAsync("Delayed Window", waitFor: TimeSpan.FromSeconds(10));

            Assert.NotNull(window);
            delayedWindow = await createTask;
        }
        finally
        {
            delayedWindow?.Dispose();
        }
    }

    [Fact]
    public async Task WindowClosed_BoundsThrows_AndIsValidIsFalse()
    {
        var testWindow = TestWindow.Create("OcuNet Closed Window Test");
        var driver = this.CreateDriver();

        var window = await driver.AttachWindowAsync("Closed Window Test");
        testWindow.Dispose();

        Assert.False(window.IsValid);
        await Assert.ThrowsAsync<WindowNotFoundException>(() => Task.FromResult(window.Bounds));
    }

    [Fact]
    public async Task ListWindows_ContainsCreatedWindow()
    {
        using var testWindow = TestWindow.Create("OcuNet List Test App");
        var driver = this.CreateDriver();

        var windows = await driver.ListWindowsAsync();

        Assert.Contains(windows, window => window.Title == testWindow.Title);
    }

    [Fact]
    public async Task SaveWindowScreenshot_WithAttachedWindow_CropsToWindowBounds()
    {
        using var testWindow = TestWindow.Create("OcuNet Screenshot Test App");
        var driver = this.CreateDriver();
        await driver.AttachWindowAsync("Screenshot Test");

        var targetPath = Path.Combine(Path.GetTempPath(), $"ocunet-window-shot-{Guid.NewGuid():N}.png");
        try
        {
            await driver.SaveWindowScreenshotAsync(targetPath);

            using var saved = new Bitmap(targetPath);
            var expectedBounds = testWindow.Bounds;
            Assert.Equal(expectedBounds.Width, saved.Width);
            Assert.Equal(expectedBounds.Height, saved.Height);
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
    public async Task SaveWindowScreenshot_WithoutAttachedWindow_Throws()
    {
        var driver = this.CreateDriver();

        await Assert.ThrowsAsync<WindowNotFoundException>(() =>
            driver.SaveWindowScreenshotAsync(Path.Combine(Path.GetTempPath(), "should-not-exist.png")));
    }

    [Fact]
    public async Task SaveWindowScreenshot_AfterWindowClosed_Throws()
    {
        var testWindow = TestWindow.Create("OcuNet Screenshot Closed Test");
        var driver = this.CreateDriver();
        await driver.AttachWindowAsync("Screenshot Closed");

        testWindow.Dispose();

        await Assert.ThrowsAsync<WindowNotFoundException>(() =>
            driver.SaveWindowScreenshotAsync(Path.Combine(Path.GetTempPath(), "should-not-exist.png")));
    }

    [Fact]
    public async Task EffectiveSearchRectangle_WithoutAttachment_ReturnsExplicitRect()
    {
        var driver = this.CreateDriver();
        var explicitRect = new Rectangle(10, 20, 110, 120);

        var effective = driver.GetEffectiveSearchRectangle(explicitRect);

        Assert.Equal(explicitRect, effective);
    }

    [Fact]
    public async Task EffectiveSearchRectangle_WithAttachedWindow_UsesWindowBounds()
    {
        using var testWindow = TestWindow.Create("OcuNet Scope Rect Test");
        var driver = this.CreateDriver();
        await driver.AttachWindowAsync("Scope Rect");

        var effective = driver.GetEffectiveSearchRectangle(null);

        Assert.Equal(testWindow.Bounds, effective);
    }

    [Fact]
    public async Task EffectiveSearchRectangle_ExplicitRectWinsOverAttachedWindow()
    {
        using var testWindow = TestWindow.Create("OcuNet Scope Rect Override Test");
        var driver = this.CreateDriver();
        await driver.AttachWindowAsync("Scope Rect Override");
        var explicitRect = new Rectangle(1, 2, 3, 4);

        var effective = driver.GetEffectiveSearchRectangle(explicitRect);

        Assert.Equal(explicitRect, effective);
    }

    private sealed class TestWindow : IDisposable
    {
        private const int WindowStyleOverlappedWindow = 0x00CF0000;
        private const int WindowStyleVisible = 0x10000000;
        private const int ShowWindowCommandShow = 5;
        private const uint RemoveMessageFlag = 1;

        private static int _windowCounter;

        private readonly string _className;
        private readonly Thread _thread;
        private volatile bool _disposed;
        private IntPtr _handle;

        private static readonly WindowProcDelegate DefWindowProc = DefWindowProcNative;

        private TestWindow(string className, string title, IntPtr handle, Thread thread)
        {
            this._className = className;
            this.Title = title;
            this._handle = handle;
            this._thread = thread;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WindowProcDelegate(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

        public string Title { get; }

        public Rectangle Bounds
        {
            get
            {
                GetWindowRect(this._handle, out var rect);
                return new Rectangle(rect.Left, rect.Top, rect.Right, rect.Bottom);
            }
        }

        public static TestWindow Create(string title)
        {
            TestWindow? window = null;
            using var ready = new ManualResetEventSlim();

            var thread = new Thread(() =>
            {
                var created = CreateOnCurrentThread(title);
                window = created;
                ready.Set();
                created.MessagePump();
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (!ready.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Timed out creating the test window.");
            }

            return window!;
        }

        public void Dispose()
        {
            this._disposed = true;
            this._thread.Join(TimeSpan.FromSeconds(5));
            UnregisterClass(this._className, GetModuleHandle(null));
        }

        private static TestWindow CreateOnCurrentThread(string title)
        {
            var className = "OcuNetTestWindow" + Interlocked.Increment(ref _windowCounter);
            var instanceHandle = GetModuleHandle(null);
            var windowClass = new WindowClass
            {
                Style = 0,
                WindowProc = DefWindowProc,
                InstanceHandle = instanceHandle,
                ClassName = className,
            };

            if (RegisterClass(ref windowClass) == 0)
            {
                throw new InvalidOperationException($"RegisterClass failed: {Marshal.GetLastWin32Error()}");
            }

            var handle = CreateWindowEx(
                0,
                className,
                title,
                WindowStyleOverlappedWindow | WindowStyleVisible,
                100,
                100,
                300,
                200,
                IntPtr.Zero,
                IntPtr.Zero,
                instanceHandle,
                IntPtr.Zero);

            if (handle == IntPtr.Zero)
            {
                UnregisterClass(className, instanceHandle);
                throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
            }

            ShowWindow(handle, ShowWindowCommandShow);
            return new TestWindow(className, title, handle, Thread.CurrentThread);
        }

        private void MessagePump()
        {
            while (!this._disposed)
            {
                if (PeekMessage(out var message, IntPtr.Zero, 0, 0, RemoveMessageFlag))
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
                else
                {
                    Thread.Sleep(10);
                }
            }

            if (this._handle != IntPtr.Zero)
            {
                DestroyWindow(this._handle);
                this._handle = IntPtr.Zero;
            }
        }

        [DllImport("user32.dll", EntryPoint = "DefWindowProcW", ExactSpelling = true)]
        private static extern IntPtr DefWindowProcNative(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClass(ref WindowClass windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int extendedStyle,
            string className,
            string windowName,
            int style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parentWindow,
            IntPtr menu,
            IntPtr instanceHandle,
            IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr windowHandle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool UnregisterClass(string className, IntPtr instanceHandle);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr windowHandle, out Rect rect);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out Message message, IntPtr windowHandle, uint messageFilterMin, uint messageFilterMax, uint removeMessage);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref Message message);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref Message message);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? moduleName);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            public uint Style;

            [MarshalAs(UnmanagedType.FunctionPtr)]
            public WindowProcDelegate WindowProc;

            public int ClassExtraBytes;

            public int WindowExtraBytes;

            public IntPtr InstanceHandle;

            public IntPtr Icon;

            public IntPtr Cursor;

            public IntPtr BackgroundBrush;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string MenuName;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string ClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;

            public int Top;

            public int Right;

            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Message
        {
            public IntPtr WindowHandle;

            public uint MessageId;

            public IntPtr WParam;

            public IntPtr LParam;

            public uint Time;

            public PointNative Point;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PointNative
        {
            public int X;

            public int Y;
        }
    }
}
