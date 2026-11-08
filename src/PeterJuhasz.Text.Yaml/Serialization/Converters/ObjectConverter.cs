using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using PeterJuhasz.Text.Yaml.Model;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

// populates the public settable properties of a new instance, unknown keys are skipped
[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal sealed class ObjectConverter : YamlConverter
{
	private sealed record class PropertyMetadata(int Index, string Name, PropertyInfo Property);

	private readonly FrozenDictionary<string, PropertyMetadata> properties;
	private readonly ImmutableArray<PropertyMetadata> required;

	public ObjectConverter(Type type, YamlSerializerOptions options) : base(type)
	{
		var comparer = options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		var properties = new Dictionary<string, PropertyMetadata>(comparer);
		var declared = new HashSet<string>(StringComparer.Ordinal);
		var required = ImmutableArray.CreateBuilder<PropertyMetadata>();

		// the members are returned from the most derived type first, so hidden properties of base types are skipped
		foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (property is not { SetMethod.IsPublic: true } || property.GetIndexParameters() is not [] || property.IsDefined(typeof(YamlIgnoreAttribute)) || !declared.Add(property.Name))
			{
				continue;
			}

			var name = property.GetCustomAttribute<YamlPropertyNameAttribute>()?.Name ?? options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
			var metadata = new PropertyMetadata(Index: properties.Count, Name: name, Property: property);
			if (!properties.TryAdd(name, metadata))
			{
				throw new InvalidOperationException($"The properties '{properties[name].Property.Name}' and '{property.Name}' of type '{type}' have the same name '{name}'.");
			}

			if (property.IsDefined(typeof(RequiredMemberAttribute)))
			{
				required.Add(metadata);
			}
		}

		this.properties = properties.ToFrozenDictionary(comparer);

		// the constructor may set the required members itself
		var setsRequiredMembers = type.GetConstructor(Type.EmptyTypes)?.IsDefined(typeof(SetsRequiredMembersAttribute)) ?? false;
		this.required = setsRequiredMembers ? [] : required.ToImmutable();
	}

	public override object? Read(Node node, YamlSerializerOptions options)
	{
		if (node is not MappingNode mapping)
		{
			throw Mismatch(node);
		}

		var instance = Activator.CreateInstance(Type)!;
		var seen = required.IsEmpty ? null : new bool[properties.Count];
		foreach (var entry in mapping.Entries)
		{
			if (entry.Key is not ScalarNode key || !properties.TryGetValue(key.Value, out var property))
			{
				continue;
			}

			try
			{
				property.Property.SetValue(instance, ReadValue(entry.Value, property.Property.PropertyType, options));
			}
			catch (YamlSerializationException exception) when (exception.Prepend(key.Value))
			{
				throw;
			}

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
}
