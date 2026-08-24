using System;
using System.Collections.Generic;

namespace OcuNet;

/// <summary>
/// Thrown when no window (or an ambiguous set of windows) matches the requested title
/// pattern, or when an attached window is no longer available.
/// </summary>
public sealed class WindowNotFoundException : OcuNetException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WindowNotFoundException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="titlePattern">The title pattern that was searched for.</param>
    /// <param name="candidateWindows">The windows that were considered, for diagnostics.</param>
    public WindowNotFoundException(string message, string titlePattern, IReadOnlyList<WindowInfo> candidateWindows)
        : base(message)
    {
        this.TitlePattern = titlePattern;
        this.CandidateWindows = candidateWindows;
    }

    /// <summary>
    /// Gets the title pattern that was searched for.
    /// </summary>
    public string TitlePattern { get; }

    /// <summary>
    /// Gets the windows that were considered, for diagnostics.
    /// </summary>
    public IReadOnlyList<WindowInfo> CandidateWindows { get; }
}
