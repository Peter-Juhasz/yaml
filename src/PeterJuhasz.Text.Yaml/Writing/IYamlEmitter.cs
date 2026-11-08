using PeterJuhasz.Text.Yaml.Model;

namespace PeterJuhasz.Text.Yaml.Writing;

// the target of serialization, which is either the writer producing text, or the builder producing the object model
internal interface IYamlEmitter
{
	void WriteStartMapping();

	void WriteEndMapping();

	void WriteStartSequence();

	void WriteEndSequence();

	// scalars written in place of a key are the keys of mappings
	void WriteScalar(string value, ScalarStyle style);

	// the style is selected, so the text is read back as the same string
	void WriteString(string value);

	void WriteNode(Node node);
}
