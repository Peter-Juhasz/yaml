using System.ComponentModel;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Serialization.Converters;

namespace PeterJuhasz.Text.Yaml.Serialization.Metadata;

// creates the metadata of types, it is called by the code generated for serializer contexts, and is not meant to be called directly
[EditorBrowsable(EditorBrowsableState.Never)]
public static class YamlMetadataServices
{
	// the built in scalar types, like strings, numbers and dates
	public static YamlTypeInfo<T> CreateScalarInfo<T>(YamlSerializerContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		return ScalarFormats.Known.TryGetValue(typeof(T), out var format) && format is ScalarFormat<T> typed
			? new ScalarTypeInfo<T>(context, typed)
			: throw new ArgumentException($"The type '{typeof(T)}' is not a built in scalar type.", nameof(T));
	}

	// any other type, which can be parsed from a string
	public static YamlTypeInfo<T> CreateParsableInfo<T>(YamlSerializerContext context) where T : IParsable<T>
	{
		ArgumentNullException.ThrowIfNull(context);
		return new ScalarTypeInfo<T>(context, ScalarFormats.Parsable<T>());
	}

	// the explicit names are the values of [EnumMember]
	public static YamlTypeInfo<TEnum> CreateEnumInfo<TEnum>(YamlSerializerContext context, (string Name, string? ExplicitName, TEnum Value)[] members, bool isFlags) where TEnum : struct, Enum
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(members);
		return new EnumTypeInfo<TEnum>(context, members, isFlags);
	}

	public static YamlTypeInfo<T?> CreateNullableInfo<T>(YamlSerializerContext context, YamlTypeInfo<T> underlying) where T : struct
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(underlying);
		return new NullableTypeInfo<T>(context, underlying);
	}

	// parts of the object model, which are read and written as is
	public static YamlTypeInfo<TNode> CreateNodeInfo<TNode>(YamlSerializerContext context) where TNode : Node
	{
		ArgumentNullException.ThrowIfNull(context);
		return new NodeTypeInfo<TNode>(context);
	}

	// values declared as object, which are read as the model, and written as their runtime type
	public static YamlTypeInfo<object> CreateUntypedInfo(YamlSerializerContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		return new UntypedTypeInfo(context);
	}

	public static YamlTypeInfo<TElement[]> CreateArrayInfo<TElement>(YamlSerializerContext context, YamlTypeInfo<TElement> element)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(element);
		return new ArrayTypeInfo<TElement>(context, element);
	}

	// the builder is the collection itself, when it can be populated, or a list, which is converted to the collection
	public static YamlTypeInfo<TCollection> CreateCollectionInfo<TCollection, TBuilder, TElement>(YamlSerializerContext context, YamlTypeInfo<TElement> element, Func<TBuilder> create, Action<TBuilder, TElement> add, Func<TBuilder, TCollection> convert, bool isStack = false) where TCollection : IEnumerable<TElement>
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(element);
		ArgumentNullException.ThrowIfNull(create);
		ArgumentNullException.ThrowIfNull(add);
		ArgumentNullException.ThrowIfNull(convert);
		return new CollectionTypeInfo<TCollection, TBuilder, TElement>(context, element, create, add, convert, isStack);
	}

	public static YamlTypeInfo<TDictionary> CreateDictionaryInfo<TDictionary, TBuilder, TKey, TValue>(YamlSerializerContext context, YamlTypeInfo<TKey> key, YamlTypeInfo<TValue> value, Func<TBuilder> create, Action<TBuilder, TKey, TValue> set, Func<TBuilder, TDictionary> convert) where TDictionary : IEnumerable<KeyValuePair<TKey, TValue>>
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(value);
		ArgumentNullException.ThrowIfNull(create);
		ArgumentNullException.ThrowIfNull(set);
		ArgumentNullException.ThrowIfNull(convert);
		return new DictionaryTypeInfo<TDictionary, TBuilder, TKey, TValue>(context, key, value, create, set, convert);
	}

	// the object is created by the parameterless constructor, or by the one with parameters, the members are resolved on first use, so the types can reference each other
	// the properties are listed from the most derived type first, without the hidden properties of base types
	public static YamlTypeInfo<T> CreateObjectInfo<T>(YamlSerializerContext context, Func<T>? createObject, Func<object?[], T>? createObjectWithArguments, Func<YamlPropertyInfo[]> properties, Func<YamlParameterInfo[]>? parameters, bool setsRequiredMembers)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(properties);
		return new ObjectTypeInfo<T>(context, createObject, createObjectWithArguments, properties, parameters, setsRequiredMembers, ignoredKey: null);
	}

	// the getter and the setter are null, when they are not public
	public static YamlPropertyInfo CreatePropertyInfo<TDeclaring, TProperty>(string name, YamlTypeInfo<TProperty> typeInfo, Func<TDeclaring, TProperty>? getter, YamlPropertySetter<TDeclaring, TProperty>? setter, string? explicitName, bool isRequired, int writeOrder)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(typeInfo);
		return new YamlPropertyInfo<TDeclaring, TProperty>(name, explicitName, isRequired, writeOrder, typeInfo, getter, setter);
	}

	public static YamlParameterInfo CreateParameterInfo<TParameter>(string name, YamlTypeInfo<TParameter> typeInfo, string? explicitName, string? boundProperty, bool isOptional, TParameter defaultValue)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(typeInfo);
		return new(name, explicitName, boundProperty, typeInfo, isOptional, defaultValue);
	}

	// the metadata of the derived type is its object, not the polymorphic one, when it has derived types itself
	public static YamlDerivedTypeInfo CreateDerivedTypeInfo<TDerived>(YamlTypeInfo<TDerived> typeInfo, string typeDiscriminator, params string[] aliases)
	{
		ArgumentNullException.ThrowIfNull(typeInfo);
		ArgumentNullException.ThrowIfNull(typeDiscriminator);
		ArgumentNullException.ThrowIfNull(aliases);
		return new(typeInfo, typeDiscriminator, aliases);
	}

	// the fallback is the object of the base type, when it is not abstract
	public static YamlTypeInfo<T> CreatePolymorphicInfo<T>(YamlSerializerContext context, string typeDiscriminatorPropertyName, bool typeDiscriminatorCaseInsensitive, YamlUnknownDerivedTypeHandling unknownDerivedTypeHandling, YamlDerivedTypeInfo[] derivedTypes, YamlTypeInfo<T>? fallback)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(typeDiscriminatorPropertyName);
		ArgumentNullException.ThrowIfNull(derivedTypes);
		return new PolymorphicTypeInfo<T>(context, typeDiscriminatorPropertyName, typeDiscriminatorCaseInsensitive, unknownDerivedTypeHandling, derivedTypes, fallback);
	}

	// types, which cannot be serialized, raise the error when they are used
	public static YamlTypeInfo<T> CreateUnsupportedInfo<T>(YamlSerializerContext context, string reason)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(reason);
		return new UnsupportedTypeInfo<T>(context, reason);
	}
}
