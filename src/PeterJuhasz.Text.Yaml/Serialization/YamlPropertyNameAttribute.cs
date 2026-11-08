namespace PeterJuhasz.Text.Yaml.Serialization;

// overrides the mapping key of a property, the naming policy is not applied to it
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class YamlPropertyNameAttribute(string name) : Attribute
{
	public string Name { get; } = name;
}
