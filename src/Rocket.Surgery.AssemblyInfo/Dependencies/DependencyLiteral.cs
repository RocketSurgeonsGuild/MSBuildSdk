using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rocket.Surgery.AssemblyInfo.Dependencies;

/// <summary>
/// Turns a value read from a referenced assembly into a C# literal. The text comes from someone else's metadata, so it is
/// never pasted into the generated code: strings are escaped, and typed values are parsed as a literal and written again.
/// </summary>
internal static class DependencyLiteral
{
    /// <summary>A C# literal for <paramref name="text" /> as <paramref name="canonicalType" />; false when the text is not a valid value of that type.</summary>
    public static bool TryFormat(string canonicalType, string text, [NotNullWhen(true)] out string? literal)
    {
        literal = null;
        switch (canonicalType)
        {
            case "string":
                literal = SymbolDisplay.FormatLiteral(text, quote: true);
                return true;
            case "bool":
                if (!bool.TryParse(text.Trim(), out var boolean))
                    return false;

                literal = boolean ? "true" : "false";
                return true;
        }

        if (!TryReadNumber(text, out var number, out var digits, out var negative))
            return false;

        var sign = negative ? "-" : "";
        switch (canonicalType)
        {
            case "sbyte": return TryFormatIntegral(number, negative, sbyte.MinValue, sbyte.MaxValue, out literal);
            case "byte": return TryFormatIntegral(number, negative, byte.MinValue, byte.MaxValue, out literal);
            case "short": return TryFormatIntegral(number, negative, short.MinValue, short.MaxValue, out literal);
            case "ushort": return TryFormatIntegral(number, negative, ushort.MinValue, ushort.MaxValue, out literal);
            case "int": return TryFormatIntegral(number, negative, int.MinValue, int.MaxValue, out literal);
            case "uint": return TryFormatIntegral(number, negative, uint.MinValue, uint.MaxValue, out literal);
            case "long": return TryFormatIntegral(number, negative, long.MinValue, long.MaxValue, out literal);
            case "ulong": return TryFormatIntegral(number, negative, ulong.MinValue, ulong.MaxValue, out literal);
            case "float":
                if (!float.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var single) || float.IsInfinity(single))
                    return false;

                literal = sign + single.ToString("R", CultureInfo.InvariantCulture) + "F";
                return true;
            case "double":
                if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var @double) || double.IsInfinity(@double))
                    return false;

                literal = sign + @double.ToString("R", CultureInfo.InvariantCulture);
                return true;
            case "decimal":
                if (!decimal.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var @decimal))
                    return false;

                literal = sign + @decimal.ToString(CultureInfo.InvariantCulture) + "M";
                return true;
            default:
                return false;
        }
    }

    // The token's value is whatever the literal's own type is (int, uint, long, ulong, float, double or decimal), so hex,
    // digit separators and suffixes all come out as numbers. Floating point values are read again from the digits, so a
    // decimal does not take a detour through a double. Anything that is not a plain literal is refused.
    private static bool TryReadNumber(string text, [NotNullWhen(true)] out object? number, out string digits, out bool negative)
    {
        number = null;
        digits = "";
        negative = false;

        var expression = SyntaxFactory.ParseExpression(text.Trim());
        if (expression.ContainsDiagnostics)
            return false;

        if (expression is PrefixUnaryExpressionSyntax prefix && prefix.Kind() is SyntaxKind.UnaryMinusExpression or SyntaxKind.UnaryPlusExpression)
        {
            negative = prefix.Kind() == SyntaxKind.UnaryMinusExpression;
            expression = prefix.Operand;
        }

        if (expression is not LiteralExpressionSyntax literal || literal.Kind() != SyntaxKind.NumericLiteralExpression)
            return false;

        number = literal.Token.Value;
        if (number is null)
            return false;

        digits = literal.Token.Text.Replace("_", "");
        if (number is float or double or decimal)
            digits = digits.TrimEnd('f', 'F', 'd', 'D', 'm', 'M');
        else
            digits = Convert.ToDecimal(number, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

        return true;
    }

    private static bool TryFormatIntegral(object number, bool negative, decimal minimum, decimal maximum, [NotNullWhen(true)] out string? literal)
    {
        literal = null;
        // Only whole numbers: 1.5 is not an int.
        if (number is not (int or uint or long or ulong))
            return false;

        var value = Convert.ToDecimal(number, CultureInfo.InvariantCulture);
        if (negative)
            value = -value;

        if (value < minimum || value > maximum)
            return false;

        literal = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }
}
