namespace OcuNet;

/// <summary>
/// The reason why a window scroll screenshot stopped.
/// </summary>
public enum ScrollScreenshotStopReason
{
    /// <summary>
    /// The user-provided stop condition returned true (possibly on the very first frame, before any scroll).
    /// </summary>
    ConditionMet = 0,

    /// <summary>
    /// No stop condition was provided and the bottom of the content was detected (the content stopped
    /// producing new frames).
    /// </summary>
    BottomReached = 1,

    /// <summary>
    /// A stop condition was provided but the content stopped producing new frames before it was met.
    /// </summary>
    NoProgress = 2,

    /// <summary>
    /// The maximum number of scroll steps was reached (for example endless feeds). The result image is
    /// partial.
    /// </summary>
    MaxStepsReached = 3,
}
