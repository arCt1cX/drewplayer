using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IOPath = System.IO.Path;

namespace DrewPlayer;

/// <summary>A decoded photo: pixels already turned upright according to its EXIF orientation.</summary>
internal sealed record Photo(BitmapSource Bitmap, BitmapMetadata? Metadata, bool HasAlpha);

/// <summary>Loading, transforming and saving photos with WPF/WIC (used by the photo editor and quick actions).</summary>
internal static class PhotoIO
{
    private const string JpegOrientation = "/app1/ifd/{ushort=274}";
    private const string TiffOrientation = "/ifd/{ushort=274}";

    /// <summary>Decodes a still image. Throws NotSupportedException for animated or undecodable files.</summary>
    public static Photo Load(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new NotSupportedException("No image data");
        if (decoder.Frames.Count > 1) throw new NotSupportedException("Animated images can't be edited");

        var frame = decoder.Frames[0];
        BitmapMetadata? meta = null;
        int orientation = 1;
        try
        {
            meta = (frame.Metadata as BitmapMetadata)?.Clone();
            object? o = null;
            if (meta != null)
            {
                if (meta.ContainsQuery(JpegOrientation)) o = meta.GetQuery(JpegOrientation);
                else if (meta.ContainsQuery(TiffOrientation)) o = meta.GetQuery(TiffOrientation);
            }
            if (o is ushort u) orientation = u;
        }
        catch { meta = null; }

        BitmapSource bmp = frame;
        bmp = orientation switch
        {
            2 => Transform(bmp, 0, flipH: true),
            3 => Transform(bmp, 2, false),
            4 => Transform(bmp, 2, flipH: true),
            5 => Transform(bmp, 1, flipH: true),
            6 => Transform(bmp, 1, false),
            7 => Transform(bmp, 3, flipH: true),
            8 => Transform(bmp, 3, false),
            _ => bmp,
        };
        if (!bmp.IsFrozen) bmp.Freeze();
        return new Photo(bmp, meta, HasAlpha(bmp));
    }

    private static bool HasAlpha(BitmapSource b)
    {
        var f = b.Format;
        if (f == PixelFormats.Bgra32 || f == PixelFormats.Pbgra32 || f == PixelFormats.Rgba64 || f == PixelFormats.Prgba64
            || f == PixelFormats.Rgba128Float || f == PixelFormats.Prgba128Float) return true;
        return b.Palette?.Colors.Any(c => c.A < 255) == true; // indexed PNG/GIF with transparency
    }

    /// <summary>Rotates by quarterTurns × 90° clockwise, optionally flipping horizontally first.</summary>
    public static BitmapSource Transform(BitmapSource src, int quarterTurns, bool flipH = false, bool flipV = false)
    {
        var g = new TransformGroup();
        if (flipH || flipV) g.Children.Add(new ScaleTransform(flipH ? -1 : 1, flipV ? -1 : 1));
        if (quarterTurns % 4 != 0) g.Children.Add(new RotateTransform(90 * (((quarterTurns % 4) + 4) % 4)));
        return Materialize(new TransformedBitmap(src, g));
    }

    public static BitmapSource Crop(BitmapSource src, Int32Rect r) => Materialize(new CroppedBitmap(src, r));

    /// <summary>Flattens a chain of transforms into plain pixels (keeps memory and redraws predictable).</summary>
    public static BitmapSource Materialize(BitmapSource src)
    {
        var wb = new WriteableBitmap(src);
        wb.Freeze();
        return wb;
    }

    /// <summary>Formats WIC can write. Others (WebP, HEIC, AVIF, JXL...) can only be saved as a copy.</summary>
    public static bool CanEncode(string ext) => ext.ToLowerInvariant() is ".jpg" or ".jpeg" or ".jfif" or ".png" or ".bmp" or ".tif" or ".tiff" or ".gif";

    private static BitmapEncoder CreateEncoder(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => new PngBitmapEncoder(),
        ".bmp" => new BmpBitmapEncoder(),
        ".tif" or ".tiff" => new TiffBitmapEncoder(),
        ".gif" => new GifBitmapEncoder(),
        _ => new JpegBitmapEncoder { QualityLevel = 95 },
    };

    /// <summary>Writes the image (atomically: temp file, then move). Keeps EXIF for JPEG, with orientation reset to upright.</summary>
    public static void Save(BitmapSource bmp, BitmapMetadata? sourceMeta, string path)
    {
        var ext = IOPath.GetExtension(path);
        var enc = CreateEncoder(ext);
        BitmapMetadata? meta = null;
        if (enc is JpegBitmapEncoder && sourceMeta?.Format == "jpg")
        {
            try
            {
                meta = sourceMeta.Clone();
                if (meta.ContainsQuery(JpegOrientation)) meta.SetQuery(JpegOrientation, (ushort)1);
            }
            catch { meta = null; }
        }

        BitmapSource toWrite = bmp;
        if (enc is JpegBitmapEncoder or BmpBitmapEncoder && bmp.Format != PixelFormats.Bgr24 && bmp.Format != PixelFormats.Bgr32)
            toWrite = new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0); // no alpha in JPEG/BMP

        enc.Frames.Add(BitmapFrame.Create(toWrite, null, meta, null));
        var tmp = path + ".drewtmp";
        try
        {
            using (var fs = File.Create(tmp)) enc.Save(fs);
        }
        catch when (meta != null)
        {
            // some metadata can't be re-written: save without it
            enc = CreateEncoder(ext);
            enc.Frames.Add(BitmapFrame.Create(toWrite));
            using var fs = File.Create(tmp);
            enc.Save(fs);
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>"photo.jpg" → "photo (edited).jpg", "photo (edited 2).jpg", ...</summary>
    public static string CopyPath(string original, string ext)
    {
        var dir = IOPath.GetDirectoryName(original)!;
        var name = IOPath.GetFileNameWithoutExtension(original);
        for (int i = 1; ; i++)
        {
            var p = IOPath.Combine(dir, i == 1 ? $"{name} (edited){ext}" : $"{name} (edited {i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }
}
