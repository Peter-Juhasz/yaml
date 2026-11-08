namespace PeterJuhasz.Text.Yaml.Serialization;

// configures the polymorphic deserialization of a type, whose derived types are listed by [YamlDerivedType]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class YamlPolymorphicAttribute : Attribute
{
	// the key of the mapping, whose value selects the derived type
	public string TypeDiscriminatorPropertyName { get; init; } = "$type";

	public YamlUnknownDerivedTypeHandling UnknownDerivedTypeHandling { get; init; }
}
