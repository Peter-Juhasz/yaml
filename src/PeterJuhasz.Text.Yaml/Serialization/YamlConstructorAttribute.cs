namespace PeterJuhasz.Text.Yaml.Serialization;

// selects the constructor used for deserialization, when the type has more than one, or it is not public
[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false)]
public sealed class YamlConstructorAttribute : Attribute;
