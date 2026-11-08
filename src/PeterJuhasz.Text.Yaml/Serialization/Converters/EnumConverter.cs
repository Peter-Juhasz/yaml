using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using PeterJuhasz.Text.Yaml.Model;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

// reads enums from integers, or from member names or their enum member values, which are comma separated for flags,
// and writes them by their names, or as integers when they have no name
[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal sealed class EnumConverter : YamlConverter
{
	private readonly FrozenDictionary<string, ulong>.AlternateLookup<ReadOnlySpan<char>> names;
	private readonly bool isFlags;
	private readonly FrozenDictionary<ulong, string> namesByValue;

	// the members of flags, from the largest value, so combined members are preferred over their parts
	private readonly ImmutableArray<(ulong Value, string Name)> flags;
	private readonly bool isSigned;

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

		// members are written by their explicit value, or by their name converted by the policy, the first member of a value wins
		var namesByValue = new Dictionary<ulong, string>();
		foreach (var field in fields)
		{
			var name = field.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? namingPolicy?.ConvertName(field.Name) ?? field.Name;
			namesByValue.TryAdd(ToUInt64(field.GetValue(null)!), name);
		}

		this.namesByValue = namesByValue.ToFrozenDictionary();
		flags = [.. this.namesByValue.Where(static p => p.Key is not 0).OrderByDescending(static p => p.Key).Select(static p => (p.Key, p.Value))];
		isSigned = Type.GetTypeCode(type) is TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64;
	}

	public override Node Write(object value, YamlSerializerOptions options, int depth)
	{
		var bits = ToUInt64(value);
		if (namesByValue.TryGetValue(bits, out var name))
		{
			return ScalarConverter.Text(name);
		}

		if (isFlags && bits is not 0)
		{
			var remaining = bits;
			var parts = new List<string>();
			foreach (var (flag, flagName) in flags)
			{
				if ((remaining & flag) == flag)
				{
					parts.Add(flagName);
					remaining &= ~flag;
				}
			}

			// the members are listed from the smallest value, like by the enum formatter
			if (remaining is 0)
			{
				parts.Reverse();
				return ScalarConverter.Text(String.Join(", ", parts));
			}
		}

		// values without a name are written as integers
		return new ScalarNode(Value: isSigned ? unchecked((long)bits).ToString(CultureInfo.InvariantCulture) : bits.ToString(CultureInfo.InvariantCulture));
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
