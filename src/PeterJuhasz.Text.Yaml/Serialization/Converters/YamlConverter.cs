using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using PeterJuhasz.Text.Yaml.Model;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal abstract class YamlConverter(Type type)
{
	private static readonly FrozenSet<Type> ListInterfaces = [typeof(IEnumerable<>), typeof(ICollection<>), typeof(IList<>), typeof(IReadOnlyCollection<>), typeof(IReadOnlyList<>)];
	private static readonly FrozenSet<Type> SetInterfaces = [typeof(ISet<>), typeof(IReadOnlySet<>)];
	private static readonly FrozenSet<Type> DictionaryInterfaces = [typeof(IDictionary<,>), typeof(IReadOnlyDictionary<,>)];

	public Type Type { get; } = type;

	// the node is never a null scalar when the type is nullable, those are handled by the caller
	public abstract object? Read(Node node, YamlSerializerOptions options);

	protected YamlSerializationException Mismatch(Node node) => new($"Cannot convert {Describe(node)} to type '{Type}'.");

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

	public static YamlConverter Create(Type type, YamlSerializerOptions options)
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

		if (!type.IsValueType && type.GetConstructor(Type.EmptyTypes) is null)
		{
			return new UnsupportedConverter(type, "The type must have a public parameterless constructor.");
		}

		if (FindInterface(type, typeof(IDictionary<,>)) is not null)
		{
			return new DictionaryConverter(type, type);
		}

		if (FindInterface(type, typeof(ICollection<>)) is not null)
		{
			return new CollectionConverter(type, type);
		}

		return new ObjectConverter(type, options);
	}

	protected static Type? FindInterface(Type type, Type definition)
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

	private static string Describe(Node node) => node switch
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
}
