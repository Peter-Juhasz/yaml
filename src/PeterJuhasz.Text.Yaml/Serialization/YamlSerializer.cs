using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Parsing;
using PeterJuhasz.Text.Yaml.Serialization.Converters;
using PeterJuhasz.Text.Yaml.Serialization.Metadata;
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

	// the overloads below use the metadata generated for serializer contexts, so they work without reflection, the options are the ones of the context

	public static string Serialize<T>(T value, YamlTypeInfo<T> typeInfo)
	{
		ArgumentNullException.ThrowIfNull(typeInfo);
		using var lease = ArrayBufferWriterPool<char>.GetPooledObject(out var buffer);
		typeInfo.Write(YamlWriter.Create(buffer, typeInfo.Options.FormattingOptions), value, depth: 0);
		return buffer.WrittenSpan.ToString();
	}

	public static string Serialize(object? value, Type type, YamlSerializerContext context)
	{
		var typeInfo = GetTypeInfo(value, type, context);
		using var lease = ArrayBufferWriterPool<char>.GetPooledObject(out var buffer);
		typeInfo.WriteAsObject(YamlWriter.Create(buffer, typeInfo.Options.FormattingOptions), value, depth: 0);
		return buffer.WrittenSpan.ToString();
	}

	public static void Serialize<T, TWriter>(YamlWriter<TWriter> writer, T value, YamlTypeInfo<T> typeInfo) where TWriter : IBufferWriter<char>
	{
		ArgumentNullException.ThrowIfNull(writer);
		ArgumentNullException.ThrowIfNull(typeInfo);
		typeInfo.Write(writer, value, depth: 0);
	}

	public static void Serialize<TWriter>(YamlWriter<TWriter> writer, object? value, Type type, YamlSerializerContext context) where TWriter : IBufferWriter<char>
	{
		ArgumentNullException.ThrowIfNull(writer);
		GetTypeInfo(value, type, context).WriteAsObject(writer, value, depth: 0);
	}

	public static Node SerializeToNode<T>(T value, YamlTypeInfo<T> typeInfo)
	{
		ArgumentNullException.ThrowIfNull(typeInfo);
		var builder = new NodeBuilder();
		typeInfo.Write(builder, value, depth: 0);
		return builder.Root;
	}

	public static Node SerializeToNode(object? value, Type type, YamlSerializerContext context)
	{
		var typeInfo = GetTypeInfo(value, type, context);
		var builder = new NodeBuilder();
		typeInfo.WriteAsObject(builder, value, depth: 0);
		return builder.Root;
	}

	public static T? Deserialize<T>(string yaml, YamlTypeInfo<T> typeInfo) => Deserialize(Parser.Parse(yaml), typeInfo);

	public static T? Deserialize<T>(Node node, YamlTypeInfo<T> typeInfo)
	{
		ArgumentNullException.ThrowIfNull(node);
		ArgumentNullException.ThrowIfNull(typeInfo);
		return typeInfo.Read(node is DocumentNode document ? document.Root : node);
	}

	public static object? Deserialize(string yaml, Type type, YamlSerializerContext context) => Deserialize(Parser.Parse(yaml), type, context);

	public static object? Deserialize(Node node, Type type, YamlSerializerContext context)
	{
		ArgumentNullException.ThrowIfNull(node);
		ArgumentNullException.ThrowIfNull(type);
		return GetTypeInfo(context, type).ReadAsObject(node is DocumentNode document ? document.Root : node);
	}

	private static YamlTypeInfo GetTypeInfo(object? value, Type type, YamlSerializerContext context)
	{
		ArgumentNullException.ThrowIfNull(type);
		if (value is not null && !type.IsInstanceOfType(value))
		{
			throw new ArgumentException($"The value of type '{value.GetType()}' is not an instance of type '{type}'.", nameof(value));
		}

		return GetTypeInfo(context, type);
	}

	private static YamlTypeInfo GetTypeInfo(YamlSerializerContext context, Type type)
	{
		ArgumentNullException.ThrowIfNull(context);
		return context.GetTypeInfo(type) ?? throw new InvalidOperationException($"The type '{type}' is not known by the serializer context '{context.GetType()}', list it with {nameof(YamlSerializableAttribute)}.");
	}
}
