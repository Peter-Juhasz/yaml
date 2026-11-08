using System.Globalization;

namespace PeterJuhasz.Text.Yaml.Serialization;

// a derived type, which is deserialized when the type discriminator of the mapping matches
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class YamlDerivedTypeAttribute(Type derivedType, string typeDiscriminator) : Attribute
{
	// integer discriminators are matched by their invariant text, like '1'
	public YamlDerivedTypeAttribute(Type derivedType, int typeDiscriminator) : this(derivedType, typeDiscriminator.ToString(CultureInfo.InvariantCulture))
	{
	}

	public Type DerivedType { get; } = derivedType;

	public string TypeDiscriminator { get; } = typeDiscriminator;
}
