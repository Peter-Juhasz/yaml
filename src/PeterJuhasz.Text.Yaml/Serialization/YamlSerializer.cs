using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Parsing;
using PeterJuhasz.Text.Yaml.Serialization.Converters;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Serialization;

// deserializes the object model into .NET objects, and serializes .NET objects into the object model
public static class YamlSerializer
{
	internal const string UnreferencedCodeMessage = "Serialization accesses the members of the types using reflection, which may be removed by trimming.";
	internal const string DynamicCodeMessage = "Serialization may construct generic collection types and methods at runtime.";

	[RequiresUnreferencedCode(UnreferencedCodeMessage)]
	[RequiresDynamicCode(DynamicCodeMessage)]
	public static string Serialize<T>(T value, YamlSerializerOptions? options = null) => Serialize(value, typeof(T), options);

	// the writes are buffered, and the text is formatted by the formatting options of the serializer options
	[RequiresUnreferencedCode(UnreferencedCodeMessage)]
	[RequiresDynamicCode(DynamicCodeMessage)]
	public static string Serialize(object? value, Type type, YamlSerializerOptions? options = null)
	{
		options ??= YamlSerializerOptions.Default;
		using var lease = ArrayBufferWriterPool<char>.GetPooledObject(out var buffer);
		Serialize(YamlWriter.Create(buffer, options.FormattingOptions), value, type, options);
		return buffer.WrittenSpan.ToString();
	}

	[RequiresUnreferencedCode(UnreferencedCodeMessage)]
	[RequiresDynamicCode(DynamicCodeMessage)]
	public static void Serialize<T, TWriter>(YamlWriter<TWriter> writer, T value, YamlSerializerOptions? options = null) where TWriter : IBufferWriter<char> => Serialize(writer, value, typeof(T), options);

	// writes the value as a node at the current position of the writer, like the root of a document, or the value of a key
	[RequiresUnreferencedCode(UnreferencedCodeMessage)]
	[RequiresDynamicCode(DynamicCodeMessage)]
	public static void Serialize<TWriter>(YamlWriter<TWriter> writer, object? value, Type type, YamlSerializerOptions? options = null) where TWriter : IBufferWriter<char>
	{
		ArgumentNullException.ThrowIfNull(writer);
		Write(writer, value, type, options);
	}

	[RequiresUnreferencedCode(UnreferencedCodeMessage)]
	[RequiresDynamicCode(DynamicCodeMessage)]
	public static Node SerializeToNode<T>(T value, YamlSerializerOptions? options = null) => SerializeToNode(value, typeof(T), options);

	// the type selects how the value is written, just like the type of deserialization selects how it is read
	[RequiresUnreferencedCode(UnreferencedCodeMessage)]
	[RequiresDynamicCode(DynamicCodeMessage)]
	public static Node SerializeToNode(object? value, Type type, YamlSerializerOptions? options = null)
	{
		var builder = new NodeBuilder();
		Write(builder, value, type, options);
		return builder.Root;
	}

	[RequiresUnreferencedCode(UnreferencedCodeMessage)]
	[RequiresDynamicCode(DynamicCodeMessage)]
	private static void Write(IYamlEmitter emitter, object? value, Type type, YamlSerializerOptions? options)
	{
		ArgumentNullException.ThrowIfNull(type);
		if (value is not null && !type.IsInstanceOfType(value))
		{
			throw new ArgumentException($"The value of type '{value.GetType()}' is not an instance of type '{type}'.", nameof(value));
		}

		YamlConverter.WriteValue(emitter, value, type, options ?? YamlSerializerOptions.Default, depth: 0);
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
