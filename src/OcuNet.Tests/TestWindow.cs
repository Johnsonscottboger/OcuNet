using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace OcuNet.Tests;

/// <summary>
/// A minimal real Win32 window used by integration tests (window attach, window screenshots, scroll
/// screenshots). The window is created and pumped on a dedicated STA thread.
/// </summary>
internal sealed class TestWindow : IDisposable
{
    private const int WindowStyleOverlappedWindow = 0x00CF0000;
    private const int WindowStyleVisible = 0x10000000;
    private const int ShowWindowCommandShow = 5;
    private const uint RemoveMessageFlag = 1;
    private const uint WmMouseWheel = 0x020A;

    private static readonly ConcurrentDictionary<IntPtr, Action<int>> MouseWheelCallbacks = new();

    private static int _windowCounter;

    private readonly string _className;
    private readonly Thread _thread;
    private volatile bool _disposed;
    private IntPtr _handle;

    private static readonly WindowProcDelegate WindowProc = WindowProcNative;

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
        return Create(title, 100, 100, 300, 200, null);
    }

    public static TestWindow Create(string title, Action<int>? onMouseWheel)
    {
        return Create(title, 100, 100, 300, 200, onMouseWheel);
    }

    public static TestWindow Create(string title, int x, int y, int width, int height, Action<int>? onMouseWheel = null)
    {
        TestWindow? window = null;
        using var ready = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            var created = CreateOnCurrentThread(title, x, y, width, height);
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

        if (onMouseWheel != null)
        {
            MouseWheelCallbacks[window!._handle] = onMouseWheel;
        }

        return window!;
    }

    public void Dispose()
    {
        MouseWheelCallbacks.TryRemove(this._handle, out _);
        this._disposed = true;
        this._thread.Join(TimeSpan.FromSeconds(5));
        UnregisterClass(this._className, GetModuleHandle(null));
    }

    private static IntPtr WindowProcNative(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmMouseWheel && MouseWheelCallbacks.TryGetValue(windowHandle, out var callback))
        {
            var delta = (short)(wParam.ToInt32() & 0xFFFF);
            callback(delta);
            return IntPtr.Zero;
        }

        return DefWindowProcNative(windowHandle, message, wParam, lParam);
    }

    private static TestWindow CreateOnCurrentThread(string title, int x, int y, int width, int height)
    {
        var className = "OcuNetTestWindow" + Interlocked.Increment(ref _windowCounter);
        var instanceHandle = GetModuleHandle(null);
        var windowClass = new WindowClass
        {
            Style = 0,
            WindowProc = WindowProc,
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
            x,
            y,
            width,
            height,
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
