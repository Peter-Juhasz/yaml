using System.Collections.Immutable;
using PeterJuhasz.Text.Yaml.Model;

namespace PeterJuhasz.Text.Yaml.Parsing;

public static class Parser
{
	public static DocumentNode Parse(string yaml)
	{
		var documents = ParseStream(yaml);
		return documents.Length switch
		{
			0 => new(Root: ScalarNode.Empty),
			1 => documents[0],
			_ => throw new YamlException("The stream contains more than one document.", 1, 1),
		};
	}

	public static ImmutableArray<DocumentNode> ParseStream(string yaml)
	{
		ArgumentNullException.ThrowIfNull(yaml);

		// a byte order mark is not part of the content
		if (yaml is ['﻿', ..])
		{
			yaml = yaml[1..];
		}

		// the null character is not printable, and serves as the end of input sentinel of the reader
		if (yaml.IndexOf('\0', StringComparison.Ordinal) is var index and >= 0)
		{
			throw new YamlReader(yaml).Error(index, "The null character is not allowed.");
		}

		return new YamlReader(yaml).ReadStream();
	}
}
