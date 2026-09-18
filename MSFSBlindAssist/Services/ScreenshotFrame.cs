using System.Drawing;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Decides whether a captured frame carries a picture at all. PrintWindow can hand back a black
/// frame on some fullscreen setups; that frame must fall through to the screen copy rather than
/// reach the AI, which would confidently read "no display visible".
/// </summary>
internal static class ScreenshotFrame
{
    /// <summary>
    /// True when the frame is a failed capture: every sample on a <paramref name="gridColumns"/> ×
    /// <paramref name="gridRows"/> grid is near black (R+G+B ≤ <paramref name="darkThreshold"/>)
    /// AND the samples are uniform (their R+G+B values span at most <paramref name="uniformSpread"/>).
    /// A failed PrintWindow frame is flat black. A night cockpit is dark too, but its dim lighting
    /// varies from sample to sample, and a darkness-only test sent that real picture to the screen
    /// copy — which captures whatever is on top of the simulator, an MSFSBA window included. A
    /// bitmap smaller than the grid is sampled pixel by pixel.
    ///
    /// <paramref name="region"/> bounds the sampling to the window's CLIENT area, and passing it is
    /// what makes the test mean anything on a WINDOWED simulator. PrintWindow with
    /// PW_RENDERFULLCONTENT draws the NON-CLIENT area too and the bitmap is sized from
    /// GetWindowRect, so sample (0,0) is the title bar — never near black (Windows draws a
    /// dark-theme caption at about RGB 32,32,32, sum 96, well over the threshold). The first
    /// sample therefore ended the test on its own, a frame whose client area had come back BLACK
    /// was reported as a picture, and the AI read "no display visible" — the exact outcome this
    /// guard exists to prevent, on every aircraft, where the screen copy PrintWindow replaced had
    /// always produced a real frame. Null — or a rectangle that does not overlap the bitmap —
    /// samples the whole bitmap, which is right for a borderless or fullscreen window.
    /// </summary>
    internal static bool LooksBlank(Bitmap bitmap, Rectangle? region = null, int gridColumns = 24, int gridRows = 14,
        int darkThreshold = 30, int uniformSpread = 6)
    {
        var area = SampleArea(region, bitmap);
        int stepX = Math.Max(1, area.Width / gridColumns);
        int stepY = Math.Max(1, area.Height / gridRows);
        int darkest = int.MaxValue;
        int brightest = int.MinValue;
        for (int y = area.Top; y < area.Bottom; y += stepY)
        {
            for (int x = area.Left; x < area.Right; x += stepX)
            {
                var c = bitmap.GetPixel(x, y);
                int sum = c.R + c.G + c.B;
                if (sum > darkThreshold) return false;
                darkest = Math.Min(darkest, sum);
                brightest = Math.Max(brightest, sum);
                if (brightest - darkest > uniformSpread) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The rectangle to sample: the requested region clipped to the bitmap, or the whole bitmap
    /// when none was given or nothing of it lands inside. Degrading to the whole bitmap keeps a
    /// window whose client area Windows will not report exactly as it behaved before.
    /// </summary>
    private static Rectangle SampleArea(Rectangle? region, Bitmap bitmap)
    {
        var whole = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        if (region is not { } requested) return whole;
        var clipped = Rectangle.Intersect(requested, whole);
        return clipped.Width > 0 && clipped.Height > 0 ? clipped : whole;
    }
}
