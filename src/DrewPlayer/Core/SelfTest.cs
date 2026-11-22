#if DEBUG
using System.Text;
using System.Windows;
using IOPath = System.IO.Path;

namespace DrewPlayer;

/// <summary>Debug builds only: DREWPLAYER_SELFTEST=photo.jpg runs the photo pipeline headless and writes selftest.txt next to it.</summary>
internal static class SelfTest
{
    public static void RunPhoto(string path)
    {
        var log = new StringBuilder();
        void Check(string what, bool ok, string detail = "") => log.AppendLine($"{(ok ? "PASS" : "FAIL")}  {what} {detail}");
        try
        {
            var p = PhotoIO.Load(path);
            Check("load + EXIF orientation applied (1200x800 stored, orientation 6 → 800x1200)",
                p.Bitmap.PixelWidth == 800 && p.Bitmap.PixelHeight == 1200, $"got {p.Bitmap.PixelWidth}x{p.Bitmap.PixelHeight}");

            var rotated = PhotoIO.Transform(p.Bitmap, 1);
            Check("rotate right", rotated.PixelWidth == 1200 && rotated.PixelHeight == 800, $"{rotated.PixelWidth}x{rotated.PixelHeight}");

            var flipped = PhotoIO.Transform(rotated, 0, flipH: true);
            Check("flip keeps size", flipped.PixelWidth == 1200 && flipped.PixelHeight == 800);

            var cropped = PhotoIO.Crop(flipped, new Int32Rect(100, 50, 640, 360));
            Check("crop", cropped.PixelWidth == 640 && cropped.PixelHeight == 360);

            var copyJpg = PhotoIO.CopyPath(path, ".jpg");
            PhotoIO.Save(cropped, p.Metadata, copyJpg);
            var back = PhotoIO.Load(copyJpg);
            Check("save JPEG copy + reload (orientation reset, no double rotation)",
                back.Bitmap.PixelWidth == 640 && back.Bitmap.PixelHeight == 360, $"{IOPath.GetFileName(copyJpg)} → {back.Bitmap.PixelWidth}x{back.Bitmap.PixelHeight}");

            var copyPng = PhotoIO.CopyPath(path, ".png");
            PhotoIO.Save(rotated, null, copyPng);
            var png = PhotoIO.Load(copyPng);
            Check("save PNG copy + reload", png.Bitmap.PixelWidth == 1200 && png.Bitmap.PixelHeight == 800, IOPath.GetFileName(copyPng));

            Check("copy names don't collide", PhotoIO.CopyPath(path, ".jpg") != copyJpg);
            Check("CanEncode jpg/png, not webp/heic", PhotoIO.CanEncode(".JPG") && PhotoIO.CanEncode(".png") && !PhotoIO.CanEncode(".webp") && !PhotoIO.CanEncode(".heic"));
        }
        catch (Exception ex)
        {
            log.AppendLine("FAIL  exception: " + ex);
        }
        File.WriteAllText(IOPath.Combine(IOPath.GetDirectoryName(path)!, "selftest.txt"), log.ToString());
    }
}
#endif
