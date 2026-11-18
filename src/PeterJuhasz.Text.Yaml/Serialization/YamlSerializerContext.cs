using PeterJuhasz.Text.Yaml.Serialization.Metadata;

namespace PeterJuhasz.Text.Yaml.Serialization;

// provides the metadata of the types listed by [YamlSerializable], and the ones they reference, which is generated at compile time,
// so serialization works without reflection, the options are applied at runtime, just like by the reflection based serializer
public abstract class YamlSerializerContext
{
	protected YamlSerializerContext(YamlSerializerOptions? options)
	{
		Options = options ?? YamlSerializerOptions.Default;
	}

	public YamlSerializerOptions Options { get; }

	// returns null, when the type is not known by the context
	public abstract YamlTypeInfo? GetTypeInfo(Type type);
}
