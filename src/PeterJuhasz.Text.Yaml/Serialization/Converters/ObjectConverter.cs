using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using PeterJuhasz.Text.Yaml.Model;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

// creates the instance using the parameterless constructor, or by passing the matching keys to the parameters of the selected constructor,
// then populates the remaining public settable properties, unknown keys are skipped
[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal sealed class ObjectConverter : YamlConverter
{
	private sealed record class PropertyMetadata(int Index, string Name, PropertyInfo Property);

	private sealed record class ParameterMetadata(int Position, string Name, Type Type, bool IsOptional, object? DefaultValue);

	private readonly ConstructorInfo? constructor;
	private readonly ImmutableArray<ParameterMetadata> parameters;
	private readonly FrozenDictionary<string, ParameterMetadata> parametersByName;
	private readonly FrozenDictionary<string, PropertyMetadata> properties;
	private readonly ImmutableArray<PropertyMetadata> required;

	public ObjectConverter(Type type, ConstructorInfo? constructor, YamlSerializerOptions options) : base(type)
	{
		var comparer = options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		var names = new Dictionary<string, string>(comparer);

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
		if (constructor is not null)
		{
			var nullability = new NullabilityInfoContext();
			foreach (var parameter in constructor.GetParameters())
			{
				var parameterName = parameter.Name ?? $"arg{parameter.Position}";
				var property = publicProperties.Values.FirstOrDefault(p => String.Equals(p.Name, parameterName, StringComparison.OrdinalIgnoreCase));
				if (property is not null)
				{
					bound.Add(property.Name);
				}

				var name = parameter.GetCustomAttribute<YamlPropertyNameAttribute>()?.Name ?? (property is not null ? GetName(property, options) : options.PropertyNamingPolicy?.ConvertName(parameterName) ?? parameterName);
				AddName(names, name, parameterName, type);

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

			if (property.IsDefined(typeof(RequiredMemberAttribute)))
			{
				required.Add(metadata);
			}
		}

		this.constructor = constructor;
		this.parameters = parameters.ToImmutable();
		parametersByName = this.parameters.ToFrozenDictionary(static p => p.Name, comparer);
		this.properties = properties.ToFrozenDictionary(comparer);

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
		foreach (var entry in mapping.Entries)
		{
			if (entry.Key is not ScalarNode key || !properties.TryGetValue(key.Value, out var property))
			{
				continue;
			}

			property.Property.SetValue(instance, ReadMember(entry.Value, property.Property.PropertyType, key.Value, options));
			seen?[property.Index] = true;
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
				null when type.IsValueType && Nullable.GetUnderlyingType(type) is null => Activator.CreateInstance(type),
				{ } value when type.IsEnum => Enum.ToObject(type, value),
				var value => value,
			});
		}

		if (Nullable.GetUnderlyingType(type) is not null)
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
