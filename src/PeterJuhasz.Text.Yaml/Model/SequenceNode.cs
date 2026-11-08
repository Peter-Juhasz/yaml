using System.Collections.Immutable;

namespace PeterJuhasz.Text.Yaml.Model;

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
