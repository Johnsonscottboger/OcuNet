using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace OcuNet.Tests;

public class ScrollStitcherTests
{
    private const int CanvasWidth = 640;
    private const int ViewportHeight = 480;

    [Theory]
    [InlineData(47)]
    [InlineData(96)]
    [InlineData(150)]
    [InlineData(213)]
    public async Task FindOverlap_ReturnsExactOverlap_ForVariousScrollAmounts(int scrollAmount)
    {
        using var upper = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);
        using var lower = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, scrollAmount);

        var expectedOverlap = ViewportHeight - scrollAmount;

        Assert.Equal(expectedOverlap, ScrollStitcher.FindOverlap(upper, lower));
        Assert.Equal(expectedOverlap, ScrollStitcher.FindOverlap(upper, lower, expectedOverlap));
    }

    [Fact]
    public async Task FindOverlap_IdenticalFrames_ReturnsFrameHeight()
    {
        using var frame = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);

        Assert.Equal(ViewportHeight, ScrollStitcher.FindOverlap(frame, frame));
    }

    [Fact]
    public async Task FindOverlap_NoOverlap_ReturnsNull()
    {
        using var upper = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);
        using var lower = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 600);

        Assert.Null(ScrollStitcher.FindOverlap(upper, lower));
    }

    [Fact]
    public async Task FindOverlap_DifferentSizes_ReturnsNull()
    {
        using var upper = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);
        using var lower = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight + 100, 0);

        Assert.Null(ScrollStitcher.FindOverlap(upper, lower));
    }

    [Fact]
    public async Task FindOverlap_StatusBarWithChangingClock_StillFindsOffset()
    {
        // A fixed 80px status bar whose "clock" changes between the two frames (the same-position
        // slight-difference case). The overlap must still be measured correctly without any band knowledge.
        using var upper = ScrollTestUtils.CreateFrameWithStatusBar(CanvasWidth, ViewportHeight, 0, statusBarHeight: 80, clockValue: 1);
        using var lower = ScrollTestUtils.CreateFrameWithStatusBar(CanvasWidth, ViewportHeight, 120, statusBarHeight: 80, clockValue: 2);

        Assert.Equal(ViewportHeight - 120, ScrollStitcher.FindOverlap(upper, lower));
    }

    [Fact]
    public async Task FindOverlap_StickyHeader_StillFindsOffset()
    {
        // A constant 80px sticky header on both frames (no changing clock): the header rows match at the
        // no-scroll offset only, and must not confuse the argmax.
        using var upper = ScrollTestUtils.CreateFrameWithStatusBar(CanvasWidth, ViewportHeight, 0, statusBarHeight: 80, clockValue: 1);
        using var lower = ScrollTestUtils.CreateFrameWithStatusBar(CanvasWidth, ViewportHeight, 100, statusBarHeight: 80, clockValue: 1);

        Assert.Equal(ViewportHeight - 100, ScrollStitcher.FindOverlap(upper, lower));
    }

    [Fact]
    public async Task FindOverlap_PeriodicContent_ReturnsPeriodConsistentOffset()
    {
        // Perfectly periodic content (identical rows repeating) has no uniquely identifiable offset, so
        // the exact offset is not guaranteed. The result stays period-consistent (stitching remains
        // seamless); real pages are never exactly periodic.
        using var upper = CreatePeriodicCanvas(CanvasWidth, ViewportHeight, 0, period: 128);
        using var lower = CreatePeriodicCanvas(CanvasWidth, ViewportHeight, 150, period: 128);

        var overlap = ScrollStitcher.FindOverlap(upper, lower);

        Assert.NotNull(overlap);
        Assert.Equal(0, (ViewportHeight - overlap!.Value - 150) % 128);
    }

    [Fact]
    public async Task IsSameContent_IdenticalFrames_ReturnsTrue()
    {
        using var a = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);
        using var b = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);

        Assert.True(ScrollStitcher.IsSameContent(a, b));
    }

    [Fact]
    public async Task IsSameContent_DifferentContent_ReturnsFalse()
    {
        using var a = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);
        using var b = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 150);

        Assert.False(ScrollStitcher.IsSameContent(a, b));
    }

    [Fact]
    public async Task IsSameContent_SmallDynamicRegion_ReturnsTrue()
    {
        // A small "clock-like" region changing between the frames must not make the frames look different.
        using var a = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);
        using var b = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);
        using (var graphics = Graphics.FromImage(b))
        {
            graphics.FillRectangle(Brushes.Orange, CanvasWidth - 40, 0, 20, 10);
        }

        Assert.True(ScrollStitcher.IsSameContent(a, b));
    }

    [Fact]
    public async Task IsSameContent_LargeDynamicRegion_ReturnsFalse()
    {
        using var a = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);
        using var b = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);
        using (var graphics = Graphics.FromImage(b))
        {
            graphics.FillRectangle(Brushes.Orange, 0, 0, 200, 100);
        }

        Assert.False(ScrollStitcher.IsSameContent(a, b));
    }

    [Fact]
    public async Task DetectStaticBands_TopStatusBar_IsDetected()
    {
        var frames = new List<Bitmap>();
        try
        {
            for (var i = 0; i < 6; i++)
            {
                frames.Add(ScrollTestUtils.CreateFrameWithStatusBar(CanvasWidth, ViewportHeight, i * 60, statusBarHeight: 80, clockValue: i));
            }

            var (topRows, bottomRows) = ScrollStitcher.DetectStaticBands(frames);

            Assert.Equal(80, topRows);
            Assert.Equal(0, bottomRows);
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    [Fact]
    public async Task DetectStaticBands_BottomFooter_IsDetected()
    {
        var frames = new List<Bitmap>();
        try
        {
            for (var i = 0; i < 6; i++)
            {
                frames.Add(CreateFrameWithBottomFooter(CanvasWidth, ViewportHeight, i * 60, footerHeight: 60, footerValue: i));
            }

            var (topRows, bottomRows) = ScrollStitcher.DetectStaticBands(frames);

            Assert.Equal(0, topRows);
            Assert.Equal(60, bottomRows);
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    [Fact]
    public async Task DetectStaticBands_ContentStoppedAtBottom_IsNotMisclassified()
    {
        // The last two pairs are static (content stopped producing new frames). Those rows must not be
        // detected as fixed bands: only rows static in the MAJORITY of pairs count.
        var frames = new List<Bitmap>();
        try
        {
            var offsets = new[] { 0, 60, 120, 180, 180, 180 };
            foreach (var offset in offsets)
            {
                frames.Add(ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, offset));
            }

            var (topRows, bottomRows) = ScrollStitcher.DetectStaticBands(frames);

            Assert.Equal(0, topRows);
            Assert.Equal(0, bottomRows);
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    [Fact]
    public async Task Stitch_ReproducesOriginalCanvas()
    {
        // Labels are disabled so the frames are pixel-identical crops of the same virtual canvas (a label
        // anchored just before a frame boundary would otherwise be partially re-rendered inside the frame).
        var frames = new List<Bitmap>();
        try
        {
            var offsets = new[] { 0, 70, 140, 210, 280 };
            foreach (var offset in offsets)
            {
                frames.Add(ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, offset, drawLabels: false));
            }

            var overlaps = new List<int> { ViewportHeight - 70, ViewportHeight - 70, ViewportHeight - 70, ViewportHeight - 70 };
            using var stitched = ScrollStitcher.Stitch(frames, overlaps);
            using var expected = ScrollTestUtils.CreateCanvas(CanvasWidth, (70 * 4) + ViewportHeight, 0, drawLabels: false);

            ScrollTestUtils.AssertBitmapsEqual(expected, stitched);
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    [Fact]
    public async Task Stitch_SingleFrame_ReturnsCopy()
    {
        using var frame = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);
        using var stitched = ScrollStitcher.Stitch(new[] { frame }, Array.Empty<int>());
        using var expected = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);

        Assert.Equal(ViewportHeight, stitched.Height);
        ScrollTestUtils.AssertBitmapsEqual(expected, stitched);
    }

    [Fact]
    public async Task Stitch_WrongOverlapCount_Throws()
    {
        using var frame = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);

        Assert.Throws<ArgumentException>(() => ScrollStitcher.Stitch(new[] { frame }, new[] { 100 }));
    }

    [Fact]
    public async Task Stitch_NegativeOverlap_Throws()
    {
        using var a = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);
        using var b = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 70);

        Assert.Throws<ArgumentOutOfRangeException>(() => ScrollStitcher.Stitch(new[] { a, b }, new[] { -1 }));
    }

    [Fact]
    public async Task FindOverlap_PerformanceSmoke()
    {
        using var upper = ScrollTestUtils.CreateCanvas(1920, 1080, 0);
        using var lower = ScrollTestUtils.CreateCanvas(1920, 1080, 150);

        var stopwatch = Stopwatch.StartNew();
        var overlap = ScrollStitcher.FindOverlap(upper, lower);
        stopwatch.Stop();

        Assert.Equal(1080 - 150, overlap);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"FindOverlap took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task FindOverlap_WithTopBandExcluded_IgnoresTitleBar()
    {
        // A window with a fixed 80px title bar on top of scrolling content: the title bar rows match at
        // the no-scroll offset only and would compete with the content alignment. Excluding them (the
        // merge phase passes the full-sequence band estimate) keeps the measured offset exact.
        using var upper = ScrollTestUtils.CreateCanvasWithFixedTopBand(CanvasWidth, ViewportHeight, 0, 80);
        using var lower = ScrollTestUtils.CreateCanvasWithFixedTopBand(CanvasWidth, ViewportHeight, 150, 80);

        Assert.Equal(ViewportHeight - 150, ScrollStitcher.FindOverlap(upper, lower, null, ignoreTopRows: 80));
    }

    [Fact]
    public async Task StitchAll_FixedTopBand_FullPipeline()
    {
        // Merge phase over a whole sequence: band detection, per-pair measurement, and stitching with
        // the title bar kept only on the first frame.
        var frames = new List<Bitmap>();
        try
        {
            var offsets = new[] { 0, 150, 300 };
            foreach (var offset in offsets)
            {
                frames.Add(ScrollTestUtils.CreateCanvasWithFixedTopBand(CanvasWidth, ViewportHeight, offset, 80));
            }

            var (topRows, bottomRows) = ScrollStitcher.DetectStaticBands(frames);
            using var stitched = ScrollStitcher.StitchAll(frames, topRows, bottomRows);
            using var expected = ScrollTestUtils.CreateCanvasWithFixedTopBand(CanvasWidth, ViewportHeight + 300, 0, 80);

            Assert.Equal(80, topRows);
            Assert.Equal(0, bottomRows);
            Assert.Equal(ViewportHeight + 300, stitched.Height);
            ScrollTestUtils.AssertBitmapsEqual(expected, stitched);
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    [Fact]
    public async Task CountChangedRows_LocalDynamicsStayBelowThreshold()
    {
        using var a = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);
        using var b = ScrollTestUtils.CreateCanvasWithRect(CanvasWidth, ViewportHeight, 0, 0, 0, CanvasWidth, 30, Color.Orange);

        var changedRows = ScrollStitcher.CountChangedRows(a, b);
        Assert.True(changedRows <= ViewportHeight * ScrollStitcher.MaxChangedRowRatio, $"changed={changedRows}");

        using var c = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 150);
        Assert.True(ScrollStitcher.CountChangedRows(a, c) > ViewportHeight * ScrollStitcher.MaxChangedRowRatio);
    }

    [Fact]
    public async Task Stitch_SkipsTopBandOnSubsequentFrames()
    {
        // The title bar must appear once, at the top: subsequent frames are stitched from below the band.
        using var frame0 = ScrollTestUtils.CreateCanvasWithFixedTopBand(CanvasWidth, ViewportHeight, 0, 80);
        using var frame1 = ScrollTestUtils.CreateCanvasWithFixedTopBand(CanvasWidth, ViewportHeight, 150, 80);
        using var stitched = ScrollStitcher.Stitch(new[] { frame0, frame1 }, new[] { ViewportHeight - 150 }, ignoreTopRows: 80);
        using var expected = ScrollTestUtils.CreateCanvasWithFixedTopBand(CanvasWidth, ViewportHeight + 150, 0, 80);

        Assert.Equal(ViewportHeight + 150, stitched.Height);
        ScrollTestUtils.AssertBitmapsEqual(expected, stitched);
    }

    [Fact]
    public async Task FindOverlap_RealPageStyleContent_FindsOffset()
    {
        // Real pages contain many blank rows that match at several offsets (a plateau that would dominate
        // a raw match-count argmax). Only rows matching at exactly one offset count, so the true offset
        // must still be found — both without and with an expected overlap.
        using var upper = CreateRealPageStyleCanvas(CanvasWidth, ViewportHeight, 0);
        using var lower = CreateRealPageStyleCanvas(CanvasWidth, ViewportHeight, 150);

        Assert.Equal(ViewportHeight - 150, ScrollStitcher.FindOverlap(upper, lower, null));
        Assert.Equal(ViewportHeight - 150, ScrollStitcher.FindOverlap(upper, lower, ViewportHeight - 150));
    }

    [Fact]
    public async Task FindOverlap_TrulyStaticContent_ReturnsNull()
    {
        // No measurable overlap anywhere (content moved more than the viewport): only this case reports
        // "no progress".
        using var upper = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 0);
        using var lower = ScrollTestUtils.CreateCanvas(CanvasWidth, ViewportHeight, 1000);

        Assert.Null(ScrollStitcher.FindOverlap(upper, lower));
    }

    private static Bitmap CreateRealPageStyleCanvas(int width, int height, int startOffset)
    {
        // Mostly blank rows (white) with a unique "text-like" row block every 15 rows. Blank rows match
        // at every offset, creating the plateau real pages exhibit. Each content block is 12 black bars
        // at hash-derived distinct positions (unique per row index, stable ~15% coverage). Pixels are
        // written through LockBits because Graphics rendering (DrawString/FillRectangle) is not reliable
        // under the roll-forward test host.
        var bitmap = new Bitmap(width, height);
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var pixels = new byte[Math.Abs(stride) * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = 255; // White background.
            }

            for (var y = 0; y < height; y++)
            {
                var row = startOffset + y;
                if (row % 15 == 0)
                {
                    for (var offset = 2; offset < 12 && y + offset < height; offset++)
                    {
                        // Each row of the block gets its own bar pattern derived from the ABSOLUTE canvas
                        // row (so the same canvas row renders identically in every offset bitmap) and the
                        // in-block offset (so block rows are only identical to their exact counterpart).
                        var barRow = row + offset;
                        var barY = y + offset;
                        var positions = new HashSet<int>();
                        for (var k = 0; positions.Count < 12 && k < 64; k++)
                        {
                            positions.Add(Hash((row * 31) + (barRow * 13) + (k * 7919)) % 32);
                        }

                        foreach (var m in positions)
                        {
                            for (var x = 4 + (m * 16); x < 14 + (m * 16) && x < width; x++)
                            {
                                var p = (barY * stride) + (x * 4);
                                pixels[p] = 0;
                                pixels[p + 1] = 0;
                                pixels[p + 2] = 0;
                                pixels[p + 3] = 255;
                            }
                        }
                    }
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static int Hash(int value)
    {
        unchecked
        {
            var h = (uint)value * 2654435761u;
            h ^= h >> 16;
            h *= 2246822519u;
            h ^= h >> 13;
            return (int)h;
        }
    }

    private static Bitmap CreateFrameWithBottomFooter(int width, int height, int startOffset, int footerHeight, int footerValue)
    {
        var frame = ScrollTestUtils.CreateCanvas(width, height, startOffset);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.FillRectangle(Brushes.DarkSlateGray, 0, height - footerHeight, width, footerHeight);
            using var footerBrush = new SolidBrush(Color.FromArgb(footerValue % 256, 200, 200));
            graphics.FillRectangle(footerBrush, 20, height - footerHeight + 5, 30, 10);
        }

        return frame;
    }

    private static Bitmap CreatePeriodicCanvas(int width, int height, int startOffset, int period)
    {
        // Rows repeat every `period` rows: color depends on the row index modulo the period.
        var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
        }

        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var pixels = new byte[Math.Abs(stride) * height];
            for (var y = 0; y < height; y++)
            {
                var row = (startOffset + y) % period;
                var r = (byte)((row * 71) % 256);
                var g = (byte)((row * 131) % 256);
                var b = (byte)((row * 197) % 256);
                var rowStart = y * stride;
                for (var x = 0; x < width; x++)
                {
                    var p = rowStart + (x * 4);
                    pixels[p] = b;
                    pixels[p + 1] = g;
                    pixels[p + 2] = r;
                    pixels[p + 3] = 255;
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }
}
