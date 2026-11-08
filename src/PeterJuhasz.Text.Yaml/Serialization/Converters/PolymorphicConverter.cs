using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using PeterJuhasz.Text.Yaml.Model;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

// selects the derived type by the value of the type discriminator key of the mapping, then deserializes the whole mapping as that type,
// and writes the main discriminator of the runtime type as the first key of the mapping
[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal sealed class PolymorphicConverter(Type type, string discriminatorName, StringComparer comparer, FrozenDictionary<string, YamlConverter> derivedTypes, FrozenDictionary<Type, (ScalarNode Discriminator, YamlConverter Converter)> writers, YamlConverter? fallback, bool fallBackOnUnknown) : YamlConverter(type)
{
	private readonly ScalarNode discriminatorKey = ScalarConverter.Text(discriminatorName);

	// returns null, when the type has no derived types listed
	public static PolymorphicConverter? TryCreate(Type type, YamlSerializerOptions options)
	{
		var derivedAttributes = type.GetCustomAttributes(typeof(YamlDerivedTypeAttribute), inherit: false).Cast<YamlDerivedTypeAttribute>().ToArray();
		if (derivedAttributes is [])
		{
			return null;
		}

		var polymorphic = (YamlPolymorphicAttribute?)Attribute.GetCustomAttribute(type, typeof(YamlPolymorphicAttribute), inherit: false) ?? new();
		var discriminatorName = polymorphic.TypeDiscriminatorPropertyName;

		// derived types get their own converters, which skip the discriminator key, the cached ones of the options stay strict
		// discriminators differing only in case conflict, when they are matched case insensitively
		var valueComparer = polymorphic.TypeDiscriminatorCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		var derivedTypes = new Dictionary<string, YamlConverter>(valueComparer);
		var writers = new Dictionary<Type, (ScalarNode Discriminator, YamlConverter Converter)>();
		var seen = new HashSet<Type>();
		foreach (var attribute in derivedAttributes)
		{
			if (!type.IsAssignableFrom(attribute.DerivedType))
			{
				throw new InvalidOperationException($"The type '{attribute.DerivedType}' is not derived from the polymorphic type '{type}'.");
			}

			if (!seen.Add(attribute.DerivedType))
			{
				throw new InvalidOperationException($"The derived type '{attribute.DerivedType}' is listed multiple times for the polymorphic type '{type}'.");
			}

			// the aliases share the converter of the main discriminator
			var converter = CreateObject(attribute.DerivedType, options, discriminatorName);
			writers.Add(attribute.DerivedType, (WriteDiscriminator(attribute.TypeDiscriminator), converter));
			foreach (var discriminator in (ReadOnlySpan<string>)[attribute.TypeDiscriminator, .. attribute.Aliases])
			{
				if (!derivedTypes.TryAdd(discriminator, converter))
				{
					throw new InvalidOperationException($"The type discriminator '{discriminator}' is used multiple times for the polymorphic type '{type}'.");
				}
			}
		}

		// mappings without a discriminator are deserialized as the base type, when it can be created
		var fallback = type is { IsAbstract: false, IsInterface: false } ? CreateObject(type, options, discriminatorName) : null;
		var fallBackOnUnknown = polymorphic.UnknownDerivedTypeHandling is YamlUnknownDerivedTypeHandling.FallBackToBaseType;
		if (fallBackOnUnknown && fallback is null)
		{
			throw new InvalidOperationException($"The polymorphic type '{type}' cannot fall back to itself for unknown derived types, because it is abstract.");
		}

		var comparer = options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		return new(type, discriminatorName, comparer, derivedTypes.ToFrozenDictionary(valueComparer), writers.ToFrozenDictionary(), fallback, fallBackOnUnknown);
	}

	// integer discriminators are written as plain integers, the others as text
	private static ScalarNode WriteDiscriminator(string discriminator) => new ScalarNode(Value: discriminator) switch
	{
		var plain when plain.TryGetInt64(out _) => plain,
		_ => ScalarConverter.Text(discriminator),
	};

	public override Node Write(object value, YamlSerializerOptions options, int depth)
	{
		var runtimeType = value.GetType();
		if (writers.TryGetValue(runtimeType, out var writer))
		{
			// the converters of derived types are objects, which write mappings, or unsupported ones, which throw
			var mapping = (MappingNode)writer.Converter.Write(value, options, depth);
			return new MappingNode(Entries: [new(Key: discriminatorKey, Value: writer.Discriminator), .. mapping.Entries]);
		}

		// instances of the base type are written without a discriminator, and so are unknown derived types, when they fall back to the base type
		if (fallback is not null && (runtimeType == Type || fallBackOnUnknown))
		{
			return fallback.Write(value, options, depth);
		}

		throw new YamlSerializationException($"The type '{runtimeType}' is not a known derived type of the polymorphic type '{Type}'.");
	}

	private static YamlConverter CreateObject(Type type, YamlSerializerOptions options, string discriminatorName) => CreateCore(type, options, discriminatorName) switch
	{
		// unsupported objects, like the ones with multiple constructors, raise their error when they are read
		(ObjectConverter or UnsupportedConverter) and var converter => converter,
		_ => throw new InvalidOperationException($"The derived type '{type}' of a polymorphic type must be an object, which is deserialized from a mapping."),
	};

	public override object? Read(Node node, YamlSerializerOptions options)
	{
		if (node is not MappingNode mapping)
		{
			throw Mismatch(node);
		}

		var entry = mapping.Entries.FirstOrDefault(e => e.Key is ScalarNode key && comparer.Equals(key.Value, discriminatorName));
		switch (entry?.Value)
		{
			case null when fallback is not null:
				return fallback.Read(mapping, options);

			case null:
				throw new YamlSerializationException($"The type discriminator '{discriminatorName}' of the polymorphic type '{Type}' is missing.");

			case ScalarNode { IsNull: false } value when derivedTypes.TryGetValue(value.Value, out var converter):
				return converter.Read(mapping, options);

			case ScalarNode { IsNull: false } when fallBackOnUnknown:
				return fallback!.Read(mapping, options);

			case ScalarNode { IsNull: false } value:
				throw Discriminator($"The type discriminator '{value.Value}' does not match any derived type of the polymorphic type '{Type}'.");

			case var value:
				throw Discriminator($"Cannot convert {Describe(value)} to a type discriminator of the polymorphic type '{Type}'.");
		}
	}

	// the path points to the discriminator itself
	private YamlSerializationException Discriminator(string reason)
	{
		var exception = new YamlSerializationException(reason);
		exception.Prepend(discriminatorName);
		return exception;
	}
}
