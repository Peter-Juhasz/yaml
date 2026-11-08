namespace PeterJuhasz.Text.Yaml.Writing;

/// <param name="IndentSize">The number of spaces nested mappings and block scalars are indented with, at least one.</param>
/// <param name="IndentSequences">Whether sequences in mappings are indented, like <c>key:\n  - item</c>, or written at the level of the key, like <c>key:\n- item</c>.</param>
/// <param name="NewLine">The line ending every line is written with, either <c>\n</c> or <c>\r\n</c>.</param>
public sealed record class YamlWriterFormattingOptions(
	int IndentSize = 2,
	bool IndentSequences = true,
	string NewLine = "\n"
)
{
	public static readonly YamlWriterFormattingOptions Default = new();
}
