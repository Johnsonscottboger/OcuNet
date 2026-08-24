namespace OcuNet;

/// <summary>
/// Defines how a window title pattern is matched against the titles of the currently open windows.
/// </summary>
public enum WindowTitleMatchMode
{
    /// <summary>
    /// Case-insensitive substring matching (the default).
    /// </summary>
    Substring = 0,

    /// <summary>
    /// Regular expression matching, using <see cref="System.Text.RegularExpressions.RegexOptions.IgnoreCase"/>
    /// and <see cref="System.Text.RegularExpressions.RegexOptions.CultureInvariant"/>.
    /// </summary>
    Regex = 1,

    /// <summary>
    /// Fuzzy matching based on normalized Levenshtein similarity; the best candidate window wins.
    /// </summary>
    Fuzzy = 2,
}
