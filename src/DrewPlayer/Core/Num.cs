using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace DrewPlayer;

internal static class Num
{
    public static bool TryParse(string? s, out double v) =>
        double.TryParse(s?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    public static string Inv(double v) => v.ToString(CultureInfo.InvariantCulture);
}
