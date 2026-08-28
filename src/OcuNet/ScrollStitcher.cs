using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OcuNet;

/// <summary>
/// Pure pixel-level algorithms used by scroll screenshots: frame similarity detection, overlap offset
/// measurement, static band detection and stitching. No driver dependency, fully unit-testable.
/// </summary>
internal static class ScrollStitcher
{
    /// <summary>
    /// Smallest overlap (in rows) considered when searching for the offset between two frames.
    /// </summary>
    public const int MinOverlapPixels = 24;

    /// <summary>
    /// A row is considered matching another row when at least this fraction of its sampled pixels match.
    /// </summary>
    public const double RowMatchThreshold = 0.90;

    /// <summary>
    /// An overlap offset is accepted when at least this fraction of the rows that carry alignment
    /// information (rows matching at exactly one offset) match at that offset. The floor is relative to
    /// those informative rows so blank/similar rows — which match at every offset — cannot push a valid
    /// offset below the acceptance threshold, and coincidental matches at wrong offsets stay far below it.
    /// </summary>
    public const double MinMatchFraction = 0.20;

    /// <summary>
    /// The best overlap offset must beat every other candidate by at least this factor, unless the expected
    /// overlap disambiguates.
    /// </summary>
    public const double MatchDominance = 1.25;

    /// <summary>
    /// Columns are sampled every N pixels when comparing rows.
    /// </summary>
    public const int PixelColumnStride = 8;

    /// <summary>
    /// Two pixels are considered equal when the sum of their channel differences is at most this value.
    /// </summary>
    public const int PixelToleranceSum = 30;

    /// <summary>
    /// Side of the square blocks used by the frame similarity detection.
    /// </summary>
    public const int BlockSize = 32;

    /// <summary>
    /// A block is considered changed when more than this fraction of its pixels differ.
    /// </summary>
    public const double BlockChangeThreshold = 0.10;

    /// <summary>
    /// Two frames are considered the same when at most this fraction of their blocks changed.
    /// </summary>
    public const double ChangedBlockRatio = 0.01;

    /// <summary>
    /// A row is considered fixed (status bar, sticky header, etc.) when it is static in at least this
    /// fraction of the consecutive frame pairs.
    /// </summary>
    public const double StaticBandMajority = 0.50;

    /// <summary>
    /// During capture, a step is considered "no progress" when at most this fraction of the frame rows
    /// changed at the same screen position (local dynamics such as a scroll-percentage overlay, clock or
    /// cursor stay below it; actual scrolling changes far more rows).
    /// </summary>
    public const double MaxChangedRowRatio = 0.12;

