using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using PeterJuhasz.Text.Yaml.Serialization.Converters;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Serialization;

public sealed class YamlSerializerOptions
{
	public static YamlSerializerOptions Default { get; } = new();

	// converts property names to mapping keys, names are used as is when not set
	public YamlNamingPolicy? PropertyNamingPolicy { get; init; }

	public bool PropertyNameCaseInsensitive { get; init; }

	// enum members are matched by name case insensitively, and also by the name converted by this policy when set
	public YamlNamingPolicy? EnumNamingPolicy { get; init; }

	// keys of objects, which do not map to any member, are skipped by default
	public YamlUnmappedMemberHandling UnmappedMemberHandling { get; init; }

	// formats the text, when serializing to a string, writers have their own formatting options
	public YamlWriterFormattingOptions? FormattingOptions { get; init; }

	// the options are immutable, so the converters built from them can be cached
	private readonly ConcurrentDictionary<Type, YamlConverter> converters = new();

	[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
	[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
	internal YamlConverter GetConverter(Type type) => converters.GetOrAdd(type, static (type, options) => YamlConverter.Create(type, options), this);
}
