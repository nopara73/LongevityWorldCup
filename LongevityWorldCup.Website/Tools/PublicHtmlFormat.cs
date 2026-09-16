using System.Globalization;
using System.Numerics;

namespace LongevityWorldCup.Website.Tools;

public static class PublicHtmlFormat
{
    // Match JavaScript toFixed for the exact binary value, including halfway
    // cases. .NET's default midpoint rounding can change a displayed score.
    public static string Fixed(double? value, int decimals = 1)
    {
        if (value is not double number || !double.IsFinite(number)) return "";
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, 20);
        var bits = BitConverter.DoubleToUInt64Bits(Math.Abs(number));
        var exponent = (int)((bits >> 52) & 0x7ff);
        var mantissa = bits & 0x000fffffffffffff;
        var binaryPower = exponent == 0 ? -1074 : exponent - 1075;
        if (exponent != 0) mantissa |= 1UL << 52;
        var scaled = new BigInteger(mantissa) * BigInteger.Pow(10, decimals);
        if (binaryPower >= 0) scaled <<= binaryPower;
        else
        {
            var divisor = BigInteger.One << -binaryPower;
            scaled = BigInteger.DivRem(scaled, divisor, out var remainder);
            if (remainder * 2 >= divisor) scaled++;
        }
        var digits = scaled.ToString(CultureInfo.InvariantCulture).PadLeft(decimals + 1, '0');
        if (decimals > 0) digits = digits.Insert(digits.Length - decimals, ".");
        return (number < 0 ? "-" : "") + digits;
    }

    public static string Signed(double? value, int decimals = 1) =>
        (value > 0 ? "+" : "") + Fixed(value, decimals);
}
