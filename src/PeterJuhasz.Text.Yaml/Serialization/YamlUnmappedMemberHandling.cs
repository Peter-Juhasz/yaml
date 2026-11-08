namespace PeterJuhasz.Text.Yaml.Serialization;

// determines how keys of a mapping are handled, which do not map to any constructor parameter or settable property of the target type
public enum YamlUnmappedMemberHandling
{
	Skip,
	Disallow,
}
