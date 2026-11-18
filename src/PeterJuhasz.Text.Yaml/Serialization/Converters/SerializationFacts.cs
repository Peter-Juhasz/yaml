using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

// the rules and errors shared by the reflection based converters and the generated metadata, so both behave the same
internal static class SerializationFacts
{
	// nesting deeper than this is most likely a cycle in the object graph, which would overflow the stack
	public const int MaxDepth = 64;

	public static string Describe(Node node) => node switch
	{
		ScalarNode scalar => $"the scalar '{scalar.Value}'",
		SequenceNode => "a sequence",
		MappingNode => "a mapping",
		DocumentNode => "a document",
	};

	public static YamlSerializationException Mismatch(Node node, Type type) => new($"Cannot convert {Describe(node)} to type '{type}'.");

	public static YamlSerializationException DepthExceeded() => new($"The maximum depth of {MaxDepth} was exceeded, the object graph may contain a cycle.");

	public static YamlSerializationException NullKey() => new("Keys of dictionaries cannot be null.");

	// the segment of the path of an entry of a dictionary
	public static string GetPath(Node key) => key is ScalarNode scalar ? scalar.Value : "?";

	public static YamlSerializationException Unmapped(Node key, Type type)
	{
		if (key is not ScalarNode scalar)
		{
			return new($"A key, which is not a scalar, does not map to any member of type '{type}'.");
		}

		// the path points to the key itself
		var exception = new YamlSerializationException($"The key '{scalar.Value}' does not map to any member of type '{type}'.");
		exception.Prepend(scalar.Value);
		return exception;
	}

	public static void AddName(Dictionary<string, string> names, string name, string member, Type type)
	{
		if (!names.TryAdd(name, member))
		{
			throw new InvalidOperationException($"The members '{names[name]}' and '{member}' of type '{type}' have the same name '{name}'.");
		}
	}

	// integer discriminators are written as plain integers, the others as text
	public static ScalarNode CreateDiscriminator(string discriminator) => new ScalarNode(Value: discriminator) switch
	{
		var plain when plain.TryGetInt64(out _) => plain,
		_ => new(Value: discriminator, Style: SyntaxFacts.GetTextStyle(discriminator)),
	};

	// the path points to the discriminator itself
	public static YamlSerializationException InvalidDiscriminator(string discriminatorName, string reason)
	{
		var exception = new YamlSerializationException(reason);
		exception.Prepend(discriminatorName);
		return exception;
	}

	public static string UnsupportedRead(Type type, string reason) => $"Cannot deserialize type '{type}'. {reason}";

	public static string UnsupportedWrite(Type type, string reason) => $"Cannot serialize type '{type}'. {reason}";
}
