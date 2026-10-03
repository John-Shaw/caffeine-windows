using System;
using System.Drawing;
using System.Windows.Forms;

namespace CaffeineSetup
{
    /// <summary>
    /// Manual DPI layout.
    ///
    /// AutoScaleMode.Dpi is not dependable for a top-level Form: the font gets
    /// scaled but the child bounds do not, so a 200% display shows 18pt text
    /// inside boxes laid out for 9pt - the title lands on top of the subtitle
    /// and checkbox text gets sliced in half.  So every coordinate in
    /// InstallUI/UninstallUI is written in 96dpi units and multiplied by the
    /// same factor here.  Fonts are left alone: a point size is already
    /// device-relative, so the OS scales those by itself and doing it twice is
    /// how you end up with text twice as large as the box it sits in.
    ///
    /// The result is the exact same layout at 100%, 125%, 150% or 200%.
    /// </summary>
    internal sealed class Lay
    {
        /// <summary>device dpi / 96.</summary>
        internal readonly float K;

        private readonly Font template;

        internal Lay(Font template)
        {
            this.template = template;
            K = DeviceFactor();
        }

        private static float DeviceFactor()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    float d = g.DpiX;
                    if (d < 72F || d > 480F) return 1F;
                    return d / 96F;
                }
            }
            catch (Exception)
            {
                return 1F;
            }
        }

        /// <summary>A design-unit length in real pixels.</summary>
        internal int X(int design)
        {
            return (int)Math.Round(design * K, MidpointRounding.AwayFromZero);
        }

        internal Point P(int x, int y)
        {
            return new Point(X(x), X(y));
        }

        internal Size S(int w, int h)
        {
            return new Size(X(w), X(h));
        }

        /// <summary>Explicit point size, in design units (9 = 9pt at 96dpi).</summary>
        internal Font F(float pt, FontStyle style)
        {
            // Point sizes are deliberately NOT multiplied by K: a point is already
            // device-relative, so WinFonts/GDI+ turn 9pt into 24 physical pixels
            // on a 200% display all by itself.  Scaling it here too is what
            // gives you 48px text in a 56px box.
            return new Font(template.FontFamily, pt, style, GraphicsUnit.Point);
        }

        /// <summary>The form's own font, at the design point size.</summary>
        internal Font F()
        {
            return new Font(template.FontFamily, template.SizeInPoints,
                template.Style, GraphicsUnit.Point);
        }
    }
}
