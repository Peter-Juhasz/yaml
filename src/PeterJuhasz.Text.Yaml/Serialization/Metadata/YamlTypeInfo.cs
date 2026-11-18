using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Serialization.Converters;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Serialization.Metadata;

// the metadata of a type, which reads and writes its values without reflection, it is created by the generated serializer contexts
public abstract class YamlTypeInfo
{
	private protected YamlTypeInfo(Type type, YamlSerializerContext context)
	{
		Type = type;
		Context = context;
	}

	public Type Type { get; }

	public YamlSerializerOptions Options => Context.Options;

	internal YamlSerializerContext Context { get; }

	// null scalars are read as null for the types, which can be null
	internal abstract object? ReadAsObject(Node node);

	// the value is not null, and it is not a scalar null, those are handled by the caller
	internal abstract object? ReadCoreAsObject(Node node);

	// null values are written as null, nodes as they are, and the depth is checked
	internal abstract void WriteAsObject(IYamlEmitter emitter, object? value, int depth);

	internal abstract void WriteCoreAsObject(IYamlEmitter emitter, object value, int depth);
}

public abstract class YamlTypeInfo<T> : YamlTypeInfo
{
	// reference types and nullable value types
	private static readonly bool CanBeNull = default(T) is null;

	private protected YamlTypeInfo(YamlSerializerContext context) : base(typeof(T), context)
	{
	}

	internal virtual T Read(Node node) => CanBeNull && node is ScalarNode { IsNull: true } ? default! : ReadCore(node);

	// the node is never a null scalar when the type can be null
	internal abstract T ReadCore(Node node);

	// writes the value as a node at the current position of the emitter
	internal void Write(IYamlEmitter emitter, T value, int depth)
	{
		if (value is null)
		{
			emitter.WriteScalar("null", ScalarStyle.Plain);
			return;
		}

		// parts of the model are written as is, just like they are returned by deserialization
		if (value is Node node)
		{
			emitter.WriteNode(node);
			return;
		}

		if (depth > SerializationFacts.MaxDepth)
		{
			throw SerializationFacts.DepthExceeded();
		}

		WriteCore(emitter, value, depth);
	}

	// the value is never null, and it is not a node, those are handled by the caller
	internal abstract void WriteCore(IYamlEmitter emitter, T value, int depth);

	internal sealed override object? ReadAsObject(Node node) => Read(node);

	internal sealed override object? ReadCoreAsObject(Node node) => ReadCore(node);

	internal sealed override void WriteAsObject(IYamlEmitter emitter, object? value, int depth)
	{
		if (value is null)
		{
			emitter.WriteScalar("null", ScalarStyle.Plain);
			return;
		}

		Write(emitter, (T)value, depth);
	}

	internal sealed override void WriteCoreAsObject(IYamlEmitter emitter, object value, int depth) => WriteCore(emitter, (T)value, depth);

	private protected YamlSerializationException Mismatch(Node node) => SerializationFacts.Mismatch(node, Type);

	private protected static void WriteSequence<TElement>(IYamlEmitter emitter, IEnumerable<TElement> items, YamlTypeInfo<TElement> element, int depth)
	{
		emitter.WriteStartSequence();
		var index = 0;
		foreach (var item in items)
		{
			try
			{
				element.Write(emitter, item, depth + 1);
			}
			catch (YamlSerializationException exception) when (exception.Prepend(index))
			{
				throw;
			}
			index++;
		}
		emitter.WriteEndSequence();
	}
}

// reads and writes the values of entries, and prepends their keys to the paths of the errors
internal static class Members
{
	public static TValue Read<TValue>(YamlTypeInfo<TValue> typeInfo, Node node, string key)
	{
		try
		{
			return typeInfo.Read(node);
		}
		catch (YamlSerializationException exception) when (exception.Prepend(key))
		{
			throw;
		}
	}

	public static object? ReadAsObject(YamlTypeInfo typeInfo, Node node, string key)
	{
		try
		{
			return typeInfo.ReadAsObject(node);
		}
		catch (YamlSerializationException exception) when (exception.Prepend(key))
		{
			throw;
		}
	}

	// the value is nested in the current node, so it is one level deeper
	public static void Write<TValue>(IYamlEmitter emitter, YamlTypeInfo<TValue> typeInfo, TValue value, string key, int depth)
	{
		try
		{
			typeInfo.Write(emitter, value, depth + 1);
		}
		catch (YamlSerializationException exception) when (exception.Prepend(key))
		{
			throw;
		}
	}
}
