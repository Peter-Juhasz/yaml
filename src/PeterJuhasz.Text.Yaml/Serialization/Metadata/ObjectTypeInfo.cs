using System.Collections.Frozen;
using System.Collections.Immutable;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Serialization.Converters;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Serialization.Metadata;

// the view of an object, which a polymorphic type uses to read and write its derived types
internal interface IObjectTypeInfo
{
	// a copy, which skips the type discriminator key, its members are resolved immediately, so their errors, like conflicting names, are raised
	YamlTypeInfo CreateDerived(string discriminatorName);

	// the entries of the mapping, after the type discriminator
	void WriteMembers(IYamlEmitter emitter, object value, int depth);
}

// creates the instance using the parameterless constructor, or by passing the matching keys to the parameters of the selected constructor,
// then populates the remaining public settable properties, unknown keys are skipped or rejected depending on the options
// the members are resolved on first use, so types can reference each other, and the keys are computed by the naming policy of the options
internal sealed class ObjectTypeInfo<T> : YamlTypeInfo<T>, IObjectTypeInfo
{
	private sealed record class PropertyEntry(int Index, string Name, YamlPropertyInfo<T> Property);

	private sealed record class ParameterEntry(int Position, string Name, YamlParameterInfo Parameter);

	private sealed record class State(
		ImmutableArray<ParameterEntry> Parameters,
		FrozenDictionary<string, ParameterEntry> ParametersByName,
		FrozenDictionary<string, PropertyEntry> Properties,
		ImmutableArray<PropertyEntry> Required,
		ImmutableArray<(string Name, YamlPropertyInfo<T> Property)> Written,
		StringComparer Comparer
	);

	private readonly Func<T>? create;
	private readonly Func<object?[], T>? createWithArguments;
	private readonly Func<YamlPropertyInfo[]> properties;
	private readonly Func<YamlParameterInfo[]>? parameters;
	private readonly bool setsRequiredMembers;
	private readonly string? ignoredKey;
	private readonly Lazy<State> state;

	// the ignored key is the type discriminator, when the type is deserialized as a derived type of a polymorphic one
	public ObjectTypeInfo(YamlSerializerContext context, Func<T>? create, Func<object?[], T>? createWithArguments, Func<YamlPropertyInfo[]> properties, Func<YamlParameterInfo[]>? parameters, bool setsRequiredMembers, string? ignoredKey) : base(context)
	{
		if ((create is null) == (createWithArguments is null))
		{
			throw new ArgumentException("Exactly one of the factories must be provided.", nameof(create));
		}

		this.create = create;
		this.createWithArguments = createWithArguments;
		this.properties = properties;
		this.parameters = parameters;
		this.setsRequiredMembers = setsRequiredMembers;
		this.ignoredKey = ignoredKey;

		// errors are not cached, so they are raised on each use, like by the reflection based serializer
		state = new(Build, LazyThreadSafetyMode.PublicationOnly);
	}

	public YamlTypeInfo CreateDerived(string discriminatorName)
	{
		var derived = new ObjectTypeInfo<T>(Context, create, createWithArguments, properties, parameters, setsRequiredMembers, discriminatorName);
		_ = derived.state.Value;
		return derived;
	}

