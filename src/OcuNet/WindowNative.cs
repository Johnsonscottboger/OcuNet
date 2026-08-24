using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OcuNet;

/// <summary>
/// Win32 interop helpers for enumerating and activating top-level windows.
/// </summary>
[SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1201:Elements should appear in the correct order", Justification = "Keep interop code close to its usage")]
internal static class WindowNative
{
    /// <summary>
    /// Enumerates all visible top-level windows with a non-empty title, in z-order.
    /// UWP cloaked windows are filtered out.
    /// </summary>
    public static Task<WindowInfo[]> GetWindowsAsync()
    {
        return Task.Run(() => GetWindowsInternal().ToArray());
    }

    /// <summary>
    /// Tries to get the current bounds of a window in absolute screen coordinates.
    /// </summary>
    public static bool TryGetWindowRect(IntPtr windowHandle, out Rectangle bounds)
    {
        if (GetWindowRect(windowHandle, out var rect))
        {
            bounds = new Rectangle(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return true;
        }

        bounds = new Rectangle(0, 0, 0, 0);
        return false;
    }

    /// <summary>
    /// Restores the window if it is minimized, then brings it to the foreground.
    /// A simulated ALT press bypasses the Windows foreground-lock restriction when needed.
    /// </summary>
    public static Task ActivateAsync(IntPtr windowHandle)
    {
        return Task.Run(() => ActivateInternal(windowHandle));
    }

    private static IEnumerable<WindowInfo> GetWindowsInternal()
    {
        var windows = new List<WindowInfo>();

        var callback = new EnumWindowsProc((IntPtr windowHandle, IntPtr _) =>
        {
            if (!IsWindowVisible(windowHandle))
            {
                return 1;
            }

            if (IsCloaked(windowHandle))
            {
                return 1;
            }

            var title = GetWindowTitle(windowHandle);
            if (string.IsNullOrWhiteSpace(title))
            {
                return 1;
            }

            if (TryGetWindowRect(windowHandle, out var bounds))
            {
                windows.Add(new WindowInfo(windowHandle, title, bounds));
            }

            return 1;
        });

        EnumWindows(callback, IntPtr.Zero);
        return windows;
    }

    private static string GetWindowTitle(IntPtr windowHandle)
    {
        var length = GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        var written = GetWindowText(windowHandle, builder, builder.Capacity);
        return written > 0 ? builder.ToString(0, written) : string.Empty;
    }

    private static bool IsCloaked(IntPtr windowHandle)
    {
        try
        {
            return DwmGetWindowAttribute(windowHandle, DwmWindowAttribute.Cloaked, out var cloaked, Marshal.SizeOf<int>()) == 0
                && cloaked != 0;
        }
        catch (DllNotFoundException)
        {
            // DWM is not available (unlikely on supported Windows versions): treat the window as visible.
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static void ActivateInternal(IntPtr windowHandle)
    {
        if (IsIconic(windowHandle))
        {
            ShowWindow(windowHandle, ShowWindowCommand.Restore);
        }

        if (SetForegroundWindow(windowHandle))
        {
            return;
        }

        // Simulate an ALT key press to bypass the foreground-lock restriction, then retry.
        KeybdEvent(VirtualKeyMenu, 0, KeyEventFlag.KeyUp, UIntPtr.Zero);
        KeybdEvent(VirtualKeyMenu, 0, KeyEventFlag.KeyDown, UIntPtr.Zero);
        KeybdEvent(VirtualKeyMenu, 0, KeyEventFlag.KeyUp, UIntPtr.Zero);
        SetForegroundWindow(windowHandle);
    }

    private const byte VirtualKeyMenu = 0x12;

    private enum ShowWindowCommand
    {
        Restore = 9,
    }

    private enum KeyEventFlag : uint
    {
        KeyDown = 0x0000,
        KeyUp = 0x0002,
    }

    private enum DwmWindowAttribute
    {
        Cloaked = 14,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumWindowsProc(IntPtr windowHandle, IntPtr callbackObject);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr callbackObject);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr windowHandle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr windowHandle, ShowWindowCommand command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void KeybdEvent(byte virtualKeyCode, byte scanCode, KeyEventFlag flags, UIntPtr extraInfo);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr windowHandle, DwmWindowAttribute attribute, out int value, int size);
}
