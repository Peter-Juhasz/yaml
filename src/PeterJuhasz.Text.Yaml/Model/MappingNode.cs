using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace PeterJuhasz.Text.Yaml.Model;

public sealed record class MappingNode(ImmutableArray<MappingEntry> Entries) : Node
{
	public Node this[string key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException($"The key '{key}' was not found in the mapping.");

	public bool TryGetValue(string key, [NotNullWhen(true)] out Node? value)
	{
		foreach (var entry in Entries.AsSpan())
		{
			if (entry.Key is ScalarNode scalar && scalar.Value == key)
			{
				value = entry.Value;
				return true;
			}
		}

		value = null;
		return false;
	}

	public bool Equals(MappingNode? other) => other is not null && Entries.AsSpan().SequenceEqual(other.Entries.AsSpan());

	public override int GetHashCode()
	{
		var hash = new HashCode();
		foreach (var entry in Entries.AsSpan())
		{
			hash.Add(entry);
		}
		return hash.ToHashCode();
	}
}
