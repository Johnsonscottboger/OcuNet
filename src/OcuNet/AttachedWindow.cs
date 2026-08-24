using System;
using System.Threading.Tasks;

namespace OcuNet;

/// <summary>
/// A window attached to an <see cref="OcuNetDriver"/> through
/// <see cref="OcuNetDriver.AttachWindowAsync(string, TimeSpan?, WindowTitleMatchMode, bool, double)"/>.
/// While attached, every element lookup performed by the driver is constrained to the window's
/// current bounds. Disposing this object detaches the window and restores full-screen lookups.
/// </summary>
public sealed class AttachedWindow : IDisposable
{
    private readonly OcuNetDriver _driver;

    internal AttachedWindow(OcuNetDriver driver, WindowInfo windowInfo)
    {
        this._driver = driver;
        this.Handle = windowInfo.Handle;
        this.Title = windowInfo.Title;
    }

    /// <summary>
    /// Gets the native window handle.
    /// </summary>
    public IntPtr Handle { get; }

    /// <summary>
    /// Gets the window title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets a value indicating whether the window still exists.
    /// </summary>
    public bool IsValid => WindowNative.TryGetWindowRect(this.Handle, out _);

    /// <summary>
    /// Gets the window's current bounds in absolute screen coordinates. The bounds are
    /// refreshed from the operating system on every access, so a moved or resized window
    /// is always followed. Throws <see cref="WindowNotFoundException"/> if the window closed.
    /// </summary>
    public Rectangle Bounds
    {
        get
        {
            if (!WindowNative.TryGetWindowRect(this.Handle, out var bounds))
            {
                throw new WindowNotFoundException(
                    Messages.AttachedWindow_Throw_WindowClosed.FormatInvariant(this.Title),
                    this.Title,
                    Array.Empty<WindowInfo>());
            }

            return bounds;
        }
    }

    /// <summary>
    /// Restores the window if minimized and brings it to the foreground.
    /// </summary>
    public Task ActivateAsync()
    {
        return WindowNative.ActivateAsync(this.Handle);
    }

    /// <summary>
    /// Detaches this window from the driver, restoring full-screen lookups.
    /// </summary>
    public void Dispose()
    {
        if (ReferenceEquals(this._driver.CurrentWindow, this))
        {
            this._driver.DetachWindow();
        }
    }
}
