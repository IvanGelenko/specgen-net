using System.Globalization;

namespace PipingSpecPlugin.Spec
{
    internal static class SpecNumber
    {
        // A comma is a decimal separator, never a thousands separator.
        public static double? Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
                ? value : null;
        }
    }
}
