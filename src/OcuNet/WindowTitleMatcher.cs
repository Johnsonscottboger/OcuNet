using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OcuNet;

/// <summary>
/// Pure window-title matching logic shared by <see cref="OcuNetDriver.AttachWindowAsync(string, TimeSpan?, WindowTitleMatchMode, bool, double)"/>.
/// </summary>
internal static class WindowTitleMatcher
{
    private static readonly Regex WhitespaceRegex = new Regex("\\s+", RegexOptions.Compiled);

    /// <summary>
    /// Tries to compile <paramref name="titlePattern"/> as a regular expression.
    /// </summary>
    public static bool TryCompileRegex(string titlePattern, out Regex regex)
    {
        try
        {
            regex = new Regex(titlePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return true;
        }
        catch (ArgumentException)
        {
            regex = null!;
            return false;
        }
    }

    /// <summary>
    /// Computes the similarity between two titles (0 = completely different, 1 = identical)
    /// after normalizing case and whitespace.
    /// </summary>
    public static double GetSimilarity(string left, string right)
    {
        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);

        if (normalizedLeft == normalizedRight)
        {
            return 1;
        }

        var maxLength = Math.Max(normalizedLeft.Length, normalizedRight.Length);
        if (maxLength == 0)
        {
            return 1;
        }

        var distance = LevenshteinDistance(normalizedLeft, normalizedRight);
        return 1 - (distance / (double)maxLength);
    }

    /// <summary>
    /// Returns the windows whose title matches <paramref name="titlePattern"/>.
    /// In <see cref="WindowTitleMatchMode.Fuzzy"/> mode only the single best candidate is
    /// returned, and only when its similarity is at least <paramref name="fuzzyThreshold"/>.
    /// </summary>
    public static IReadOnlyList<WindowInfo> MatchAll(IReadOnlyList<WindowInfo> windows, string titlePattern, WindowTitleMatchMode matchMode, double fuzzyThreshold)
    {
        switch (matchMode)
        {
            case WindowTitleMatchMode.Substring:
#pragma warning disable CA2249 // IndexOf is required: netstandard2.0 has no Contains(string, StringComparison)
                return windows
                    .Where(window => window.Title.IndexOf(titlePattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToArray();
#pragma warning restore CA2249

            case WindowTitleMatchMode.Regex:
                var regex = new Regex(titlePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                return windows
                    .Where(window => regex.IsMatch(window.Title))
                    .ToArray();

            case WindowTitleMatchMode.Fuzzy:
                var best = windows
                    .OrderByDescending(window => GetSimilarity(titlePattern, window.Title))
                    .FirstOrDefault();
                return best != null && GetSimilarity(titlePattern, best.Title) >= fuzzyThreshold
                    ? new[] { best }
                    : Array.Empty<WindowInfo>();

            default:
                throw new ArgumentOutOfRangeException(nameof(matchMode));
        }
    }

    private static string Normalize(string value)
    {
        return WhitespaceRegex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), " ");
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            var swap = previous;
            previous = current;
            current = swap;
        }

        return previous[right.Length];
    }
}