	private State Build()
	{
		var options = Options;
		var comparer = options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		var names = new Dictionary<string, string>(comparer);
		if (ignoredKey is not null)
		{
			names.Add(ignoredKey, "type discriminator");
		}

		// the properties are listed from the most derived type first, and the hidden properties of base types are already skipped
		var publicProperties = new Dictionary<string, YamlPropertyInfo<T>>(StringComparer.Ordinal);
		foreach (var property in properties())
		{
			var typed = property as YamlPropertyInfo<T> ?? throw new InvalidOperationException($"The property '{property.Name}' is not a property of type '{Type}'.");
			publicProperties.TryAdd(typed.Name, typed);
		}

		// parameters bind to the property of the same name, like the ones of the primary constructor of a record, so the key of the property is used for them
		var bound = new HashSet<string>(StringComparer.Ordinal);
		var parameterEntries = ImmutableArray.CreateBuilder<ParameterEntry>();

		// only the members, which are read by deserialization, are written, and only when they can be read
		var written = new List<(string Name, YamlPropertyInfo<T> Property)>();
		if (createWithArguments is not null)
		{
			foreach (var parameter in parameters?.Invoke() ?? [])
			{
				var property = parameter.BoundProperty is { } boundName ? publicProperties.GetValueOrDefault(boundName) : null;
				var name = parameter.ExplicitName ?? (property is not null ? GetName(property, options) : options.PropertyNamingPolicy?.ConvertName(parameter.Name) ?? parameter.Name);
				SerializationFacts.AddName(names, name, parameter.Name, Type);

				if (property is not null && bound.Add(property.Name) && property.CanGet)
				{
					written.Add((name, property));
				}

				parameterEntries.Add(new(Position: parameterEntries.Count, Name: name, Parameter: parameter));
			}
		}

		var propertyEntries = new Dictionary<string, PropertyEntry>(comparer);
		var required = ImmutableArray.CreateBuilder<PropertyEntry>();
		foreach (var property in publicProperties.Values)
		{
			if (!property.CanSet || bound.Contains(property.Name))
			{
				continue;
			}

			var name = GetName(property, options);
			SerializationFacts.AddName(names, name, property.Name, Type);
			var entry = new PropertyEntry(Index: propertyEntries.Count, Name: name, Property: property);
			propertyEntries.Add(name, entry);

			if (property.CanGet)
			{
				written.Add((name, property));
			}

			if (property.IsRequired)
			{
				required.Add(entry);
			}
		}

		var parameterArray = parameterEntries.ToImmutable();
		return new(
			Parameters: parameterArray,
			ParametersByName: parameterArray.ToFrozenDictionary(static p => p.Name, comparer),
			Properties: propertyEntries.ToFrozenDictionary(comparer),

			// the constructor may set the required members itself
			Required: setsRequiredMembers ? [] : required.ToImmutable(),

			// members are written in the order of their declaration, starting with the ones of the base types
			Written: [.. written.OrderBy(static m => m.Property.WriteOrder)],
			Comparer: comparer
		);
	}

	internal override T ReadCore(Node node)
	{
		if (node is not MappingNode mapping)
		{
			throw Mismatch(node);
		}

		var state = this.state.Value;
		var instance = createWithArguments is null ? create!() : Construct(mapping, state, createWithArguments);

		var seen = state.Required.IsEmpty ? null : new bool[state.Properties.Count];
		var disallowUnmapped = Options.UnmappedMemberHandling is YamlUnmappedMemberHandling.Disallow;
		foreach (var entry in mapping.Entries)
		{
			if (entry.Key is ScalarNode key && state.Properties.TryGetValue(key.Value, out var property))
			{
				property.Property.Read(ref instance, entry.Value, key.Value);
				seen?[property.Index] = true;
				continue;
			}

			// keys of constructor parameters were already consumed, and the type discriminator by the polymorphic type
			if (disallowUnmapped && !(entry.Key is ScalarNode known && (state.ParametersByName.ContainsKey(known.Value) || (ignoredKey is not null && state.Comparer.Equals(known.Value, ignoredKey)))))
			{
				throw SerializationFacts.Unmapped(entry.Key, Type);
			}
		}

		foreach (var property in state.Required)
		{
			if (!seen![property.Index])
			{
				throw new YamlSerializationException($"The required property '{property.Name}' is missing.");
			}
		}

		return instance;
	}

	internal override void WriteCore(IYamlEmitter emitter, T value, int depth)
	{
		emitter.WriteStartMapping();
		WriteMembers(emitter, value, depth);
		emitter.WriteEndMapping();
	}

	void IObjectTypeInfo.WriteMembers(IYamlEmitter emitter, object value, int depth) => WriteMembers(emitter, (T)value, depth);

	private void WriteMembers(IYamlEmitter emitter, T value, int depth)
	{
		foreach (var (name, property) in state.Value.Written)
		{
			emitter.WriteString(name);
			property.Write(emitter, value, name, depth);
		}
	}

	private static T Construct(MappingNode mapping, State state, Func<object?[], T> createWithArguments)
	{
		var arguments = new object?[state.Parameters.Length];
		var assigned = new bool[state.Parameters.Length];
		foreach (var entry in mapping.Entries)
		{
			if (entry.Key is not ScalarNode key || !state.ParametersByName.TryGetValue(key.Value, out var parameter))
			{
				continue;
			}

			arguments[parameter.Position] = Members.ReadAsObject(parameter.Parameter.TypeInfo, entry.Value, key.Value);
			assigned[parameter.Position] = true;
		}

		foreach (var parameter in state.Parameters)
		{
			if (assigned[parameter.Position])
			{
				continue;
			}

			arguments[parameter.Position] = parameter.Parameter.IsOptional ? parameter.Parameter.DefaultValue : throw new YamlSerializationException($"The required constructor parameter '{parameter.Name}' is missing.");
		}

		// validation errors of the constructor are thrown as is
		return createWithArguments(arguments);
	}

	private static string GetName(YamlPropertyInfo property, YamlSerializerOptions options) => property.ExplicitName ?? options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
}
