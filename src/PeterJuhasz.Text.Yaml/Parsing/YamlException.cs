namespace PeterJuhasz.Text.Yaml.Parsing;

public sealed class YamlException(string message, int line, int column) : Exception($"{message} (line {line}, column {column})")
{
	public string Reason { get; } = message;

	public int Line { get; } = line;

	public int Column { get; } = column;
}
