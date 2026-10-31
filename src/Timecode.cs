using System.Globalization;

namespace Win4DaveMediaPlayer;

public static class Timecode
{
    public static bool TryParse(string text, out double seconds)
    {
        seconds = 0;
        var parts = text.Trim().Split(':');
        if (parts.Length is < 1 or > 3)
            return false;
        for (var index = 0; index < parts.Length; index++)
        {
            if (!double.TryParse(parts[index], NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < 0)
                return false;
            if (index < parts.Length - 1 && value != Math.Truncate(value))
                return false;
            if (index > 0 && value >= 60)
                return false;
            seconds = seconds * 60 + value;
        }
        return double.IsFinite(seconds);
    }

    public static string Format(double seconds)
    {
        var milliseconds = (long)Math.Round(Math.Max(0, seconds) * 1000);
        return $"{milliseconds / 3600000:00}:{milliseconds / 60000 % 60:00}:" +
            $"{milliseconds / 1000 % 60:00}.{milliseconds % 1000:000}";
    }
}
