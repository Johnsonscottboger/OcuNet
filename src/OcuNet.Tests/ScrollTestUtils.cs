using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Xunit;

namespace OcuNet.Tests;

/// <summary>
/// Test helpers to build synthetic scrolling content: a virtual tall canvas whose rows are rendered
/// deterministically from their absolute row index, so any offset range can be rendered without storing
/// the whole canvas. Also provides bitmap comparison helpers.
/// </summary>
internal static class ScrollTestUtils
{
    /// <summary>
    /// Renders a bitmap of <paramref name="height"/> rows starting at absolute row <paramref name="startOffset"/>.
    /// Every row has a deterministic color derived from its absolute row index, so crops of consecutive offsets
    /// overlap exactly like real scrolling content would.
    /// </summary>
    /// <remarks>
    /// Labels are anchored to absolute rows and only rendered when the anchor falls inside the rendered range,
    /// so a label anchored just before <paramref name="startOffset"/> is not partially re-rendered inside the
    /// bitmap (its tail is simply absent). This is fine for overlap detection but breaks pixel-exact comparisons
    /// against a full-canvas render; equality assertions must pass <c>drawLabels: false</c>.
    /// </remarks>
    public static Bitmap CreateCanvas(int width, int height, int startOffset = 0, bool drawLabels = true)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var pixels = new byte[Math.Abs(stride) * height];
            for (var y = 0; y < height; y++)
            {
                var row = startOffset + y;
                var r = (byte)(Hash((row * 3) + 0) & 0xFF);
                var g = (byte)(Hash((row * 3) + 1) & 0xFF);
                var b = (byte)(Hash((row * 3) + 2) & 0xFF);
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

            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        if (drawLabels)
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                using var font = new Font(FontFamily.GenericSansSerif, 10);
                for (var y = 0; y < height; y++)
                {
                    var row = startOffset + y;
                    if (row % 60 == 0)
                    {
                        graphics.DrawString($"Row {row}", font, Brushes.White, 4, y + 2);
                    }
                }
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Renders a frame of <paramref name="height"/> rows starting at absolute row <paramref name="startOffset"/>,
    /// then overlays a fixed status bar of <paramref name="statusBarHeight"/> rows at the top with a small
    /// "clock" rectangle whose color changes with <paramref name="clockValue"/> (simulating a status bar clock
    /// that changes at a fixed screen position).
    /// </summary>
    public static Bitmap CreateFrameWithStatusBar(int width, int height, int startOffset, int statusBarHeight, int clockValue)
    {
        var frame = CreateCanvas(width, height, startOffset);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.FillRectangle(Brushes.DarkSlateGray, 0, 0, width, statusBarHeight);
            using var clockBrush = new SolidBrush(Color.FromArgb(clockValue % 256, 200, 200));
            graphics.FillRectangle(clockBrush, width - 40, 5, 30, 10);
        }

        return frame;
    }

    /// <summary>
    /// Renders a canvas like <see cref="CreateCanvas(int, int, int, bool)"/> (drawLabels disabled) and
    /// overwrites a rectangle with <paramref name="color"/>. Pixels are written through LockBits because
    /// Graphics rendering is not reliable under the roll-forward test host.
    /// </summary>
    public static Bitmap CreateCanvasWithRect(int width, int height, int startOffset, int rectX, int rectY, int rectWidth, int rectHeight, Color color)
    {
        var bitmap = CreateCanvas(width, height, startOffset, drawLabels: false);
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var pixels = new byte[Math.Abs(stride) * height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            for (var y = Math.Max(0, rectY); y < Math.Min(height, rectY + rectHeight); y++)
            {
                for (var x = Math.Max(0, rectX); x < Math.Min(width, rectX + rectWidth); x++)
                {
                    var p = (y * stride) + (x * 4);
                    pixels[p] = color.B;
                    pixels[p + 1] = color.G;
                    pixels[p + 2] = color.R;
                    pixels[p + 3] = color.A;
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

    /// <summary>
    /// Renders a canvas like <see cref="CreateCanvas(int, int, int, bool)"/> (drawLabels disabled) with a
    /// fixed top band of <paramref name="bandHeight"/> rows (simulating a window title bar / sticky
    /// header): a constant dark color plus a constant "clock" rectangle. The band is written through
    /// LockBits because Graphics rendering is not reliable under the roll-forward test host.
    /// </summary>
    public static Bitmap CreateCanvasWithFixedTopBand(int width, int height, int startOffset, int bandHeight)
    {
        var bitmap = CreateCanvas(width, height, startOffset, drawLabels: false);
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var pixels = new byte[Math.Abs(stride) * height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            for (var y = 0; y < bandHeight; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var p = (y * stride) + (x * 4);
                    var isClock = y >= 5 && y < 15 && x >= width - 40 && x < width - 10;
                    if (isClock)
                    {
                        pixels[p] = 0; // Orange clock block (constant -> the band is static across frames).
                        pixels[p + 1] = 165;
                        pixels[p + 2] = 255;
                    }
                    else
                    {
                        pixels[p] = 79; // DarkSlateGray.
                        pixels[p + 1] = 79;
                        pixels[p + 2] = 47;
                    }

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

    /// <summary>
    /// Copies <paramref name="height"/> rows starting at row <paramref name="y"/> of <paramref name="source"/>
    /// into a new bitmap.
    /// </summary>
    public static Bitmap CropRows(Bitmap source, int y, int height)
    {
        var result = new Bitmap(source.Width, height);
        using (var graphics = Graphics.FromImage(result))
        {
            graphics.DrawImage(
                source,
                new System.Drawing.Rectangle(0, 0, source.Width, height),
                new System.Drawing.Rectangle(0, y, source.Width, height),
                GraphicsUnit.Pixel);
        }

        return result;
    }

    /// <summary>
    /// Asserts that <paramref name="actual"/> has the same size and pixels as <paramref name="expected"/>,
    /// allowing a per-pixel channel difference of at most <paramref name="tolerance"/>.
    /// </summary>
    public static void AssertBitmapsEqual(Bitmap expected, Bitmap actual, int tolerance = 0)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);

        var expectedPixels = ToArgbBytes(expected, out var expectedStride);
        var actualPixels = ToArgbBytes(actual, out var actualStride);
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var e = (y * expectedStride) + (x * 4);
                var a = (y * actualStride) + (x * 4);
                var diff = Math.Abs(expectedPixels[e] - actualPixels[a])
                         + Math.Abs(expectedPixels[e + 1] - actualPixels[a + 1])
                         + Math.Abs(expectedPixels[e + 2] - actualPixels[a + 2]);
                if (diff > tolerance)
                {
                    Assert.True(false, $"Pixels differ at ({x}, {y}): diff={diff}, tolerance={tolerance}.");
                }
            }
        }
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

    private static byte[] ToArgbBytes(Bitmap bitmap, out int stride)
    {
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            stride = data.Stride;
            var bytes = new byte[Math.Abs(stride) * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
