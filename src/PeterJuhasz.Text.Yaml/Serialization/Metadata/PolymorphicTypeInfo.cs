using System.Collections.Frozen;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Serialization.Converters;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Serialization.Metadata;

// selects the derived type by the value of the type discriminator key of the mapping, then deserializes the whole mapping as that type,
// and writes the main discriminator of the runtime type as the first key of the mapping
// the derived types are validated on first use, just like by the reflection based serializer
internal sealed class PolymorphicTypeInfo<T> : YamlTypeInfo<T>
{
	private sealed record class State(
		FrozenDictionary<string, YamlTypeInfo> DerivedTypes,
		FrozenDictionary<Type, (ScalarNode Discriminator, YamlTypeInfo TypeInfo)> Writers,
		YamlTypeInfo? Fallback,
		StringComparer Comparer
	);

	private readonly string discriminatorName;
	private readonly bool caseInsensitive;
	private readonly bool fallBackOnUnknown;
	private readonly YamlDerivedTypeInfo[] derivedTypes;
	private readonly YamlTypeInfo<T>? fallback;
	private readonly Lazy<State> state;

	// the fallback is the object of the base type itself, when it is not abstract
	public PolymorphicTypeInfo(YamlSerializerContext context, string discriminatorName, bool caseInsensitive, YamlUnknownDerivedTypeHandling unknownDerivedTypeHandling, YamlDerivedTypeInfo[] derivedTypes, YamlTypeInfo<T>? fallback) : base(context)
	{
		this.discriminatorName = discriminatorName;
		this.caseInsensitive = caseInsensitive;
		fallBackOnUnknown = unknownDerivedTypeHandling is YamlUnknownDerivedTypeHandling.FallBackToBaseType;
		this.derivedTypes = derivedTypes;
		this.fallback = fallback;
		state = new(Build, LazyThreadSafetyMode.PublicationOnly);
	}

	private State Build()
	{
		// derived types get their own metadata, which skips the discriminator key, the ones of the context stay strict
		// discriminators differing only in case conflict, when they are matched case insensitively
		var valueComparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		var readers = new Dictionary<string, YamlTypeInfo>(valueComparer);
		var writers = new Dictionary<Type, (ScalarNode Discriminator, YamlTypeInfo TypeInfo)>();
		var seen = new HashSet<Type>();
		foreach (var derived in derivedTypes)
		{
			if (!Type.IsAssignableFrom(derived.DerivedType))
			{
				throw new InvalidOperationException($"The type '{derived.DerivedType}' is not derived from the polymorphic type '{Type}'.");
			}

			if (!seen.Add(derived.DerivedType))
			{
				throw new InvalidOperationException($"The derived type '{derived.DerivedType}' is listed multiple times for the polymorphic type '{Type}'.");
			}

			// the aliases share the metadata of the main discriminator
			var typeInfo = CreateObject(derived.TypeInfo);
			writers.Add(derived.DerivedType, (SerializationFacts.CreateDiscriminator(derived.TypeDiscriminator), typeInfo));
			foreach (var discriminator in (ReadOnlySpan<string>)[derived.TypeDiscriminator, .. derived.Aliases])
			{
				if (!readers.TryAdd(discriminator, typeInfo))
				{
					throw new InvalidOperationException($"The type discriminator '{discriminator}' is used multiple times for the polymorphic type '{Type}'.");
				}
			}
		}

		// mappings without a discriminator are deserialized as the base type, when it can be created
		var fallbackTypeInfo = fallback is null ? null : CreateObject(fallback);
		if (fallBackOnUnknown && fallbackTypeInfo is null)
		{
			throw new InvalidOperationException($"The polymorphic type '{Type}' cannot fall back to itself for unknown derived types, because it is abstract.");
		}

		var comparer = Options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		return new(DerivedTypes: readers.ToFrozenDictionary(valueComparer), Writers: writers.ToFrozenDictionary(), Fallback: fallbackTypeInfo, Comparer: comparer);
	}

	// unsupported objects, like the ones with multiple constructors, raise their error when they are read
	private YamlTypeInfo CreateObject(YamlTypeInfo typeInfo)
	{
		switch (typeInfo)
		{
			case IObjectTypeInfo objectTypeInfo:
				return objectTypeInfo.CreateDerived(discriminatorName);

			case IUnsupportedTypeInfo:
				return typeInfo;

			default:
				throw new InvalidOperationException($"The derived type '{typeInfo.Type}' of a polymorphic type must be an object, which is deserialized from a mapping.");
		}
	}

	internal override void WriteCore(IYamlEmitter emitter, T value, int depth)
	{
		var state = this.state.Value;
		var runtimeType = value!.GetType();
		if (state.Writers.TryGetValue(runtimeType, out var writer))
		{
			// the derived types are objects, or unsupported ones, which throw
			if (writer.TypeInfo is not IObjectTypeInfo objectTypeInfo)
			{
				writer.TypeInfo.WriteCoreAsObject(emitter, value, depth);
				return;
			}

			emitter.WriteStartMapping();
			emitter.WriteString(discriminatorName);
			emitter.WriteScalar(writer.Discriminator.Value, writer.Discriminator.Style);
			objectTypeInfo.WriteMembers(emitter, value, depth);
			emitter.WriteEndMapping();
			return;
		}

		// instances of the base type are written without a discriminator, and so are unknown derived types, when they fall back to the base type
		if (state.Fallback is not null && (runtimeType == Type || fallBackOnUnknown))
		{
			state.Fallback.WriteCoreAsObject(emitter, value, depth);
			return;
		}

		throw new YamlSerializationException($"The type '{runtimeType}' is not a known derived type of the polymorphic type '{Type}'.");
	}

	internal override T ReadCore(Node node)
	{
		if (node is not MappingNode mapping)
		{
			throw Mismatch(node);
		}

		var state = this.state.Value;
		var entry = mapping.Entries.FirstOrDefault(e => e.Key is ScalarNode key && state.Comparer.Equals(key.Value, discriminatorName));
		switch (entry?.Value)
		{
			case null when state.Fallback is not null:
				return (T)state.Fallback.ReadCoreAsObject(mapping)!;

			case null:
				throw new YamlSerializationException($"The type discriminator '{discriminatorName}' of the polymorphic type '{Type}' is missing.");

			case ScalarNode { IsNull: false } value when state.DerivedTypes.TryGetValue(value.Value, out var typeInfo):
				return (T)typeInfo.ReadCoreAsObject(mapping)!;

			case ScalarNode { IsNull: false } when fallBackOnUnknown:
				return (T)state.Fallback!.ReadCoreAsObject(mapping)!;

			case ScalarNode { IsNull: false } value:
				throw SerializationFacts.InvalidDiscriminator(discriminatorName, $"The type discriminator '{value.Value}' does not match any derived type of the polymorphic type '{Type}'.");

			case var value:
				throw SerializationFacts.InvalidDiscriminator(discriminatorName, $"Cannot convert {SerializationFacts.Describe(value)} to a type discriminator of the polymorphic type '{Type}'.");
		}
	}
}
