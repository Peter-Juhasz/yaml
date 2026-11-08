using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Xml;
using PeterJuhasz.Text.Yaml.Model;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

internal delegate bool ScalarParser(ScalarNode scalar, out object? value);

internal delegate ScalarNode ScalarFormatter(object value);

[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal sealed class ScalarConverter(Type type, ScalarParser parser) : YamlConverter(type)
{
	// booleans and numbers follow the yaml 1.2 core schema, so they are read only from plain scalars, any other type is read from the value regardless of the style
	private static readonly FrozenDictionary<Type, ScalarParser> Parsers = new Dictionary<Type, ScalarParser>
	{
		[typeof(string)] = static (ScalarNode scalar, out object? value) => Return(scalar.Value, out value),
		[typeof(char)] = static (ScalarNode scalar, out object? value) => scalar.Value is [var c] ? Return(c, out value) : Fail(out value),
		[typeof(bool)] = static (ScalarNode scalar, out object? value) => scalar.TryGetBoolean(out var result) ? Return(result, out value) : Fail(out value),
		[typeof(byte)] = Integer<byte>,
		[typeof(sbyte)] = Integer<sbyte>,
		[typeof(short)] = Integer<short>,
		[typeof(ushort)] = Integer<ushort>,
		[typeof(int)] = Integer<int>,
		[typeof(uint)] = Integer<uint>,
		[typeof(long)] = Integer<long>,
		[typeof(ulong)] = Integer<ulong>,
		[typeof(nint)] = Integer<nint>,
		[typeof(nuint)] = Integer<nuint>,
		[typeof(Int128)] = Integer<Int128>,
		[typeof(UInt128)] = Integer<UInt128>,
		[typeof(BigInteger)] = Integer<BigInteger>,
		[typeof(Half)] = Float<Half>,
		[typeof(float)] = Float<float>,
		[typeof(double)] = Float<double>,
		[typeof(decimal)] = static (ScalarNode scalar, out object? value) => scalar.TryGetDecimal(out var result) ? Return(result, out value) : Fail(out value),
		[typeof(DateTime)] = static (ScalarNode scalar, out object? value) => DateTime.TryParse(scalar.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result) ? Return(result, out value) : Fail(out value),
		[typeof(DateTimeOffset)] = static (ScalarNode scalar, out object? value) => DateTimeOffset.TryParse(scalar.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result) ? Return(result, out value) : Fail(out value),
		[typeof(DateOnly)] = static (ScalarNode scalar, out object? value) => DateOnly.TryParse(scalar.Value, CultureInfo.InvariantCulture, out var result) ? Return(result, out value) : Fail(out value),
		[typeof(TimeOnly)] = static (ScalarNode scalar, out object? value) => TimeOnly.TryParse(scalar.Value, CultureInfo.InvariantCulture, out var result) ? Return(result, out value) : Fail(out value),
		[typeof(TimeSpan)] = static (ScalarNode scalar, out object? value) => TryParseTimeSpan(scalar.Value, out var result) ? Return(result, out value) : Fail(out value),
		[typeof(Uri)] = static (ScalarNode scalar, out object? value) => Uri.TryCreate(scalar.Value, UriKind.RelativeOrAbsolute, out var result) ? Return(result, out value) : Fail(out value),
		[typeof(Guid)] = static (ScalarNode scalar, out object? value) => Guid.TryParse(scalar.Value, out var result) ? Return(result, out value) : Fail(out value),
		[typeof(Version)] = static (ScalarNode scalar, out object? value) => Version.TryParse(scalar.Value, out var result) ? Return(result, out value) : Fail(out value),
	}.ToFrozenDictionary();

	// the values are written in the formats accepted by the parsers above, using the invariant culture
	private static readonly FrozenDictionary<Type, ScalarFormatter> Formatters = new Dictionary<Type, ScalarFormatter>
	{
		[typeof(string)] = static value => Text((string)value),
		[typeof(char)] = static value => Text(((char)value).ToString()),
		[typeof(bool)] = static value => new(Value: (bool)value ? "true" : "false"),
		[typeof(byte)] = Number,
		[typeof(sbyte)] = Number,
		[typeof(short)] = Number,
		[typeof(ushort)] = Number,
		[typeof(int)] = Number,
		[typeof(uint)] = Number,
		[typeof(long)] = Number,
		[typeof(ulong)] = Number,
		[typeof(nint)] = Number,
		[typeof(nuint)] = Number,
		[typeof(Int128)] = Number,
		[typeof(UInt128)] = Number,
		[typeof(BigInteger)] = Number,
		[typeof(Half)] = Float<Half>,
		[typeof(float)] = Float<float>,
		[typeof(double)] = Float<double>,
		[typeof(decimal)] = Number,
		[typeof(DateTime)] = static value => Text(((DateTime)value).ToString("O", CultureInfo.InvariantCulture)),
		[typeof(DateTimeOffset)] = static value => Text(((DateTimeOffset)value).ToString("O", CultureInfo.InvariantCulture)),
		[typeof(DateOnly)] = static value => Text(((DateOnly)value).ToString("O", CultureInfo.InvariantCulture)),
		[typeof(TimeOnly)] = static value => Text(((TimeOnly)value).ToString("O", CultureInfo.InvariantCulture)),
		[typeof(TimeSpan)] = static value => Text(((TimeSpan)value).ToString("c", CultureInfo.InvariantCulture)),
		[typeof(Uri)] = static value => Text(((Uri)value).OriginalString),
		[typeof(Guid)] = static value => Text(((Guid)value).ToString("D", CultureInfo.InvariantCulture)),
		[typeof(Version)] = static value => Text(((Version)value).ToString()),
	}.ToFrozenDictionary();

	private readonly ScalarFormatter formatter = Formatters.GetValueOrDefault(type, Formattable);

	public static ScalarConverter? TryCreate(Type type)
	{
		if (Parsers.TryGetValue(type, out var parser))
		{
			return new(type, parser);
		}

		// any other type, which can be parsed from a string
		if (type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IParsable<>) && i.GetGenericArguments()[0] == type))
		{
			var method = typeof(ScalarConverter).GetMethod(nameof(Parsable), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type);
			return new(type, method.CreateDelegate<ScalarParser>());
		}

		return null;
	}

	public override object? Read(Node node, YamlSerializerOptions options) => node is ScalarNode scalar && parser(scalar, out var value) ? value : throw Mismatch(node);

	public override Node Write(object value, YamlSerializerOptions options, int depth) => formatter(value);

	// text is written as a plain scalar, unless it would be read as another type by the core schema, or it cannot be written as a plain scalar in block context
	public static ScalarNode Text(string value) => RequiresQuotes(value) ? new(Value: value, Style: ScalarStyle.DoubleQuoted) : new(Value: value);

	private static bool RequiresQuotes(string value)
	{
		if (value is [] or [' ' or '\t', ..] or [.., ' ' or '\t' or ':'])
		{
			return true;
		}

		// indicators cannot start a plain scalar, except for '-', '?' and ':' followed by a non-space character
		if (value is ['-' or '?' or ':'] or ['-' or '?' or ':', ' ' or '\t', ..] or ['#' or ',' or '[' or ']' or '{' or '}' or '&' or '*' or '!' or '|' or '>' or '\'' or '"' or '%' or '@' or '`', ..])
		{
			return true;
		}

		// document markers are recognized at the start of a line
		if (value.StartsWith("---", StringComparison.Ordinal) || value.StartsWith("...", StringComparison.Ordinal))
		{
			return true;
		}

		if (value.Contains(": ", StringComparison.Ordinal) || value.Contains(":\t", StringComparison.Ordinal) || value.Contains(" #", StringComparison.Ordinal) || value.Contains("\t#", StringComparison.Ordinal))
		{
			return true;
		}

		// line breaks and other non-printable characters can only be written with escape sequences
		for (var i = 0; i < value.Length; i++)
		{
			switch (value[i])
			{
				case < ' ' or '\u007F' or '\u0085' or '\u2028' or '\u2029' or '\uFEFF':
					return true;

				// lone surrogates are not valid characters
				case var c when Char.IsHighSurrogate(c):
					if (i + 1 >= value.Length || !Char.IsLowSurrogate(value[i + 1]))
					{
						return true;
					}
					i++;
					break;

				case var c when Char.IsLowSurrogate(c):
					return true;
			}
		}

		var plain = new ScalarNode(Value: value);
		return plain.IsNull || plain.TryGetBoolean(out _) || plain.TryGetDouble(out _);
	}

	private static ScalarNode Number(object value) => new(Value: ((IFormattable)value).ToString(format: null, CultureInfo.InvariantCulture));

	// the shortest text, which round trips, is written for finite values, and the core schema symbols for the rest
	private static ScalarNode Float<T>(object value) where T : IFloatingPointIeee754<T> => (T)value switch
	{
		var number when T.IsNaN(number) => new(Value: ".nan"),
		var number when T.IsPositiveInfinity(number) => new(Value: ".inf"),
		var number when T.IsNegativeInfinity(number) => new(Value: "-.inf"),
		var number => new(Value: number.ToString(format: null, CultureInfo.InvariantCulture)),
	};

	// any other parsable type is written by its invariant text
	private static ScalarNode Formattable(object value) => Text(value is IFormattable formattable ? formattable.ToString(format: null, CultureInfo.InvariantCulture) : value.ToString() ?? String.Empty);

	private static bool Integer<T>(ScalarNode scalar, out object? value) where T : IBinaryInteger<T>
	{
		// the core schema covers the range of 64 bit integers, larger values are parsed for the wider types
		if (scalar.TryGetInt64(out var integer))
		{
			return TryConvert<T, long>(integer, out value);
		}

		if (scalar is not { Style: ScalarStyle.Plain, Value: [.., var last] })
		{
			return Fail(out value);
		}

		if (scalar.Value.StartsWith("0x", StringComparison.Ordinal))
		{
			return Char.IsAsciiHexDigit(last) && UInt128.TryParse(scalar.Value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hexadecimal) ? TryConvert<T, UInt128>(hexadecimal, out value) : Fail(out value);
		}

		return Char.IsAsciiDigit(last) && T.TryParse(scalar.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) ? Return(parsed, out value) : Fail(out value);
	}

	// the value is in range of the target type, when it survives a round trip
	private static bool TryConvert<T, TSource>(TSource source, out object? value) where T : IBinaryInteger<T> where TSource : IBinaryInteger<TSource>
	{
		var result = T.CreateSaturating(source);
		return TSource.CreateSaturating(result) == source ? Return(result, out value) : Fail(out value);
	}

	private static bool Float<T>(ScalarNode scalar, out object? value) where T : IFloatingPointIeee754<T>
	{
		if (!scalar.TryGetDouble(out var number))
		{
			return Fail(out value);
		}

		// values out of the range of the narrower types are rejected, instead of becoming infinity
		var result = T.CreateTruncating(number);
		return T.IsInfinity(result) && !Double.IsInfinity(number) ? Fail(out value) : Return(result, out value);
	}

	private static bool Parsable<T>(ScalarNode scalar, out object? value) where T : IParsable<T> => scalar.TryGet<T>(out var result) ? Return(result, out value) : Fail(out value);

	// accepts the .NET format like '1.02:03:04', and the iso 8601 duration format like 'P1DT2H'
	private static bool TryParseTimeSpan(string text, out TimeSpan value)
	{
		if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value))
		{
			return true;
		}

		if (text is not (['P', ..] or ['-', 'P', ..]))
		{
			return false;
		}

		try
		{
			value = XmlConvert.ToTimeSpan(text);
			return true;
		}
		catch (Exception exception) when (exception is FormatException or OverflowException)
		{
			return false;
		}
	}

	private static bool Return<T>(T result, out object? value)
	{
		value = result;
		return true;
	}

	private static bool Fail(out object? value)
	{
		value = null;
		return false;
	}
}
