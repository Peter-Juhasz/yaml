namespace PeterJuhasz.Text.Yaml.Serialization;

// determines how a type discriminator is handled, which does not match any derived type
public enum YamlUnknownDerivedTypeHandling
{
	Fail,

	// the base type is deserialized instead, it must not be abstract
	FallBackToBaseType,
}
