namespace PeterJuhasz.Text.Yaml.Serialization;

// lists a type, whose metadata is generated into the partial serializer context, the types of its members are included as well
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class YamlSerializableAttribute(Type type) : Attribute
{
	public Type Type { get; } = type;

	// the name of the property of the context, which returns the metadata of the type, derived from the name of the type by default
	public string? TypeInfoPropertyName { get; init; }
}
