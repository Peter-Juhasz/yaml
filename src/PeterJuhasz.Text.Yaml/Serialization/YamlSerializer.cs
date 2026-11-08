using System.Diagnostics.CodeAnalysis;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Parsing;
using PeterJuhasz.Text.Yaml.Serialization.Converters;

namespace PeterJuhasz.Text.Yaml.Serialization;

// deserializes the object model into .NET objects, and serializes .NET objects into the object model
public static class YamlSerializer
{
	internal const string UnreferencedCodeMessage = "Serialization accesses the members of the types using reflection, which may be removed by trimming.";
	internal const string DynamicCodeMessage = "Serialization may construct generic collection types and methods at runtime.";

	[RequiresUnreferencedCode(UnreferencedCodeMessage)]
	[RequiresDynamicCode(DynamicCodeMessage)]
	public static Node SerializeToNode<T>(T value, YamlSerializerOptions? options = null) => SerializeToNode(value, typeof(T), options);

	// the type selects how the value is written, just like the type of deserialization selects how it is read
	[RequiresUnreferencedCode(UnreferencedCodeMessage)]
	[RequiresDynamicCode(DynamicCodeMessage)]
	public static Node SerializeToNode(object? value, Type type, YamlSerializerOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(type);
		if (value is not null && !type.IsInstanceOfType(value))
		{
			throw new ArgumentException($"The value of type '{value.GetType()}' is not an instance of type '{type}'.", nameof(value));
		}

		return YamlConverter.WriteValue(value, type, options ?? YamlSerializerOptions.Default, depth: 0);
	}

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