    /// <summary>
    /// Returns true when <paramref name="a"/> and <paramref name="b"/> look like the same frame. The
    /// comparison is block-based so small dynamic regions (clock, cursor, minor animations) do not
    /// prevent the frames from being considered identical.
    /// </summary>
    public static bool IsSameContent(Bitmap a, Bitmap b)
    {
        if (a == null || b == null || a.Width != b.Width || a.Height != b.Height || a.Width <= 0 || a.Height <= 0)
        {
            return false;
        }

        var width = a.Width;
        var height = a.Height;
        var aPixels = ToArgbBytes(a, out var aStride);
        var bPixels = ToArgbBytes(b, out var bStride);

        var blocksX = (width + BlockSize - 1) / BlockSize;
        var blocksY = (height + BlockSize - 1) / BlockSize;
        var totalBlocks = blocksX * blocksY;
        var maxChangedBlocks = (int)Math.Ceiling(totalBlocks * ChangedBlockRatio);
        var maxChangedPixelsPerBlock = (int)Math.Ceiling(BlockSize * BlockSize * BlockChangeThreshold);

        var changedBlocks = 0;
        for (var blockY = 0; blockY < blocksY; blockY++)
        {
            for (var blockX = 0; blockX < blocksX; blockX++)
            {
                if (IsBlockChanged(
                    aPixels,
                    aStride,
                    bPixels,
                    bStride,
                    width,
                    height,
                    blockX * BlockSize,
                    blockY * BlockSize,
                    maxChangedPixelsPerBlock))
                {
                    changedBlocks++;
                    if (changedBlocks > maxChangedBlocks)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Measures the number of rows shared by <paramref name="upper"/> (old frame, on top) and
    /// <paramref name="lower"/> (new frame, below). The measured offset is robust to fixed bands (status
    /// bar, sticky header/footer) and to real-page content: only rows that match at exactly ONE offset
    /// carry alignment information (blank or visually similar rows match at many offsets and are
    /// excluded), so the argmax is driven by the actual content. Returns null when no reliable overlap
    /// could be measured.
    /// </summary>
    /// <param name="upper">The older frame.</param>
    /// <param name="lower">The newer frame.</param>
    /// <param name="expectedOverlap">
    /// Optional previously measured overlap used to disambiguate close candidates (periodic content).
    /// </param>
    /// <param name="ignoreTopRows">
    /// Fixed rows at the top of the frames (window title bar, sticky header) to exclude from the match:
    /// they match at the no-scroll offset only and would compete with the real content alignment.
    /// </param>
    /// <param name="ignoreBottomRows">Fixed rows at the bottom of the frames to exclude from the match.</param>
    public static int? FindOverlap(Bitmap upper, Bitmap lower, int? expectedOverlap = null, int ignoreTopRows = 0, int ignoreBottomRows = 0)
    {
        if (upper == null || lower == null || upper.Width != lower.Width || upper.Height != lower.Height
            || upper.Width <= 0 || upper.Height <= 0)
        {
            return null;
        }

        var width = upper.Width;
        var height = upper.Height;
        var upperPixels = ToArgbBytes(upper, out var upperStride);
        var lowerPixels = ToArgbBytes(lower, out var lowerStride);

        var minOverlap = Math.Min(MinOverlapPixels, Math.Max(1, height - 1));
        var maxOverlap = height;
        if (minOverlap > maxOverlap)
        {
            return null;
        }

        ignoreTopRows = Math.Max(0, Math.Min(height - 1, ignoreTopRows));
        ignoreBottomRows = Math.Max(0, Math.Min(height - 1, ignoreBottomRows));

        // Full scan with step 1: the row-match peak is a single point (one row of misalignment and no row
        // matches anymore), so any sub-sampling could miss it entirely. Record, for every lower row, at
        // how many offsets it matches (a row matching at several offsets is blank/similar and carries no
        // alignment information).
        var rowMatchCounts = new int[height];
        var matchedPairs = new List<(int D, int Y)>(Math.Min(64 * height, height * height));
        for (var d = maxOverlap; d >= minOverlap; d--)
        {
            for (var y = ignoreTopRows; y < d - ignoreBottomRows; y++)
            {
                if (RowsMatch(upperPixels, upperStride, height - d + y, lowerPixels, lowerStride, y, width))
                {
                    rowMatchCounts[y]++;
                    matchedPairs.Add((d, y));
                }
            }
        }

        if (matchedPairs.Count == 0)
        {
            return null;
        }

        // Unique-match counts: a row counts only when it matches at exactly one offset.
        var uniqueRows = 0;
        for (var y = 0; y < height; y++)
        {
            if (rowMatchCounts[y] == 1)
            {
                uniqueRows++;
            }
        }

        if (uniqueRows == 0)
        {
            return null; // Nothing carries alignment information (fully blank or fully periodic content).
        }

        var uniqueByOffset = new int[height + 1];
        foreach (var (d, y) in matchedPairs)
        {
            if (rowMatchCounts[y] == 1)
            {
                uniqueByOffset[d]++;
            }
        }

        var best = (D: 0, Count: -1);
        for (var d = minOverlap; d <= maxOverlap; d++)
        {
            if (uniqueByOffset[d] > best.Count)
            {
                best = (d, uniqueByOffset[d]);
            }
        }

        var minAccept = Math.Max(8, (int)Math.Ceiling(uniqueRows * MinMatchFraction));
        if (best.Count < minAccept)
        {
            // Almost no row carries alignment information: there is genuinely no measurable overlap
            // (or the content did not move at all). Only this case reports "no progress".
            return null;
        }

        // The unique-match count makes wrong offsets score ~0, so the best candidate usually dominates.
        // When several candidates are close (repeated content), prefer the strongest one closest to the
        // previously measured overlap instead of giving up — a slightly off stitch is preferable to a
        // one-screen result.
        var contenders = new List<(int D, int Count)>();
        for (var d = minOverlap; d <= maxOverlap; d++)
        {
            if (uniqueByOffset[d] * MatchDominance >= best.Count)
            {
                contenders.Add((d, uniqueByOffset[d]));
            }
        }

        if (contenders.Count == 1)
        {
            return contenders[0].D;
        }

        if (expectedOverlap != null)
        {
            var nearest = 0;
            var nearestDistance = int.MaxValue;
            foreach (var contender in contenders)
            {
                var distance = Math.Abs(contender.D - expectedOverlap.Value);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = contender.D;
                }
            }

            return nearest;
        }

        return best.D;
    }

    /// <summary>
    /// Returns the number of rows that look different at the same screen position in <paramref name="a"/>
    /// and <paramref name="b"/> (same-position row comparison). Used during capture to decide whether the
    /// content actually moved: local dynamics (scroll-percentage overlays, clocks, cursors) change few
    /// rows, real scrolling changes most rows.
    /// </summary>
    public static int CountChangedRows(Bitmap a, Bitmap b)
    {
        if (a == null || b == null || a.Width != b.Width || a.Height != b.Height || a.Width <= 0 || a.Height <= 0)
        {
            return a?.Height ?? 0;
        }

        var width = a.Width;
        var height = a.Height;
        var aPixels = ToArgbBytes(a, out var aStride);
        var bPixels = ToArgbBytes(b, out var bStride);

        var changed = 0;
        for (var y = 0; y < height; y++)
        {
            if (!RowsMatch(aPixels, aStride, y, bPixels, bStride, y, width))
            {
                changed++;
            }
        }

        return changed;
    }

    /// <summary>
    /// Stitches the captured frames into a single long image, analyzing the whole sequence together:
    /// every consecutive pair is measured with the fixed bands excluded, then the measured scroll amounts
    /// are validated against their median (failed or outlier measurements are repaired by interpolation)
    /// and the frames are stitched with the fixed top band kept only on the first frame.
    /// </summary>
    /// <param name="frames">The captured frames, in order.</param>
    /// <param name="topBandRows">Fixed rows at the top (window title bar / sticky header), from <see cref="DetectStaticBands"/>.</param>
    /// <param name="bottomBandRows">Fixed rows at the bottom (sticky footer), from <see cref="DetectStaticBands"/>.</param>
    public static Bitmap StitchAll(IReadOnlyList<Bitmap> frames, int topBandRows, int bottomBandRows)
    {
        if (frames == null || frames.Count == 0)
        {
            throw new ArgumentException("At least one frame is required.", nameof(frames));
        }

        var height = frames[0].Height;
        var overlaps = new int[frames.Count - 1];
        var scrollAmounts = new double[frames.Count - 1];
        var measured = new bool[frames.Count - 1];
        int? expectedOverlap = null;

        for (var i = 1; i < frames.Count; i++)
        {
            var overlap = FindOverlap(frames[i - 1], frames[i], expectedOverlap, topBandRows, bottomBandRows);
            if (overlap != null && overlap < height)
            {
                overlaps[i - 1] = overlap.Value;
                scrollAmounts[i - 1] = height - overlap.Value;
                measured[i - 1] = true;
                expectedOverlap = overlap;
            }
        }

        // Global consistency: the per-step scroll amounts are close to constant (the wheel sends the
        // same number of ticks every step). Failed measurements are repaired with the median; isolated
        // outliers between two sane neighbours (a single bad pair) are smoothed with the neighbours'
        // mean. Boundary values are kept as measured — a legitimate last step may be shorter (e.g. the
        // content bottom clamping the scroll).
        var validAmounts = new List<double>();
        for (var i = 0; i < scrollAmounts.Length; i++)
        {
            if (measured[i])
            {
                validAmounts.Add(scrollAmounts[i]);
            }
        }

        double median = 0;
        if (validAmounts.Count > 0)
        {
            validAmounts.Sort();
            median = validAmounts[validAmounts.Count / 2];
            for (var i = 0; i < overlaps.Length; i++)
            {
                if (!measured[i])
                {
                    overlaps[i] = height - (int)Math.Round(median);
                }
            }
        }
        else
        {
            var fallback = Math.Max(1, height / 4);
            for (var i = 0; i < overlaps.Length; i++)
            {
                overlaps[i] = height - fallback;
            }

            return Stitch(frames, overlaps, topBandRows);
        }

        for (var i = 1; i < overlaps.Length - 1; i++)
        {
            var previousAmount = scrollAmounts[i - 1];
            var nextAmount = scrollAmounts[i + 1];
            var isOutlier = Math.Abs(scrollAmounts[i] - previousAmount) > Math.Max(30, previousAmount * 0.5)
                         && Math.Abs(scrollAmounts[i] - nextAmount) > Math.Max(30, nextAmount * 0.5);
            if (isOutlier)
            {
                overlaps[i] = height - (int)Math.Round((previousAmount + nextAmount) / 2);
            }
        }

        return Stitch(frames, overlaps, topBandRows);
    }

    /// <summary>
    /// Detects the fixed rows at the top and bottom of the captured frames (status bar, sticky header or
    /// footer). A row is fixed when it looks identical at the same screen position in the majority of the
    /// consecutive frame pairs, so frames captured after the content stopped moving do not pollute the
    /// result.
    /// </summary>
    public static (int TopRows, int BottomRows) DetectStaticBands(IReadOnlyList<Bitmap> frames)
    {
        if (frames == null || frames.Count < 2)
        {
            return (0, 0);
        }

        var width = frames[0].Width;
        var height = frames[0].Height;
        if (width <= 0 || height <= 0)
        {
            return (0, 0);
        }

        var pairs = frames.Count - 1;
        var required = (int)Math.Ceiling(pairs * StaticBandMajority);
        var staticCounts = new int[height];

        for (var i = 0; i < pairs; i++)
        {
            if (frames[i].Width != width || frames[i].Height != height)
            {
                return (0, 0);
            }

            var upperPixels = ToArgbBytes(frames[i], out var upperStride);
            var lowerPixels = ToArgbBytes(frames[i + 1], out var lowerStride);
            for (var y = 0; y < height; y++)
            {
                if (RowsMatch(upperPixels, upperStride, y, lowerPixels, lowerStride, y, width))
                {
                    staticCounts[y]++;
                }
            }
        }

        var topRows = 0;
        while (topRows < height && staticCounts[topRows] >= required)
        {
            topRows++;
        }

        var bottomRows = 0;
        while (bottomRows < height && staticCounts[height - 1 - bottomRows] >= required)
        {
            bottomRows++;
        }

        return (topRows, bottomRows);
    }

    /// <summary>
    /// Stitches <paramref name="frames"/> into a single long image. <paramref name="overlaps"/>[i] is the
    /// measured overlap (in rows) between frames[i] and frames[i + 1].
    /// </summary>
    /// <param name="frames">The frames to stitch, in capture order.</param>
    /// <param name="overlaps">The measured overlap between each consecutive pair of frames.</param>
    /// <param name="ignoreTopRows">
    /// Fixed rows at the top of the frames (window title bar, sticky header). They are kept only on the
    /// first frame: subsequent frames are stitched from this row down, so the title bar appears once at
    /// the top of the long image instead of repeating at every seam.
    /// </param>
    public static Bitmap Stitch(IReadOnlyList<Bitmap> frames, IReadOnlyList<int> overlaps, int ignoreTopRows = 0)
    {
        if (frames == null || frames.Count == 0)
        {
            throw new ArgumentException("At least one frame is required.", nameof(frames));
        }

        if (overlaps == null || overlaps.Count != frames.Count - 1)
        {
            throw new ArgumentException("The number of overlaps must be frames.Count - 1.", nameof(overlaps));
        }

        var width = frames[0].Width;
        var height = frames[0].Height;
        ignoreTopRows = Math.Max(0, Math.Min(height - 1, ignoreTopRows));
        var totalHeight = height;
        foreach (var overlap in overlaps)
        {
            if (overlap < 0 || overlap > height)
            {
                throw new ArgumentOutOfRangeException(nameof(overlaps), $"Overlap {overlap} is outside [0, {height}].");
            }

            totalHeight += height - overlap;
        }

        var canvas = new Bitmap(width, totalHeight);
        using (var graphics = Graphics.FromImage(canvas))
        {
            var y = 0;
            for (var i = 0; i < frames.Count; i++)
            {
                if (frames[i].Width != width || frames[i].Height != height)
                {
                    canvas.Dispose();
                    throw new ArgumentException("All frames must have the same size.", nameof(frames));
                }

                if (i == 0 || ignoreTopRows == 0)
                {
                    graphics.DrawImage(frames[i], 0, y, width, height);
                }
                else
                {
                    // Skip the fixed top band: it was already stitched with the first frame. The band's
                    // rows below the seam keep the previous frame's content, so the seam stays seamless.
                    var stripHeight = height - ignoreTopRows;
                    graphics.DrawImage(
                        frames[i],
                        new System.Drawing.Rectangle(0, y + ignoreTopRows, width, stripHeight),
                        new System.Drawing.Rectangle(0, ignoreTopRows, width, stripHeight),
                        GraphicsUnit.Pixel);
                }

                if (i < frames.Count - 1)
                {
                    y += height - overlaps[i];
                }
            }
        }

        return canvas;
    }

    private static bool RowsMatch(byte[] aPixels, int aStride, int aRow, byte[] bPixels, int bStride, int bRow, int width)
    {
        var sampled = (width + PixelColumnStride - 1) / PixelColumnStride;
        var maxBad = (int)Math.Floor(sampled * (1.0 - RowMatchThreshold));
        var aStart = aRow * aStride;
        var bStart = bRow * bStride;
        var bad = 0;
        for (var x = 0; x < width; x += PixelColumnStride)
        {
            var a = aStart + (x * 4);
            var b = bStart + (x * 4);
            var diff = Math.Abs(aPixels[a] - bPixels[b])
                     + Math.Abs(aPixels[a + 1] - bPixels[b + 1])
                     + Math.Abs(aPixels[a + 2] - bPixels[b + 2]);
            if (diff > PixelToleranceSum)
            {
                bad++;
                if (bad > maxBad)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsBlockChanged(
        byte[] aPixels,
        int aStride,
        byte[] bPixels,
        int bStride,
        int width,
        int height,
        int startX,
        int startY,
        int maxChangedPixels)
    {
        var blockWidth = Math.Min(BlockSize, width - startX);
        var blockHeight = Math.Min(BlockSize, height - startY);
        var changed = 0;
        for (var y = 0; y < blockHeight; y++)
        {
            var aRow = (startY + y) * aStride;
            var bRow = (startY + y) * bStride;
            for (var x = 0; x < blockWidth; x++)
            {
                var a = aRow + ((startX + x) * 4);
                var b = bRow + ((startX + x) * 4);
                var diff = Math.Abs(aPixels[a] - bPixels[b])
                         + Math.Abs(aPixels[a + 1] - bPixels[b + 1])
                         + Math.Abs(aPixels[a + 2] - bPixels[b + 2]);
                if (diff > PixelToleranceSum)
                {
                    changed++;
                    if (changed > maxChangedPixels)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static byte[] ToArgbBytes(Bitmap bitmap, out int stride)
    {
        var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            stride = data.Stride;
            if (stride < 0)
            {
                throw new NotSupportedException("Bottom-up bitmaps are not supported.");
            }

            var bytes = new byte[stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
