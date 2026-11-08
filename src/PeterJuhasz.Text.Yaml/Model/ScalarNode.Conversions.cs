using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PeterJuhasz.Text.Yaml.Model;

// values are resolved according to the yaml 1.2 core schema, so only plain scalars are converted
public sealed partial record class ScalarNode
{
	private const NumberStyles FloatStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

	public bool TryGetBoolean(out bool value)
	{
		(var success, value) = (Style, Value) switch
		{
			(ScalarStyle.Plain, "true" or "True" or "TRUE") => (true, true),
			(ScalarStyle.Plain, "false" or "False" or "FALSE") => (true, false),
			_ => (false, false),
		};
		return success;
	}

	public bool TryGetInt32(out int value)
	{
		if (TryGetInt64(out var result) && result is >= Int32.MinValue and <= Int32.MaxValue)
		{
			value = (int)result;
			return true;
		}

		value = 0;
		return false;
	}

	public bool TryGetInt64(out long value)
	{
		value = 0;
		if (Style is not ScalarStyle.Plain || Value is not [.., var last])
		{
			return false;
		}

		// the number parser ignores trailing null characters, so the last character is checked to be a digit
		if (Value.StartsWith("0x", StringComparison.Ordinal))
		{
			// the unsigned parser is used, because the signed one would read the sign bit from the digits
			if (!Char.IsAsciiHexDigit(last) || !UInt64.TryParse(Value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hexadecimal) || hexadecimal > Int64.MaxValue)
			{
				return false;
			}

			value = (long)hexadecimal;
			return true;
		}

		if (Value.StartsWith("0o", StringComparison.Ordinal))
		{
			return TryParseOctal(Value.AsSpan(2), out value);
		}

		return Char.IsAsciiDigit(last) && Int64.TryParse(Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
	}

	public bool TryGetDouble(out double value)
	{
		if (TryGetInt64(out var integer))
		{
			value = integer;
			return true;
		}

		value = 0;
		if (Style is not ScalarStyle.Plain)
		{
			return false;
		}

		switch (Value)
		{
			case ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF":
				value = Double.PositiveInfinity;
				return true;

			case "-.inf" or "-.Inf" or "-.INF":
				value = Double.NegativeInfinity;
				return true;

			case ".nan" or ".NaN" or ".NAN":
				value = Double.NaN;
				return true;
		}

		return EndsWithDigitOrPoint(Value) && Double.TryParse(Value, FloatStyles, CultureInfo.InvariantCulture, out value);
	}

	public bool TryGetDecimal(out decimal value)
	{
		if (TryGetInt64(out var integer))
		{
			value = integer;
			return true;
		}

		value = 0;
		return Style is ScalarStyle.Plain && EndsWithDigitOrPoint(Value) && Decimal.TryParse(Value, FloatStyles, CultureInfo.InvariantCulture, out value);
	}

	// the core schema types are resolved by the schema aware methods above, any other type is parsed from the value regardless of the style
	public bool TryGet<T>([NotNullWhen(true)] out T? value) where T : IParsable<T>
	{
		// the type checks are constants after specialization, so the boxing casts are elided for value types
		if (typeof(T) == typeof(bool))
		{
			var success = TryGetBoolean(out var result);
			value = (T)(object)result;
			return success;
		}

		if (typeof(T) == typeof(int))
		{
			var success = TryGetInt32(out var result);
			value = (T)(object)result;
			return success;
		}

		if (typeof(T) == typeof(long))
		{
			var success = TryGetInt64(out var result);
			value = (T)(object)result;
			return success;
		}

		if (typeof(T) == typeof(double))
		{
			var success = TryGetDouble(out var result);
			value = (T)(object)result;
			return success;
		}

		if (typeof(T) == typeof(decimal))
		{
			var success = TryGetDecimal(out var result);
			value = (T)(object)result;
			return success;
		}

		return T.TryParse(Value, CultureInfo.InvariantCulture, out value);
	}

	// the number parser accepts the infinity and not a number symbols in any case, and ignores trailing null characters, none of which ends with a digit or a point
	private static bool EndsWithDigitOrPoint(string value) => value is [.., >= '0' and <= '9' or '.'];

	// there is no number style for octal numbers
	private static bool TryParseOctal(ReadOnlySpan<char> digits, out long value)
	{
		value = 0;
		if (digits.IsEmpty)
		{
			return false;
		}

		var result = 0L;
		foreach (var c in digits)
		{
			// an invalid digit, or the next shift would overflow
			if (c is not (>= '0' and <= '7') || result > Int64.MaxValue >> 3)
			{
				return false;
			}

			result = (result << 3) | (long)(c - '0');
		}

		value = result;
		return true;
	}
}
