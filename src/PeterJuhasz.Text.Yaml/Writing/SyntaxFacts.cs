using PeterJuhasz.Text.Yaml.Model;

namespace PeterJuhasz.Text.Yaml.Writing;

// decides which styles can represent a value exactly, so it is read back as the same text
internal static class SyntaxFacts
{
	// text is written as a plain scalar, unless it would be read as another type by the core schema, or it cannot be written as a plain scalar,
	// multi-line text is written as a literal block scalar, when it can be
	public static ScalarStyle GetTextStyle(ReadOnlySpan<char> value)
	{
		if (CanWritePlain(value) && !IsResolvedAsNonString(value))
		{
			return ScalarStyle.Plain;
		}

		if (value.Contains('\n') && CanWriteBlock(value))
		{
			return ScalarStyle.Literal;
		}

		return ScalarStyle.DoubleQuoted;
	}

	// plain scalars are read as null, booleans and numbers by the core schema
	public static bool IsResolvedAsNonString(ReadOnlySpan<char> value)
	{
		// only these characters can start a value of the core schema, so other text is not allocated
		if (value is not [(>= '0' and <= '9') or '+' or '-' or '.' or '~' or 'n' or 'N' or 't' or 'T' or 'f' or 'F', ..] && !value.IsEmpty)
		{
			return false;
		}

		var plain = new ScalarNode(Value: value.ToString());
		return plain.IsNull || plain.TryGetBoolean(out _) || plain.TryGetDouble(out _);
	}

	// whether the text is read back as is from a plain scalar in block context
	public static bool CanWritePlain(ReadOnlySpan<char> value)
	{
		if (value is [] or [' ', ..] or [.., ' ' or ':'])
		{
			return false;
		}

		// indicators cannot start a plain scalar, except for '-', '?' and ':' followed by a non-space character
		if (value is ['-' or '?' or ':'] or ['-' or '?' or ':', ' ', ..] or ['#' or ',' or '[' or ']' or '{' or '}' or '&' or '*' or '!' or '|' or '>' or '\'' or '"' or '%' or '@' or '`', ..])
		{
			return false;
		}

		// document markers are recognized at the start of a line
		if (value.StartsWith("---") || value.StartsWith("..."))
		{
			return false;
		}

		// tabs are allowed by the specification, but they are easy to confuse with indentation
		if (value.Contains(": ", StringComparison.Ordinal) || value.Contains(" #", StringComparison.Ordinal) || value.Contains('\t'))
		{
			return false;
		}

		return IsPrintableLine(value);
	}

	// single quoted scalars can contain any printable character, but line breaks would be folded
	public static bool CanWriteSingleQuoted(ReadOnlySpan<char> value) => IsPrintableLine(value);

	// block scalars keep their lines as they are, but they cannot represent text without content,
	// carriage returns, lines of only white space, and a first line starting with a space would change the indentation
	public static bool CanWriteBlock(ReadOnlySpan<char> value)
	{
		var content = value.TrimEnd('\n');
		if (content.IsEmpty || content.Contains('\r'))
		{
			return false;
		}

		var first = true;
		foreach (var range in content.Split('\n'))
		{
			var line = content[range];
			if (line.IsEmpty)
			{
				continue;
			}

			if (line.IsWhiteSpace() || (first && line[0] is ' ') || !IsPrintableLine(line, allowTab: true))
			{
				return false;
			}

			first = false;
		}

		return true;
	}

	// lines starting with white space are more indented, which are not folded
	public static bool CanWriteFolded(ReadOnlySpan<char> value)
	{
		if (!CanWriteBlock(value))
		{
			return false;
		}

		var content = value.TrimEnd('\n');
		foreach (var range in content.Split('\n'))
		{
			if (content[range] is [' ' or '\t', ..])
			{
				return false;
			}
		}

		return true;
	}

	// printable characters on a single line, line breaks of other specifications, and the byte order mark are excluded too
	private static bool IsPrintableLine(ReadOnlySpan<char> value, bool allowTab = true)
	{
		for (var i = 0; i < value.Length; i++)
		{
			var c = value[i];
			if (Char.IsHighSurrogate(c))
			{
				if (i + 1 >= value.Length || !Char.IsLowSurrogate(value[i + 1]))
				{
					return false;
				}

				i++;
				continue;
			}

			if (!IsPrintable(c) || (c is '\t' && !allowTab))
			{
				return false;
			}
		}

		return true;
	}

	// characters, which can be written as is, without line breaks
	public static bool IsPrintable(char c) => (int)c switch
	{
		0x09 => true,
		>= 0x20 and <= 0x7E => true,
		0x85 or 0x2028 or 0x2029 or 0xFEFF => false,
		>= 0xA0 and <= 0xD7FF => true,
		>= 0xE000 and <= 0xFFFD => true,
		_ => false,
	};
}
