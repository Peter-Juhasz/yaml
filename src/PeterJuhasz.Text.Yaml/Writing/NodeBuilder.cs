using System.Collections.Immutable;
using PeterJuhasz.Text.Yaml.Model;

namespace PeterJuhasz.Text.Yaml.Writing;

// builds the object model from the written nodes, entries of mappings are collected as alternating keys and values
internal sealed class NodeBuilder : IYamlEmitter
{
	private static readonly ScalarNode Null = new(Value: "null");

	private readonly Stack<ImmutableArray<Node>.Builder> open = [];
	private Node? root;

	public Node Root => root ?? throw new InvalidOperationException("No node was written.");

	public void WriteStartMapping() => open.Push(ImmutableArray.CreateBuilder<Node>());

	public void WriteEndMapping()
	{
		var nodes = open.Pop();
		var entries = ImmutableArray.CreateBuilder<MappingEntry>(nodes.Count / 2);
		for (var i = 0; i + 1 < nodes.Count; i += 2)
		{
			entries.Add(new(Key: nodes[i], Value: nodes[i + 1]));
		}
		Add(new MappingNode(Entries: entries.MoveToImmutable()));
	}

	public void WriteStartSequence() => open.Push(ImmutableArray.CreateBuilder<Node>());

	public void WriteEndSequence() => Add(new SequenceNode(Items: open.Pop().DrainToImmutable()));

	public void WriteScalar(string value, ScalarStyle style) => Add(value is "null" && style is ScalarStyle.Plain ? Null : new ScalarNode(Value: value, Style: style));

	public void WriteString(string value) => Add(new ScalarNode(Value: value, Style: SyntaxFacts.GetTextStyle(value)));

	public void WriteNode(Node node) => Add(node);

	private void Add(Node node)
	{
		if (open.TryPeek(out var nodes))
		{
			nodes.Add(node);
		}
		else
		{
			root = node;
		}
	}
}
