using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace PeterJuhasz.Text.Yaml.Model;


public abstract record class Node();


public sealed record class DocumentNode(Node Root) : Node;


public sealed partial record class ScalarNode(string Value, ScalarStyle Style = ScalarStyle.Plain) : Node
{
	public static ScalarNode Empty { get; } = new(Value: String.Empty);

	public bool IsNull => Style is ScalarStyle.Plain && Value is "" or "~" or "null" or "Null" or "NULL";
}

public enum ScalarStyle
{
	Plain,
	SingleQuoted,
	DoubleQuoted,
	Literal,
	Folded,
}


public sealed record class SequenceNode(ImmutableArray<Node> Items) : Node
{
	public bool Equals(SequenceNode? other) => other is not null && Items.AsSpan().SequenceEqual(other.Items.AsSpan());

	public override int GetHashCode()
	{
		var hash = new HashCode();
		foreach (var item in Items.AsSpan())
		{
			hash.Add(item);
		}
		return hash.ToHashCode();
	}
}


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

public sealed record class MappingEntry(Node Key, Node Value);
