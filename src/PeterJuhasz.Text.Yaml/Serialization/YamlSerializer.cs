using System.Diagnostics.CodeAnalysis;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Parsing;
using PeterJuhasz.Text.Yaml.Serialization.Converters;

namespace PeterJuhasz.Text.Yaml.Serialization;

// deserializes the object model into mutable .NET objects, which have a public parameterless constructor and settable properties
public static class YamlSerializer
{
	internal const string UnreferencedCodeMessage = "Deserialization reads the members of the target type using reflection, which may be removed by trimming.";
	internal const string DynamicCodeMessage = "Deserialization may construct generic collection types and methods at runtime.";

	[RequiresUnreferencedCode(UnreferencedCodeMessage)]
	[RequiresDynamicCode(DynamicCodeMessage)]
	public static T? Deserialize<T>(string yaml, YamlSerializerOptions? options = null) => Deserialize<T>(Parser.Parse(yaml), options);

	[RequiresUnreferencedCode(UnreferencedCodeMessage)]
	[RequiresDynamicCode(DynamicCodeMessage)]
	public static T? Deserialize<T>(Node node, YamlSerializerOptions? options = null) => (T?)Deserialize(node, typeof(T), options);

	[RequiresUnreferencedCode(UnreferencedCodeMessage)]
	[RequiresDynamicCode(DynamicCodeMessage)]
	public static object? Deserialize(string yaml, Type type, YamlSerializerOptions? options = null) => Deserialize(Parser.Parse(yaml), type, options);

	[RequiresUnreferencedCode(UnreferencedCodeMessage)]
	[RequiresDynamicCode(DynamicCodeMessage)]
	public static object? Deserialize(Node node, Type type, YamlSerializerOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(node);
		ArgumentNullException.ThrowIfNull(type);

		return YamlConverter.ReadValue(node is DocumentNode document ? document.Root : node, type, options ?? YamlSerializerOptions.Default);
	}
}
