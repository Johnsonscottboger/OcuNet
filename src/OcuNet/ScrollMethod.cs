namespace OcuNet;

/// <summary>
/// The input method used to scroll the window during a scroll screenshot.
/// </summary>
public enum ScrollMethod
{
    /// <summary>
    /// Mouse wheel events. The scroll amount per event is controlled by the wheel delta in apps that
    /// scale with it; some legacy apps only react to the full WHEEL_DELTA (120).
    /// </summary>
    Wheel = 0,

    /// <summary>
    /// Down-arrow key presses. Most scrollable apps scroll one line per press, which gives fine-grained,
    /// app-independent control over the scroll speed — a good fallback when the app ignores sub-120 wheel
    /// deltas or scrolls too far per wheel click.
    /// </summary>
    ArrowKeys = 1,
}
