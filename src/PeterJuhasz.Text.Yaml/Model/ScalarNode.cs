namespace PeterJuhasz.Text.Yaml.Model;

public sealed partial record class ScalarNode(string Value, ScalarStyle Style = ScalarStyle.Plain) : Node
{
	public static ScalarNode Empty { get; } = new(Value: String.Empty);

	public bool IsNull => Style is ScalarStyle.Plain && Value is "" or "~" or "null" or "Null" or "NULL";
}
