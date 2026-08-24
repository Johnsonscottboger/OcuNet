using System;

namespace OcuNet;

/// <summary>
/// Describes a top-level window found on the desktop. This is a snapshot: the
/// <see cref="Bounds"/> reflect the window position at enumeration time.
/// </summary>
public sealed class WindowInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WindowInfo"/> class.
    /// </summary>
    /// <param name="handle">The native window handle.</param>
    /// <param name="title">The window title.</param>
    /// <param name="bounds">The window bounds in absolute screen coordinates.</param>
    public WindowInfo(IntPtr handle, string title, Rectangle bounds)
    {
        this.Handle = handle;
        this.Title = title;
        this.Bounds = bounds;
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
    /// Gets the window bounds in absolute screen coordinates.
    /// </summary>
    public Rectangle Bounds { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return this.Title;
    }
}
