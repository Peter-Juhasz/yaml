using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Parsing;

namespace PeterJuhasz.Text.Yaml.Tests;

internal static class Yaml
{
	public static Node Root(string yaml) => Parser.Parse(yaml).Root;

	public static ScalarNode Plain(string value) => new(Value: value);

	public static ScalarNode Single(string value) => new(Value: value, Style: ScalarStyle.SingleQuoted);

	public static ScalarNode Double(string value) => new(Value: value, Style: ScalarStyle.DoubleQuoted);

	public static ScalarNode Literal(string value) => new(Value: value, Style: ScalarStyle.Literal);

	public static ScalarNode Folded(string value) => new(Value: value, Style: ScalarStyle.Folded);

	public static ScalarNode Empty => ScalarNode.Empty;

	public static SequenceNode Seq(params Node[] items) => new(Items: [.. items]);

	public static MappingNode Map(params (Node Key, Node Value)[] entries) => new(Entries: [.. entries.Select(static e => new MappingEntry(Key: e.Key, Value: e.Value))]);

	public static MappingNode Map(params (string Key, Node Value)[] entries) => Map([.. entries.Select(static e => ((Node)Plain(e.Key), e.Value))]);
}
