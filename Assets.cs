using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;

namespace DiskLit;

internal static class Assets
{
    const string IconResource = "DiskLit.DiskLit.ico";
    const string GlyphResource = "DiskLit.disklit_icon.png";

    static readonly Dictionary<(int Size, int Color), Bitmap> tinted = [];
    static readonly Lazy<Bitmap?> glyph = new(() => Load(GlyphResource, stream => new Bitmap(stream)));
    static readonly Lazy<Icon?> icon = new(() => Load(IconResource, stream => new Icon(stream, 256, 256)));

    public static Icon? AppIcon => icon.Value;

    public static Bitmap? Glyph(int size, Color color)
    {
        var key = (size, color.ToArgb());
        if (tinted.TryGetValue(key, out var cached)) return cached;

        var source = glyph.Value;
        if (source is null) return null;

        var canvas = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var matrix = new ColorMatrix(
            [
                [0f, 0f, 0f, 0f, 0f],
                [0f, 0f, 0f, 0f, 0f],
                [0f, 0f, 0f, 0f, 0f],
                [0f, 0f, 0f, 1f, 0f],
                [color.R / 255f, color.G / 255f, color.B / 255f, 0f, 1f]
            ]);

            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix);
            graphics.DrawImage(source, new Rectangle(0, 0, size, size),
                0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }

        tinted[key] = canvas;
        return canvas;
    }

    static T? Load<T>(string name, Func<Stream, T> build) where T : class
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            return stream is null ? null : build(stream);
        }
        catch (Exception error) when (error is IOException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }
}
