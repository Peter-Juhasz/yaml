using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

// a member of an enum, the explicit name is the value of its enum member attribute
internal readonly record struct EnumMemberEntry(string Name, string? ExplicitName, ulong Value);

// reads enums from integers, or from member names or their enum member values, which are comma separated for flags,
// and writes them by their names, or as integers when they have no name, the values are the bits of the enum, so negative values of signed enums round trip
internal sealed class EnumTable
{
	private readonly FrozenDictionary<string, ulong>.AlternateLookup<ReadOnlySpan<char>> names;
	private readonly bool isFlags;
	private readonly FrozenDictionary<ulong, string> namesByValue;

	// the members of flags, from the largest value, so combined members are preferred over their parts
	private readonly ImmutableArray<(ulong Value, string Name)> flags;
	private readonly bool isSigned;

	public EnumTable(ReadOnlySpan<EnumMemberEntry> members, bool isFlags, bool isSigned, YamlNamingPolicy? namingPolicy)
	{
		// an explicit value replaces the name of the member, and is added first, so it wins over the names of other members
		var names = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
		foreach (var member in members)
		{
			if (member.ExplicitName is { } explicitName)
			{
				names.TryAdd(explicitName, member.Value);
			}
		}

		foreach (var member in members)
		{
			if (member.ExplicitName is not null)
			{
				continue;
			}

			names.TryAdd(member.Name, member.Value);
			if (namingPolicy is not null)
			{
				names.TryAdd(namingPolicy.ConvertName(member.Name), member.Value);
			}
		}

		this.names = names.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase).GetAlternateLookup<ReadOnlySpan<char>>();
		this.isFlags = isFlags;

		// members are written by their explicit value, or by their name converted by the policy, the first member of a value wins
		var namesByValue = new Dictionary<ulong, string>();
		foreach (var member in members)
		{
			namesByValue.TryAdd(member.Value, member.ExplicitName ?? namingPolicy?.ConvertName(member.Name) ?? member.Name);
		}

		this.namesByValue = namesByValue.ToFrozenDictionary();
		flags = [.. this.namesByValue.Where(static p => p.Key is not 0).OrderByDescending(static p => p.Key).Select(static p => (p.Key, p.Value))];
		this.isSigned = isSigned;
	}

	public void Write(IYamlEmitter emitter, ulong bits)
	{
		if (namesByValue.TryGetValue(bits, out var name))
		{
			emitter.WriteString(name);
			return;
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
				emitter.WriteString(String.Join(", ", parts));
				return;
			}
		}

		// values without a name are written as integers
		emitter.WriteScalar(isSigned ? unchecked((long)bits).ToString(CultureInfo.InvariantCulture) : bits.ToString(CultureInfo.InvariantCulture), ScalarStyle.Plain);
	}

	public bool TryRead(ScalarNode scalar, out ulong value)
	{
		if (scalar.TryGetInt64(out var number))
		{
			value = unchecked((ulong)number);
			return true;
		}

		if (!isFlags)
		{
			return names.TryGetValue(scalar.Value, out value);
		}

		value = 0;
		var span = scalar.Value.AsSpan();
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
}
