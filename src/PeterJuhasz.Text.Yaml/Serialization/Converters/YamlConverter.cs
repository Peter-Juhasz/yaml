using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal abstract class YamlConverter(Type type)
{
	private static readonly FrozenSet<Type> ListInterfaces = [typeof(IEnumerable<>), typeof(ICollection<>), typeof(IList<>), typeof(IReadOnlyCollection<>), typeof(IReadOnlyList<>)];
	private static readonly FrozenSet<Type> SetInterfaces = [typeof(ISet<>), typeof(IReadOnlySet<>)];
	private static readonly FrozenSet<Type> DictionaryInterfaces = [typeof(IDictionary<,>), typeof(IReadOnlyDictionary<,>)];

	// nesting deeper than this is most likely a cycle in the object graph, which would overflow the stack
	private const int MaxDepth = 64;

	public Type Type { get; } = type;

	// the node is never a null scalar when the type is nullable, those are handled by the caller
	public abstract object? Read(Node node, YamlSerializerOptions options);

	// the value is never null, and it is an instance of the type, those are handled by the caller
	public abstract void Write(IYamlEmitter emitter, object value, YamlSerializerOptions options, int depth);

	protected YamlSerializationException Mismatch(Node node) => new($"Cannot convert {Describe(node)} to type '{Type}'.");

	public static void WriteValue(IYamlEmitter emitter, object? value, Type type, YamlSerializerOptions options, int depth)
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

		if (depth > MaxDepth)
		{
			throw new YamlSerializationException($"The maximum depth of {MaxDepth} was exceeded, the object graph may contain a cycle.");
		}

		// values of object members are written as their runtime type
		var converterType = type == typeof(object) ? value.GetType() : Nullable.GetUnderlyingType(type) ?? type;
		options.GetConverter(converterType).Write(emitter, value, options, depth);
	}

	protected static void WriteMember(IYamlEmitter emitter, object? value, Type type, string key, YamlSerializerOptions options, int depth)
	{
		try
		{
			WriteValue(emitter, value, type, options, depth + 1);
		}
		catch (YamlSerializationException exception) when (exception.Prepend(key))
		{
			throw;
		}
	}

	protected static void WriteSequence(IYamlEmitter emitter, IEnumerable items, Type elementType, YamlSerializerOptions options, int depth)
	{
		emitter.WriteStartSequence();
		var index = 0;
		foreach (var item in items)
		{
			try
			{
				WriteValue(emitter, item, elementType, options, depth + 1);
			}
			catch (YamlSerializationException exception) when (exception.Prepend(index))
			{
				throw;
			}
			index++;
		}
		emitter.WriteEndSequence();
	}

	public static object? ReadValue(Node node, Type type, YamlSerializerOptions options)
	{
		// the model is returned as is, so parts of a document can be processed manually
		if (type == typeof(object))
		{
			return node is ScalarNode { IsNull: true } ? null : node;
		}

		if (typeof(Node).IsAssignableFrom(type))
		{
			return type.IsInstanceOfType(node) ? node : throw new YamlSerializationException($"Cannot convert {Describe(node)} to type '{type}'.");
		}

		var underlying = Nullable.GetUnderlyingType(type);
		if (node is ScalarNode { IsNull: true } && (underlying is not null || !type.IsValueType))
		{
			return null;
		}

		return options.GetConverter(underlying ?? type).Read(node, options);
	}

	public static YamlConverter Create(Type type, YamlSerializerOptions options) => PolymorphicConverter.TryCreate(type, options) ?? CreateCore(type, options, ignoredKey: null);

	// creates the converter without considering polymorphism, the ignored key is skipped by objects
	public static YamlConverter CreateCore(Type type, YamlSerializerOptions options, string? ignoredKey)
	{
		if (ScalarConverter.TryCreate(type) is { } scalar)
		{
			return scalar;
		}

		if (type.IsEnum)
		{
			return new EnumConverter(type, options.EnumNamingPolicy);
		}

		if (type.IsArray)
		{
			return type.GetArrayRank() is 1 ? new ArrayConverter(type) : new UnsupportedConverter(type, "Multi-dimensional arrays are not supported.");
		}

		if (CollectionFactory.TryCreateImmutable(type) is { } immutable)
		{
			return immutable;
		}

		// abstract collection types are created as their default implementations
		if (type is { IsInterface: true, IsGenericType: true })
		{
			var definition = type.GetGenericTypeDefinition();
			var arguments = type.GetGenericArguments();
			if (DictionaryInterfaces.Contains(definition))
			{
				return new DictionaryConverter(type, typeof(Dictionary<,>).MakeGenericType(arguments));
			}

			if (ListInterfaces.Contains(definition))
			{
				return new CollectionConverter(type, typeof(List<>).MakeGenericType(arguments));
			}

			if (SetInterfaces.Contains(definition))
			{
				return new CollectionConverter(type, typeof(HashSet<>).MakeGenericType(arguments));
			}
		}

		if (type.IsInterface || type.IsAbstract)
		{
			return new UnsupportedConverter(type, "Interfaces and abstract types are not supported.");
		}

		// collections are populated after they are created
		var hasParameterlessConstructor = type.IsValueType || type.GetConstructor(Type.EmptyTypes) is not null;
		if (hasParameterlessConstructor && FindInterface(type, typeof(IDictionary<,>)) is not null)
		{
			return new DictionaryConverter(type, type);
		}

		if (hasParameterlessConstructor && FindInterface(type, typeof(ICollection<>)) is not null)
		{
			return new CollectionConverter(type, type);
		}

		if (CollectionFactory.TryCreateFromConstructor(type) is { } constructed)
		{
			return constructed;
		}

		return ObjectConverter.SelectConstructor(type) switch
		{
			(_, { } error) => new UnsupportedConverter(type, error),
			(var constructor, _) => new ObjectConverter(type, constructor, options, ignoredKey),
		};
	}

	public static Type? FindInterface(Type type, Type definition)
	{
		if (type.IsGenericType && type.GetGenericTypeDefinition() == definition)
		{
			return type;
		}

		foreach (var candidate in type.GetInterfaces())
		{
			if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == definition)
			{
				return candidate;
			}
		}

		return null;
	}

	protected static string Describe(Node node) => node switch
	{
		ScalarNode scalar => $"the scalar '{scalar.Value}'",
		SequenceNode => "a sequence",
		MappingNode => "a mapping",
		_ => $"a {node.GetType().Name}",
	};
}

[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal sealed class UnsupportedConverter(Type type, string reason) : YamlConverter(type)
{
	public override object? Read(Node node, YamlSerializerOptions options) => throw new YamlSerializationException($"Cannot deserialize type '{Type}'. {reason}");

	public override void Write(IYamlEmitter emitter, object value, YamlSerializerOptions options, int depth) => throw new YamlSerializationException($"Cannot serialize type '{Type}'. {reason}");
}
