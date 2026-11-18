using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Xml;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

internal delegate bool ScalarParser<T>(ScalarNode scalar, [MaybeNullWhen(false)] out T value);

internal delegate void ScalarFormatter<T>(IYamlEmitter emitter, T value);

// the untyped view of a format, used by the reflection based converters
internal interface IScalarFormat
{
	bool TryParse(ScalarNode scalar, out object? value);

	void Write(IYamlEmitter emitter, object value);
}

internal sealed class ScalarFormat<T>(ScalarParser<T> parser, ScalarFormatter<T> formatter) : IScalarFormat
{
	public bool TryParse(ScalarNode scalar, [MaybeNullWhen(false)] out T value) => parser(scalar, out value);

	public void Write(IYamlEmitter emitter, T value) => formatter(emitter, value);

	bool IScalarFormat.TryParse(ScalarNode scalar, out object? value)
	{
		if (parser(scalar, out var result))
		{
			value = result;
			return true;
		}

		value = null;
		return false;
	}

	void IScalarFormat.Write(IYamlEmitter emitter, object value) => formatter(emitter, (T)value);
}

// the formats of the scalar types, shared by the reflection based converters and the generated metadata
internal static class ScalarFormats
{
	// booleans and numbers follow the yaml 1.2 core schema, so they are read only from plain scalars, any other type is read from the value regardless of the style,
	// and the values are written in the formats accepted by the parsers, using the invariant culture, numbers as plain scalars, any other value as text
	public static FrozenDictionary<Type, IScalarFormat> Known { get; } = new Dictionary<Type, IScalarFormat>
	{
		[typeof(string)] = new ScalarFormat<string>(ParseString, static (emitter, value) => emitter.WriteString(value)),
		[typeof(char)] = new ScalarFormat<char>(ParseChar, static (emitter, value) => emitter.WriteString(value.ToString())),
		[typeof(bool)] = new ScalarFormat<bool>(static (ScalarNode scalar, out bool value) => scalar.TryGetBoolean(out value), static (emitter, value) => emitter.WriteScalar(value ? "true" : "false", ScalarStyle.Plain)),
		[typeof(byte)] = Integer<byte>(),
		[typeof(sbyte)] = Integer<sbyte>(),
		[typeof(short)] = Integer<short>(),
		[typeof(ushort)] = Integer<ushort>(),
		[typeof(int)] = Integer<int>(),
		[typeof(uint)] = Integer<uint>(),
		[typeof(long)] = Integer<long>(),
		[typeof(ulong)] = Integer<ulong>(),
		[typeof(nint)] = Integer<nint>(),
		[typeof(nuint)] = Integer<nuint>(),
		[typeof(Int128)] = Integer<Int128>(),
		[typeof(UInt128)] = Integer<UInt128>(),
		[typeof(BigInteger)] = Integer<BigInteger>(),
		[typeof(Half)] = Float<Half>(),
		[typeof(float)] = Float<float>(),
		[typeof(double)] = Float<double>(),
		[typeof(decimal)] = new ScalarFormat<decimal>(static (ScalarNode scalar, out decimal value) => scalar.TryGetDecimal(out value), WriteNumber),
		[typeof(DateTime)] = new ScalarFormat<DateTime>(static (ScalarNode scalar, out DateTime value) => DateTime.TryParse(scalar.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value), static (emitter, value) => emitter.WriteString(value.ToString("O", CultureInfo.InvariantCulture))),
		[typeof(DateTimeOffset)] = new ScalarFormat<DateTimeOffset>(static (ScalarNode scalar, out DateTimeOffset value) => DateTimeOffset.TryParse(scalar.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value), static (emitter, value) => emitter.WriteString(value.ToString("O", CultureInfo.InvariantCulture))),
		[typeof(DateOnly)] = new ScalarFormat<DateOnly>(static (ScalarNode scalar, out DateOnly value) => DateOnly.TryParse(scalar.Value, CultureInfo.InvariantCulture, out value), static (emitter, value) => emitter.WriteString(value.ToString("O", CultureInfo.InvariantCulture))),
		[typeof(TimeOnly)] = new ScalarFormat<TimeOnly>(static (ScalarNode scalar, out TimeOnly value) => TimeOnly.TryParse(scalar.Value, CultureInfo.InvariantCulture, out value), static (emitter, value) => emitter.WriteString(value.ToString("O", CultureInfo.InvariantCulture))),
		[typeof(TimeSpan)] = new ScalarFormat<TimeSpan>(static (ScalarNode scalar, out TimeSpan value) => TryParseTimeSpan(scalar.Value, out value), static (emitter, value) => emitter.WriteString(value.ToString("c", CultureInfo.InvariantCulture))),
		[typeof(Uri)] = new ScalarFormat<Uri>(ParseUri, static (emitter, value) => emitter.WriteString(value.OriginalString)),
		[typeof(Guid)] = new ScalarFormat<Guid>(static (ScalarNode scalar, out Guid value) => Guid.TryParse(scalar.Value, out value), static (emitter, value) => emitter.WriteString(value.ToString("D", CultureInfo.InvariantCulture))),
		[typeof(Version)] = new ScalarFormat<Version>(ParseVersion, static (emitter, value) => emitter.WriteString(value.ToString())),
	}.ToFrozenDictionary();

