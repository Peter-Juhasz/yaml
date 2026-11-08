namespace PeterJuhasz.Text.Yaml.Serialization;

// configures the polymorphic deserialization of a type, whose derived types are listed by [YamlDerivedType]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class YamlPolymorphicAttribute : Attribute
{
	// the key of the mapping, whose value selects the derived type
	public string TypeDiscriminatorPropertyName { get; init; } = "$type";

	// discriminator values are matched case sensitively by default
	public bool TypeDiscriminatorCaseInsensitive { get; init; }

	public YamlUnknownDerivedTypeHandling UnknownDerivedTypeHandling { get; init; }
}
