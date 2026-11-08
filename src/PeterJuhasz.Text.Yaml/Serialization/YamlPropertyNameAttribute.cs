namespace PeterJuhasz.Text.Yaml.Serialization;

// overrides the mapping key of a property or a constructor parameter, the naming policy is not applied to it
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class YamlPropertyNameAttribute(string name) : Attribute
{
	public string Name { get; } = name;
}