	// any other type, which can be parsed from a string, is written by its invariant text
	public static ScalarFormat<T> Parsable<T>() where T : IParsable<T> => new(ParseParsable, WriteFormattable);

	private static ScalarFormat<T> Integer<T>() where T : IBinaryInteger<T> => new(ParseInteger, WriteNumber);

	private static ScalarFormat<T> Float<T>() where T : IFloatingPointIeee754<T> => new(ParseFloat, WriteFloat);

	private static bool ParseString(ScalarNode scalar, out string value)
	{
		value = scalar.Value;
		return true;
	}

	private static bool ParseChar(ScalarNode scalar, out char value)
	{
		(var success, value) = scalar.Value is [var c] ? (true, c) : (false, default);
		return success;
	}

	private static bool ParseUri(ScalarNode scalar, [MaybeNullWhen(false)] out Uri value) => Uri.TryCreate(scalar.Value, UriKind.RelativeOrAbsolute, out value);

	private static bool ParseVersion(ScalarNode scalar, [MaybeNullWhen(false)] out Version value) => Version.TryParse(scalar.Value, out value);

	private static bool ParseParsable<T>(ScalarNode scalar, [MaybeNullWhen(false)] out T value) where T : IParsable<T> => scalar.TryGet(out value);

	private static bool ParseInteger<T>(ScalarNode scalar, out T value) where T : IBinaryInteger<T>
	{
		// the core schema covers the range of 64 bit integers, larger values are parsed for the wider types
		if (scalar.TryGetInt64(out var integer))
		{
			return TryConvert(integer, out value);
		}

		value = T.Zero;
		if (scalar is not { Style: ScalarStyle.Plain, Value: [.., var last] })
		{
			return false;
		}

		if (scalar.Value.StartsWith("0x", StringComparison.Ordinal))
		{
			return Char.IsAsciiHexDigit(last) && UInt128.TryParse(scalar.Value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hexadecimal) && TryConvert(hexadecimal, out value);
		}

		return Char.IsAsciiDigit(last) && T.TryParse(scalar.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value!);
	}

	// the value is in range of the target type, when it survives a round trip
	private static bool TryConvert<T, TSource>(TSource source, out T value) where T : IBinaryInteger<T> where TSource : IBinaryInteger<TSource>
	{
		value = T.CreateSaturating(source);
		return TSource.CreateSaturating(value) == source;
	}

	private static bool ParseFloat<T>(ScalarNode scalar, out T value) where T : IFloatingPointIeee754<T>
	{
		value = T.Zero;
		if (!scalar.TryGetDouble(out var number))
		{
			return false;
		}

		// values out of the range of the narrower types are rejected, instead of becoming infinity
		value = T.CreateTruncating(number);
		return !T.IsInfinity(value) || Double.IsInfinity(number);
	}

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

	private static void WriteNumber<T>(IYamlEmitter emitter, T value) where T : IFormattable => emitter.WriteScalar(value.ToString(format: null, CultureInfo.InvariantCulture), ScalarStyle.Plain);

	// the shortest text, which round trips, is written for finite values, and the core schema symbols for the rest
	private static void WriteFloat<T>(IYamlEmitter emitter, T value) where T : IFloatingPointIeee754<T> => emitter.WriteScalar(value switch
	{
		var number when T.IsNaN(number) => ".nan",
		var number when T.IsPositiveInfinity(number) => ".inf",
		var number when T.IsNegativeInfinity(number) => "-.inf",
		var number => number.ToString(format: null, CultureInfo.InvariantCulture),
	}, ScalarStyle.Plain);

	private static void WriteFormattable<T>(IYamlEmitter emitter, T value) => emitter.WriteString(value is IFormattable formattable ? formattable.ToString(format: null, CultureInfo.InvariantCulture) : value?.ToString() ?? String.Empty);
}
