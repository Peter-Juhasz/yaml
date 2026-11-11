using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

// creates the instance using the parameterless constructor, or by passing the matching keys to the parameters of the selected constructor,
// then populates the remaining public settable properties, unknown keys are skipped or rejected depending on the options
[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal sealed class ObjectConverter : YamlConverter
{
	private sealed record class PropertyMetadata(int Index, string Name, PropertyInfo Property);

	private sealed record class ParameterMetadata(int Position, string Name, Type Type, bool IsOptional, object? DefaultValue);

	private sealed record class WrittenMember(string Name, PropertyInfo Property);

	private readonly ConstructorInfo? constructor;
	private readonly ImmutableArray<ParameterMetadata> parameters;
	private readonly FrozenDictionary<string, ParameterMetadata> parametersByName;
	private readonly FrozenDictionary<string, PropertyMetadata> properties;
	private readonly ImmutableArray<PropertyMetadata> required;
	private readonly ImmutableArray<WrittenMember> written;
	private readonly string? ignoredKey;
	private readonly StringComparer comparer;

	// the ignored key is the type discriminator, when the type is deserialized as a derived type of a polymorphic one
	public ObjectConverter(Type type, ConstructorInfo? constructor, YamlSerializerOptions options, string? ignoredKey = null) : base(type)
	{
		var comparer = options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		var names = new Dictionary<string, string>(comparer);
		if (ignoredKey is not null)
		{
			names.Add(ignoredKey, "type discriminator");
		}

		// the members are returned from the most derived type first, so hidden properties of base types are skipped
		var publicProperties = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
		foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (property.GetIndexParameters() is [] && !property.IsDefined(typeof(YamlIgnoreAttribute)))
			{
				publicProperties.TryAdd(property.Name, property);
			}
		}

		// parameters bind to the property of the same name, like the ones of the primary constructor of a record, so the key of the property is used for them
		var bound = new HashSet<string>(StringComparer.Ordinal);
		var parameters = ImmutableArray.CreateBuilder<ParameterMetadata>();

		// only the members, which are read by deserialization, are written, and only when they can be read
		var written = new List<WrittenMember>();
		if (constructor is not null)
		{
			var nullability = new NullabilityInfoContext();
			foreach (var parameter in constructor.GetParameters())
			{
				var parameterName = parameter.Name ?? $"arg{parameter.Position}";
				var property = publicProperties.Values.FirstOrDefault(p => String.Equals(p.Name, parameterName, StringComparison.OrdinalIgnoreCase));
				var name = parameter.GetCustomAttribute<YamlPropertyNameAttribute>()?.Name ?? (property is not null ? GetName(property, options) : options.PropertyNamingPolicy?.ConvertName(parameterName) ?? parameterName);
				AddName(names, name, parameterName, type);

				if (property is not null && bound.Add(property.Name) && property is { GetMethod.IsPublic: true })
				{
					written.Add(new(Name: name, Property: property));
				}

				var parameterType = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
				var (isOptional, defaultValue) = GetDefault(parameter, parameterType, nullability);
				parameters.Add(new(Position: parameter.Position, Name: name, Type: parameterType, IsOptional: isOptional, DefaultValue: defaultValue));
			}
		}

		var properties = new Dictionary<string, PropertyMetadata>(comparer);
		var required = ImmutableArray.CreateBuilder<PropertyMetadata>();
		foreach (var property in publicProperties.Values)
		{
			if (property is not { SetMethod.IsPublic: true } || bound.Contains(property.Name))
			{
				continue;
			}

			var name = GetName(property, options);
			AddName(names, name, property.Name, type);
			var metadata = new PropertyMetadata(Index: properties.Count, Name: name, Property: property);
			properties.Add(name, metadata);

			if (property is { GetMethod.IsPublic: true })
			{
				written.Add(new(Name: name, Property: property));
			}

			if (property.IsDefined(typeof(RequiredMemberAttribute)))
			{
				required.Add(metadata);
			}
		}

		this.constructor = constructor;
		this.ignoredKey = ignoredKey;
		this.comparer = comparer;
		this.parameters = parameters.ToImmutable();
		parametersByName = this.parameters.ToFrozenDictionary(static p => p.Name, comparer);
		this.properties = properties.ToFrozenDictionary(comparer);

		// members are written in the order of their declaration, starting with the ones of the base types
		this.written = [.. written.OrderBy(static m => Depth(m.Property.DeclaringType!)).ThenBy(static m => m.Property.MetadataToken)];

		// the constructor may set the required members itself
		var setsRequiredMembers = (constructor ?? type.GetConstructor(Type.EmptyTypes))?.IsDefined(typeof(SetsRequiredMembersAttribute)) ?? false;
		this.required = setsRequiredMembers ? [] : required.ToImmutable();
	}

	// returns no constructor, when the instance is created by the parameterless one and then populated
	public static (ConstructorInfo? Constructor, string? Error) SelectConstructor(Type type)
	{
		var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		switch (constructors.Where(static c => c.IsDefined(typeof(YamlConstructorAttribute))).ToArray())
		{
			case [var marked]:
				return (marked, null);

			case [_, _, ..]:
				return (null, $"Only one constructor can be marked with {nameof(YamlConstructorAttribute)}.");
		}

		var publicConstructors = constructors.Where(static c => c.IsPublic).ToArray();
		if ((type.IsValueType && publicConstructors is []) || publicConstructors.Any(static c => c.GetParameters() is []))
		{
			return (null, null);
		}

		return publicConstructors switch
		{
			[var single] => (single, null),
			[] => (null, "The type must have a public constructor."),
			_ => (null, $"The type has multiple public constructors, mark the one to use with {nameof(YamlConstructorAttribute)}."),
		};
	}

	public override object? Read(Node node, YamlSerializerOptions options)
	{
		if (node is not MappingNode mapping)
		{
			throw Mismatch(node);
		}

		var instance = constructor is null ? Activator.CreateInstance(Type)! : Construct(mapping, constructor, options);

		var seen = required.IsEmpty ? null : new bool[properties.Count];
		var disallowUnmapped = options.UnmappedMemberHandling is YamlUnmappedMemberHandling.Disallow;
		foreach (var entry in mapping.Entries)
		{
			if (entry.Key is ScalarNode key && properties.TryGetValue(key.Value, out var property))
			{
				property.Property.SetValue(instance, ReadMember(entry.Value, property.Property.PropertyType, key.Value, options));
				seen?[property.Index] = true;
				continue;
			}

			// keys of constructor parameters were already consumed, and the type discriminator by the polymorphic converter
			if (disallowUnmapped && !(entry.Key is ScalarNode known && (parametersByName.ContainsKey(known.Value) || (ignoredKey is not null && comparer.Equals(known.Value, ignoredKey)))))
			{
				throw Unmapped(entry.Key);
			}
		}

		foreach (var property in required)
		{
			if (!seen![property.Index])
			{
				throw new YamlSerializationException($"The required property '{property.Name}' is missing.");
			}
		}

		return instance;
	}

	public override void Write(IYamlEmitter emitter, object value, YamlSerializerOptions options, int depth)
	{
		emitter.WriteStartMapping();
		WriteMembers(emitter, value, options, depth);
		emitter.WriteEndMapping();
	}

	// the entries of the mapping, after the type discriminator, when it is written by the polymorphic converter
	public void WriteMembers(IYamlEmitter emitter, object value, YamlSerializerOptions options, int depth)
	{
		foreach (var member in written)
		{
			emitter.WriteString(member.Name);
			WriteMember(emitter, member.Property.GetValue(value), member.Property.PropertyType, member.Name, options, depth);
		}
	}

	private static int Depth(Type type)
	{
		var depth = 0;
		for (var current = type.BaseType; current is not null; current = current.BaseType)
		{
			depth++;
		}
		return depth;
	}

	private object Construct(MappingNode mapping, ConstructorInfo constructor, YamlSerializerOptions options)
	{
		var arguments = new object?[parameters.Length];
		var assigned = new bool[parameters.Length];
		foreach (var entry in mapping.Entries)
		{
			if (entry.Key is not ScalarNode key || !parametersByName.TryGetValue(key.Value, out var parameter))
			{
				continue;
			}

			arguments[parameter.Position] = ReadMember(entry.Value, parameter.Type, key.Value, options);
			assigned[parameter.Position] = true;
		}

		foreach (var parameter in parameters)
		{
			if (assigned[parameter.Position])
			{
				continue;
			}

			arguments[parameter.Position] = parameter.IsOptional ? parameter.DefaultValue : throw new YamlSerializationException($"The required constructor parameter '{parameter.Name}' is missing.");
		}

		// validation errors of the constructor are thrown as is
		return constructor.Invoke(BindingFlags.DoNotWrapExceptions, binder: null, arguments, culture: null);
	}

	private YamlSerializationException Unmapped(Node key)
	{
		if (key is not ScalarNode scalar)
		{
			return new($"A key, which is not a scalar, does not map to any member of type '{Type}'.");
		}

		// the path points to the key itself
		var exception = new YamlSerializationException($"The key '{scalar.Value}' does not map to any member of type '{Type}'.");
		exception.Prepend(scalar.Value);
		return exception;
	}

	private static object? ReadMember(Node node, Type type, string key, YamlSerializerOptions options)
	{
		try
		{
			return ReadValue(node, type, options);
		}
		catch (YamlSerializationException exception) when (exception.Prepend(key))
		{
			throw;
		}
	}

	private static string GetName(PropertyInfo property, YamlSerializerOptions options) => property.GetCustomAttribute<YamlPropertyNameAttribute>()?.Name ?? options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;

	private static void AddName(Dictionary<string, string> names, string name, string member, Type type)
	{
		if (!names.TryAdd(name, member))
		{
			throw new InvalidOperationException($"The members '{names[name]}' and '{member}' of type '{type}' have the same name '{name}'.");
		}
	}

	// a missing parameter is set to its default value, or to null when it is nullable, otherwise it is required
	private static (bool IsOptional, object? DefaultValue) GetDefault(ParameterInfo parameter, Type type, NullabilityInfoContext nullability)
	{
		if (parameter.HasDefaultValue)
		{
			return (true, parameter.DefaultValue switch
			{
				null when type.IsValueType && type.GetNullableUnderlyingType() is null => Activator.CreateInstance(type),
				{ } value when type.IsEnum => Enum.ToObject(type, value),
				var value => value,
			});
		}

		if (type.GetNullableUnderlyingType() is not null)
		{
			return (true, null);
		}

		if (type.IsValueType)
		{
			return (false, null);
		}

		try
		{
			return (nullability.Create(parameter).WriteState is not NullabilityState.NotNull, null);
		}
		catch (InvalidOperationException)
		{
			// the nullability metadata may be removed by trimming
			return (true, null);
		}
	}
}
