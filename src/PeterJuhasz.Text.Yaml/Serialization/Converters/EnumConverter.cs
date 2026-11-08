using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using PeterJuhasz.Text.Yaml.Model;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

// reads enums from integers, or from member names or their enum member values, which are comma separated for flags
[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal sealed class EnumConverter : YamlConverter
{
	private readonly FrozenDictionary<string, ulong>.AlternateLookup<ReadOnlySpan<char>> names;
	private readonly bool isFlags;

	public EnumConverter(Type type, YamlNamingPolicy? namingPolicy) : base(type)
	{
		var names = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
		var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);

		// an explicit value replaces the name of the member, and is added first, so it wins over the names of other members
		var renamed = new HashSet<FieldInfo>();
		foreach (var field in fields)
		{
			if (field.GetCustomAttribute<EnumMemberAttribute>() is { Value: { } explicitName })
			{
				names.TryAdd(explicitName, ToUInt64(field.GetValue(null)!));
				renamed.Add(field);
			}
		}

		foreach (var field in fields)
		{
			if (renamed.Contains(field))
			{
				continue;
			}

			var value = ToUInt64(field.GetValue(null)!);
			names.TryAdd(field.Name, value);
			if (namingPolicy is not null)
			{
				names.TryAdd(namingPolicy.ConvertName(field.Name), value);
			}
		}

		this.names = names.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase).GetAlternateLookup<ReadOnlySpan<char>>();
		isFlags = type.IsDefined(typeof(FlagsAttribute), inherit: false);
	}

	public override object? Read(Node node, YamlSerializerOptions options)
	{
		if (node is not ScalarNode scalar)
		{
			throw Mismatch(node);
		}

		if (scalar.TryGetInt64(out var number))
		{
			return Enum.ToObject(Type, number);
		}

		return TryParseNames(scalar.Value, out var value) ? Enum.ToObject(Type, value) : throw Mismatch(node);
	}

	private bool TryParseNames(string text, out ulong value)
	{
		if (!isFlags)
		{
			return names.TryGetValue(text, out value);
		}

		value = 0;
		var span = text.AsSpan();
		foreach (var range in span.Split(','))
		{
			if (!names.TryGetValue(span[range].Trim(), out var flag))
			{
				return false;
			}

			value |= flag;
		}

		return true;
	}

	// the bits of the value are kept, so negative values of signed enums round trip
	private static ulong ToUInt64(object value) => Type.GetTypeCode(value.GetType()) switch
	{
		TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture)),
		_ => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
	};
}
