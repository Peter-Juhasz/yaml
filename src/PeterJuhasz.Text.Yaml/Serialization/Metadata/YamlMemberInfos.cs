using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Serialization.Metadata;

// sets the property of the instance, which is passed by reference, so the properties of structs can be set as well
public delegate void YamlPropertySetter<TDeclaring, TProperty>(ref TDeclaring instance, TProperty value);

// a public property of an object, the name is the name of the property, the explicit name is the value of its [YamlPropertyName]
public abstract class YamlPropertyInfo
{
	private protected YamlPropertyInfo(string name, string? explicitName, bool isRequired, int writeOrder)
	{
		Name = name;
		ExplicitName = explicitName;
		IsRequired = isRequired;
		WriteOrder = writeOrder;
	}

	public string Name { get; }

	public string? ExplicitName { get; }

	public bool IsRequired { get; }

	// members are written in the order of their declaration, starting with the ones of the base types
	public int WriteOrder { get; }

	public abstract bool CanGet { get; }

	public abstract bool CanSet { get; }
}

internal abstract class YamlPropertyInfo<TDeclaring>(string name, string? explicitName, bool isRequired, int writeOrder) : YamlPropertyInfo(name, explicitName, isRequired, writeOrder)
{
	public abstract void Read(ref TDeclaring instance, Node node, string key);

	public abstract void Write(IYamlEmitter emitter, TDeclaring instance, string key, int depth);
}

internal sealed class YamlPropertyInfo<TDeclaring, TProperty>(string name, string? explicitName, bool isRequired, int writeOrder, YamlTypeInfo<TProperty> typeInfo, Func<TDeclaring, TProperty>? getter, YamlPropertySetter<TDeclaring, TProperty>? setter) : YamlPropertyInfo<TDeclaring>(name, explicitName, isRequired, writeOrder)
{
	public override bool CanGet => getter is not null;

	public override bool CanSet => setter is not null;

	// exceptions of the setter and the getter are thrown as is, without the key in the path
	public override void Read(ref TDeclaring instance, Node node, string key) => setter!(ref instance, Members.Read(typeInfo, node, key));

	public override void Write(IYamlEmitter emitter, TDeclaring instance, string key, int depth) => Members.Write(emitter, typeInfo, getter!(instance), key, depth);
}

// a parameter of the constructor of an object, the bound property is the one with the same name, ignoring case, whose key is used for the parameter
public sealed class YamlParameterInfo
{
	internal YamlParameterInfo(string name, string? explicitName, string? boundProperty, YamlTypeInfo typeInfo, bool isOptional, object? defaultValue)
	{
		Name = name;
		ExplicitName = explicitName;
		BoundProperty = boundProperty;
		TypeInfo = typeInfo;
		IsOptional = isOptional;
		DefaultValue = defaultValue;
	}

	public string Name { get; }

	public string? ExplicitName { get; }

	public string? BoundProperty { get; }

	public YamlTypeInfo TypeInfo { get; }

	// a missing optional parameter gets its default value
	public bool IsOptional { get; }

	public object? DefaultValue { get; }
}

// a derived type of a polymorphic type, selected by the main discriminator or one of the aliases
public sealed class YamlDerivedTypeInfo
{
	internal YamlDerivedTypeInfo(YamlTypeInfo typeInfo, string typeDiscriminator, IReadOnlyList<string> aliases)
	{
		TypeInfo = typeInfo;
		TypeDiscriminator = typeDiscriminator;
		Aliases = aliases;
	}

	public Type DerivedType => TypeInfo.Type;

	public YamlTypeInfo TypeInfo { get; }

	public string TypeDiscriminator { get; }

	public IReadOnlyList<string> Aliases { get; }
}
