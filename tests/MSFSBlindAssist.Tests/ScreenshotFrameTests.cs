using System.Drawing;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The blank-frame check that decides whether a PrintWindow capture is usable or the screen copy
/// must be taken instead. Sampled on a grid, so a 4K frame costs a few hundred pixel reads.
/// </summary>
public class ScreenshotFrameTests
{
    private static Bitmap Filled(int width, int height, Color colour)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(colour);
        return bitmap;
    }

    [Fact]
    public void AnAllBlackFrame_LooksBlank()
    {
        using var bitmap = Filled(640, 360, Color.Black);
        Assert.True(ScreenshotFrame.LooksBlank(bitmap));
    }

    [Fact]
    public void ANearBlackFrame_StillLooksBlank()
    {
        using var bitmap = Filled(640, 360, Color.FromArgb(8, 8, 8));
        Assert.True(ScreenshotFrame.LooksBlank(bitmap));
    }

    [Fact]
    public void ABlackFrameWithOneLitPanel_DoesNotLookBlank()
    {
        using var bitmap = Filled(640, 360, Color.Black);
        using (var g = Graphics.FromImage(bitmap))
            g.FillRectangle(Brushes.LimeGreen, new Rectangle(300, 150, 120, 80));

        Assert.False(ScreenshotFrame.LooksBlank(bitmap));
    }

    [Fact]
    public void ACockpitColouredFrame_DoesNotLookBlank()
    {
        using var bitmap = Filled(1920, 1080, Color.FromArgb(40, 44, 52));
        Assert.False(ScreenshotFrame.LooksBlank(bitmap));
    }

    [Fact]
    public void ATinyFrame_IsSampledPixelByPixel_WithoutThrowing()
    {
        using var black = Filled(1, 1, Color.Black);
        using var white = Filled(1, 1, Color.White);

        Assert.True(ScreenshotFrame.LooksBlank(black));
        Assert.False(ScreenshotFrame.LooksBlank(white));
    }

    // A night cockpit: nothing brighter than R+G+B 25 anywhere, so every sample passes the dark
    // test on its own, yet the dim lighting varies across the frame, which a failed capture never
    // does. Before the uniformity rule this real picture was thrown away for the screen copy.
    [Fact]
    public void ADarkButVariedFrame_DoesNotLookBlank()
    {
        using var bitmap = DarkRamp(640, 360, topBlue: 25);
        Assert.False(ScreenshotFrame.LooksBlank(bitmap));
    }

    // The uniformity allowance is a spread of R+G+B across the samples, inclusive: a failed frame
    // may carry a little noise, one step more is a (very dark) picture.
    [Theory]
    [InlineData(2, 2, 2, true)]    // R+G+B 0 beside 6: within the spread, still a failed frame
    [InlineData(2, 2, 3, false)]   // 0 beside 7: one past it, a picture
    public void ADarkFrameInTwoHalves_LooksBlankOnlyWithinTheUniformSpread(int r, int g, int b, bool blank)
    {
        using var bitmap = Filled(640, 360, Color.Black);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Color.FromArgb(r, g, b)))
            graphics.FillRectangle(brush, new Rectangle(320, 0, 320, 360));

        Assert.Equal(blank, ScreenshotFrame.LooksBlank(bitmap));
    }

    // One lit sample is enough: the rest of the frame being flat black does not outvote it. The
    // bitmap is exactly grid-sized, so every pixel is a sample and the lit one cannot be skipped.
    [Fact]
    public void OneLitSampleOnAFlatBlackFrame_DoesNotLookBlank()
    {
        using var bitmap = Filled(24, 14, Color.Black);
        bitmap.SetPixel(11, 6, Color.White);
        Assert.False(ScreenshotFrame.LooksBlank(bitmap));
    }

    // PrintWindow with PW_RENDERFULLCONTENT draws the NON-CLIENT area and the bitmap is sized from
    // GetWindowRect, so on a windowed simulator the top strip is the title bar. A Windows dark-theme
    // caption is about RGB 32,32,32 — sum 96, far over the 30 threshold — so sampling it ended the
    // test on its very first sample and a black client area was reported as a picture and sent to
    // the AI. Bounding the samples to the client area is what makes the verdict mean anything here.
    [Fact]
    public void ABlackClientAreaUnderALitTitleBar_LooksBlankWithinTheClientRegion()
    {
        using var bitmap = Filled(640, 390, Color.Black);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var caption = new SolidBrush(Color.FromArgb(32, 32, 32)))
            graphics.FillRectangle(caption, 0, 0, 640, 30);

        Assert.False(ScreenshotFrame.LooksBlank(bitmap));                                       // the whole window: the caption alone clears it
        Assert.True(ScreenshotFrame.LooksBlank(bitmap, new Rectangle(0, 30, 640, 360)));        // the client area: still a failed capture
    }

    // The region is a bound on the samples, not a claim that everything outside it is blank: a real
    // picture inside the client area must still be reported as a picture.
    [Fact]
    public void ALitClientAreaUnderALitTitleBar_DoesNotLookBlank()
    {
        using var bitmap = Filled(640, 390, Color.FromArgb(32, 32, 32));
        using (var graphics = Graphics.FromImage(bitmap))
        using (var lit = new SolidBrush(Color.White))
            graphics.FillRectangle(lit, 0, 30, 640, 360);

        Assert.False(ScreenshotFrame.LooksBlank(bitmap, new Rectangle(0, 30, 640, 360)));
    }

    // A borderless or fullscreen window has no caption to exclude, and a window whose client area
    // Windows will not report hands back null — both must sample the whole bitmap, as before.
    [Theory]
    [InlineData(null)]
    [InlineData(new[] { 900, 900, 40, 40 })]   // entirely outside the bitmap
    public void ARegionThatCannotBeUsed_FallsBackToTheWholeBitmap(int[]? bounds)
    {
        Rectangle? region = bounds == null ? null : new Rectangle(bounds[0], bounds[1], bounds[2], bounds[3]);
        using var black = Filled(640, 360, Color.Black);
        using var white = Filled(640, 360, Color.White);
        Assert.True(ScreenshotFrame.LooksBlank(black, region));
        Assert.False(ScreenshotFrame.LooksBlank(white, region));
    }

    /// <summary>A left-to-right ramp in the blue channel only, 0 at the left edge up to <paramref name="topBlue"/> at the right, so each pixel's R+G+B is its blue value.</summary>
    private static Bitmap DarkRamp(int width, int height, int topBlue)
    {
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        using var brush = new SolidBrush(Color.Black);
        for (int x = 0; x < width; x++)
        {
            brush.Color = Color.FromArgb(0, 0, x * topBlue / (width - 1));
            graphics.FillRectangle(brush, x, 0, 1, height);
        }
        return bitmap;
    }
}
