using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.ObjectPool;
using PeterJuhasz.Text.Yaml.Model;

namespace PeterJuhasz.Text.Yaml.Parsing;

internal sealed class YamlReader(string text)
{
	private int _position;

	private bool AtEnd => _position >= text.Length;

	private bool IsAtLineStart => _position == 0 || IsBreak(text[_position - 1]);

	private int Column
	{
		get
		{
			var lineStart = _position;
			while (lineStart > 0 && !IsBreak(text[lineStart - 1]))
			{
				lineStart--;
			}
			return _position - lineStart;
		}
	}


	public ImmutableArray<DocumentNode> ReadStream()
	{
		using var documents = new PooledArrayBuilder<DocumentNode>();

		SkipToContent();
		while (!AtEnd)
		{
			// directives are skipped
			if (IsAtLineStart && Peek() is '%')
			{
				SkipToEndOfLine();
				SkipToContent();
				continue;
			}

			// a document end marker without a document
			if (IsAtDocumentEnd())
			{
				_position += 3;
				FinishLine();
				SkipToContent();
				continue;
			}

			Node? root = null;
			if (IsAtDocumentStart())
			{
				_position += 3;
				SkipSpaces();
				if (IsAtEndOfLineOrComment())
				{
					FinishLine();
					SkipToContent();
				}
				else
				{
					root = ReadBlockNode(parentIndent: -1, allowCollections: false);
				}
			}

			root ??= AtEnd || IsAtDocumentMarker() ? ScalarNode.Empty : ReadBlockNode(parentIndent: -1, allowCollections: true);
			documents.Add(new(Root: root));

			if (AtEnd)
			{
				break;
			}

			if (IsAtDocumentEnd())
			{
				_position += 3;
				FinishLine();
				SkipToContent();
				continue;
			}

			if (IsAtDocumentStart())
			{
				continue;
			}

			throw Error("Unexpected content after the end of the document.");
		}

		return documents.ToImmutableArray();
	}


	private Node ReadBlockNode(int parentIndent, bool allowCollections)
	{
		ThrowIfUnsupported();

		var column = Column;
		var c = Peek();

		if (c is '?' && IsBlank(Peek(1)))
		{
			throw Error("Explicit mapping keys are not supported.");
		}

		if (c is '@' or '`' or ',' or ']' or '}')
		{
			throw Error($"Unexpected character '{c}'.");
		}

		if (c is '-' && IsBlank(Peek(1)))
		{
			return allowCollections ? ReadBlockSequence(column) : throw Error("A block sequence is not allowed in this context.");
		}

		if (c is '|' or '>')
		{
			return ReadBlockScalar(parentIndent);
		}

		if (allowCollections && LooksLikeMappingKey())
		{
			return ReadBlockMapping(column);
		}

		if (c is '[' or '{' or '"' or '\'')
		{
			var node = ReadFlowNode();
			FinishLine();
			SkipToContent();
			return node;
		}

		return ReadBlockPlainScalar(parentIndent);
	}

	private SequenceNode ReadBlockSequence(int indent)
	{
		using var items = new PooledArrayBuilder<Node>();

		while (true)
		{
			// skip the '-' indicator
			_position++;
			items.Add(ReadBlockValue(indent, inSequence: true));

			if (AtEnd || IsAtDocumentMarker())
			{
				break;
			}

			var column = Column;
			if (column == indent && Peek() is '-' && IsBlank(Peek(1)))
			{
				continue;
			}

			if (column <= indent)
			{
				break;
			}

			throw Error("Bad indentation of a sequence entry.");
		}

		return new(Items: items.ToImmutableArray());
	}

	private MappingNode ReadBlockMapping(int indent)
	{
		using var entries = new PooledArrayBuilder<MappingEntry>();
		using var keysLease = HashSetPool<Node>.GetPooledObject(out var keys);

		while (true)
		{
			var keyPosition = _position;
			var key = ReadMappingKey();
			if (!keys.Add(key))
			{
				throw Error(keyPosition, "Duplicate mapping key.");
			}

			// skip the ':' indicator
			_position++;
			var value = ReadBlockValue(indent, inSequence: false);
			entries.Add(new(Key: key, Value: value));

			if (AtEnd || IsAtDocumentMarker())
			{
				break;
			}

			var column = Column;
			if (column < indent)
			{
				break;
			}

			if (column == indent)
			{
				if (!LooksLikeMappingKey())
				{
					throw Error("Expected a mapping key.");
				}
				continue;
			}

			throw Error("Bad indentation of a mapping entry.");
		}

		return new(Entries: entries.ToImmutableArray());
	}

	private Node ReadBlockValue(int indent, bool inSequence)
	{
		SkipSpaces();

		// the value is on the same line
		if (!IsAtEndOfLineOrComment())
		{
			return ReadBlockNode(indent, allowCollections: inSequence);
		}

		// the value is on the following lines
		FinishLine();
		SkipToContent();
		if (AtEnd || IsAtDocumentMarker())
		{
			return ScalarNode.Empty;
		}

		var column = Column;
		if (column > indent)
		{
			return ReadBlockNode(indent, allowCollections: true);
		}

		// a sequence may be on the same indentation level as its key
		if (!inSequence && column == indent && Peek() is '-' && IsBlank(Peek(1)))
		{
			return ReadBlockSequence(column);
		}

		return ScalarNode.Empty;
	}

	private ScalarNode ReadMappingKey()
	{
		ThrowIfUnsupported();

		ScalarNode key;
		if (Peek() is '"' or '\'')
		{
			key = ReadQuotedScalar();
			SkipSpaces();
		}
		else
		{
			key = new(Value: ReadPlainLine());
		}

		return Peek() is ':' ? key : throw Error("Expected ':'.");
	}

	private bool LooksLikeMappingKey()
	{
		var index = _position;
		var c = CharAt(index);

		if (c is '"' or '\'')
		{
			index++;
			while (true)
			{
				var d = CharAt(index);
				if (d is '\0' || IsBreak(d))
				{
					return false;
				}

				if (c is '"' && d is '\\')
				{
					if (IsBreak(CharAt(index + 1)))
					{
						return false;
					}
					index += 2;
					continue;
				}

				if (d == c)
				{
					if (c is '\'' && CharAt(index + 1) is '\'')
					{
						index += 2;
						continue;
					}
					index++;
					break;
				}

				index++;
			}

			while (CharAt(index) is ' ' or '\t')
			{
				index++;
			}

			return CharAt(index) is ':' && IsBlank(CharAt(index + 1));
		}

		if (c is '[' or '{')
		{
			return false;
		}

		while (true)
		{
			var d = CharAt(index);
			if (d is '\0' || IsBreak(d))
			{
				return false;
			}

			if (d is ':' && IsBlank(CharAt(index + 1)))
			{
				return true;
			}

			if (d is ' ' or '\t' && CharAt(index + 1) is '#')
			{
				return false;
			}

			index++;
		}
	}


	private ScalarNode ReadBlockPlainScalar(int parentIndent)
	{
		var value = ReadPlainLine();
		ThrowIfMappingValue();

		// most scalars are on a single line, so a builder is only needed for continuation lines
		if (!SkipComment() && TryStartContinuationLine(parentIndent, out var emptyLines))
		{
			using var lease = StringBuilderPool.GetPooledObject(out var builder);
			builder.Append(value);
			do
			{
				AppendFoldedLineBreaks(builder, emptyLines);
				builder.Append(ReadPlainLineSpan());
				ThrowIfMappingValue();
			}
			while (!SkipComment() && TryStartContinuationLine(parentIndent, out emptyLines));

			value = builder.ToString();
		}

		SkipToContent();
		return new(Value: value);
	}

	private bool SkipComment()
	{
		SkipSpaces();
		if (Peek() is not '#')
		{
			return false;
		}

		// a comment ends a plain scalar
		SkipToEndOfLine();
		return true;
	}

	private bool TryStartContinuationLine(int parentIndent, out int emptyLines)
	{
		emptyLines = 0;
		if (AtEnd)
		{
			return false;
		}

		var save = _position;
		ConsumeLineBreak();

		// skip empty lines, and find the indentation of the next one
		int indent;
		while (true)
		{
			var lineStart = _position;
			while (Peek() is ' ')
			{
				_position++;
			}
			indent = _position - lineStart;

			while (Peek() is ' ' or '\t')
			{
				_position++;
			}

			if (!IsBreak(Peek()))
			{
				break;
			}

			emptyLines++;
			ConsumeLineBreak();
		}

		if (AtEnd || Peek() is '#' || indent <= parentIndent || IsDocumentMarkerAt(_position - indent))
		{
			_position = save;
			return false;
		}

		return true;
	}

	private static void AppendFoldedLineBreaks(StringBuilder builder, int emptyLines)
	{
		// a single line break is folded into a space, otherwise only the empty lines are kept
		if (emptyLines is 0)
		{
			builder.Append(' ');
		}
		else
		{
			builder.Append('\n', emptyLines);
		}
	}

	private string ReadPlainLine() => ReadPlainLineSpan().ToString();

	private ReadOnlySpan<char> ReadPlainLineSpan()
	{
		var start = _position;
		while (true)
		{
			var c = Peek();
			if (c is '\0' || IsBreak(c))
			{
				break;
			}

			if (c is ':' && IsBlank(Peek(1)))
			{
				break;
			}

			if (c is ' ' or '\t' && Peek(1) is '#')
			{
				break;
			}

			_position++;
		}

		return text.AsSpan(start, _position - start).TrimEnd(" \t");
	}

	private void ThrowIfMappingValue()
	{
		if (Peek() is ':')
		{
			throw Error("Mapping values are not allowed in this context.");
		}
	}


	private ScalarNode ReadBlockScalar(int parentIndent)
	{
		var style = Peek() is '|' ? ScalarStyle.Literal : ScalarStyle.Folded;
		_position++;

		// header
		char? chomping = null;
		var explicitIndent = 0;
		for (var i = 0; i < 2; i++)
		{
			switch (Peek())
			{
				case '-' or '+' when chomping is null:
					chomping = Peek();
					_position++;
					break;

				case >= '1' and <= '9' when explicitIndent is 0:
					explicitIndent = Peek() - '0';
					_position++;
					break;
			}
		}

		FinishLine();
		ConsumeLineBreak();

		// indentation
		var baseIndent = Math.Max(parentIndent, 0);
		var contentIndent = explicitIndent > 0 ? baseIndent + explicitIndent : Math.Max(DetectBlockScalarIndent(), parentIndent + 1);

		// lines are appended as they are read, empty lines are counted until the next content line decides how they are kept
		using var lease = StringBuilderPool.GetPooledObject(out var builder);
		var first = true;
		var previousMoreIndented = false;
		var emptyLines = 0;
		while (!AtEnd)
		{
			var lineStart = _position;
			if (contentIndent is 0 && IsDocumentMarkerAt(lineStart))
			{
				break;
			}

			var spaces = 0;
			while (CharAt(lineStart + spaces) is ' ')
			{
				spaces++;
			}

			var lineEnd = lineStart + spaces;
			var blank = true;
			while (CharAt(lineEnd) is not '\0' && !IsBreak(CharAt(lineEnd)))
			{
				blank &= CharAt(lineEnd) is ' ' or '\t';
				lineEnd++;
			}

			if (!blank && spaces < contentIndent)
			{
				break;
			}

			_position = lineEnd;
			ConsumeLineBreak();

			if (blank)
			{
				emptyLines++;
				continue;
			}

			var line = text.AsSpan(lineStart + contentIndent, lineEnd - lineStart - contentIndent);
			var moreIndented = line[0] is ' ' or '\t';
			if (first)
			{
				builder.Append('\n', emptyLines);
			}
			else if (style is ScalarStyle.Folded && !moreIndented && !previousMoreIndented)
			{
				AppendFoldedLineBreaks(builder, emptyLines);
			}
			else
			{
				// line breaks of literal scalars, and around more indented lines of folded ones are preserved
				builder.Append('\n', emptyLines + 1);
			}

			builder.Append(line);
			first = false;
			previousMoreIndented = moreIndented;
			emptyLines = 0;
		}

		// the empty lines left are the trailing ones
		switch (chomping)
		{
			case null when !first:
				builder.Append('\n');
				break;

			case '+':
				if (!first)
				{
					builder.Append('\n');
				}
				builder.Append('\n', emptyLines);
				break;
		}

		SkipToContent();
		return new(Value: builder.ToString(), Style: style);
	}

	private int DetectBlockScalarIndent()
	{
		var index = _position;
		while (index < text.Length)
		{
			var spaces = 0;
			while (CharAt(index + spaces) is ' ')
			{
				spaces++;
			}

			var lineEnd = index + spaces;
			while (CharAt(lineEnd) is ' ' or '\t')
			{
				lineEnd++;
			}

			var c = CharAt(lineEnd);
			if (c is '\0')
			{
				break;
			}

			if (!IsBreak(c))
			{
				return spaces;
			}

			index = lineEnd + (c is '\r' && CharAt(lineEnd + 1) is '\n' ? 2 : 1);
		}

		return 0;
	}


	private Node ReadFlowNode()
	{
		ThrowIfUnsupported();

		return Peek() switch
		{
			'[' => ReadFlowSequence(),
			'{' => ReadFlowMapping(),
			'"' or '\'' => ReadQuotedScalar(),
			_ => ReadFlowPlainScalar(),
		};
	}

	private SequenceNode ReadFlowSequence()
	{
		var start = _position;
		_position++;

		using var items = new PooledArrayBuilder<Node>();
		while (true)
		{
			SkipFlowWhitespace(start);
			if (Peek() is ']')
			{
				_position++;
				break;
			}

			var item = ReadFlowNode();
			SkipFlowWhitespace(start);

			// single pair mapping
			if (Peek() is ':')
			{
				_position++;
				var value = ReadFlowValue(start);
				item = new MappingNode(Entries: [new(Key: item, Value: value)]);
				SkipFlowWhitespace(start);
			}

			items.Add(item);

			if (Peek() is ',')
			{
				_position++;
				continue;
			}

			if (Peek() is ']')
			{
				_position++;
				break;
			}

			throw Error("Expected ',' or ']'.");
		}

		return new(Items: items.ToImmutableArray());
	}

	private MappingNode ReadFlowMapping()
	{
		var start = _position;
		_position++;

		using var entries = new PooledArrayBuilder<MappingEntry>();
		using var keysLease = HashSetPool<Node>.GetPooledObject(out var keys);
		while (true)
		{
			SkipFlowWhitespace(start);
			if (Peek() is '}')
			{
				_position++;
				break;
			}

			var keyPosition = _position;
			var key = ReadFlowNode();
			if (!keys.Add(key))
			{
				throw Error(keyPosition, "Duplicate mapping key.");
			}

			SkipFlowWhitespace(start);
			Node value = ScalarNode.Empty;
			if (Peek() is ':')
			{
				_position++;
				value = ReadFlowValue(start);
				SkipFlowWhitespace(start);
			}

			entries.Add(new(Key: key, Value: value));

			if (Peek() is ',')
			{
				_position++;
				continue;
			}

			if (Peek() is '}')
			{
				_position++;
				break;
			}

			throw Error("Expected ',' or '}'.");
		}

		return new(Entries: entries.ToImmutableArray());
	}

	private Node ReadFlowValue(int start)
	{
		SkipFlowWhitespace(start);
		return Peek() is ',' or ']' or '}' ? ScalarNode.Empty : ReadFlowNode();
	}

	private ScalarNode ReadFlowPlainScalar()
	{
		var first = Peek();
		if (first is '\0' or ',' or '[' or ']' or '{' or '}' or '#' or '@' or '`' || IsBreak(first) || (first is ':' or '-' or '?' && IsFlowBlank(Peek(1))))
		{
			throw Error($"Unexpected character '{first}'.");
		}

		// the text is copied in segments between line breaks, and a builder is only needed when a line break is folded
		var segmentStart = _position;
		var lease = default(PooledObject<StringBuilder>);
		StringBuilder? builder = null;
		try
		{
			while (true)
			{
				var c = Peek();
				if (c is '\0' or ',' or '[' or ']' or '{' or '}' || (c is ':' && IsFlowBlank(Peek(1))))
				{
					break;
				}

				if (c is not (' ' or '\t' or '\n' or '\r'))
				{
					_position++;
					continue;
				}

				var whitespaceStart = _position;
				var breaks = 0;
				while (true)
				{
					var d = Peek();
					if (d is ' ' or '\t')
					{
						_position++;
					}
					else if (IsBreak(d))
					{
						breaks++;
						ConsumeLineBreak();
					}
					else
					{
						break;
					}
				}

				var next = Peek();
				if (next is '\0' or ',' or '[' or ']' or '{' or '}' or '#' || (next is ':' && IsFlowBlank(Peek(1))))
				{
					_position = whitespaceStart;
					break;
				}

				// white space within a line is kept as it is
				if (breaks is 0)
				{
					continue;
				}

				if (builder is null)
				{
					lease = StringBuilderPool.GetPooledObject(out builder);
				}

				builder.Append(text.AsSpan(segmentStart, whitespaceStart - segmentStart));
				AppendFoldedLineBreaks(builder, breaks - 1);
				segmentStart = _position;
			}

			if (builder is null)
			{
				return new(Value: text[segmentStart.._position]);
			}

			builder.Append(text.AsSpan(segmentStart, _position - segmentStart));
			return new(Value: builder.ToString());
		}
		finally
		{
			lease.Dispose();
		}
	}

	private void SkipFlowWhitespace(int start)
	{
		while (true)
		{
			if (AtEnd)
			{
				throw Error(start, "Unterminated flow collection.");
			}

			var c = Peek();
			if (c is ' ' or '\t')
			{
				_position++;
			}
			else if (IsBreak(c))
			{
				ConsumeLineBreak();
				if (IsAtDocumentMarker())
				{
					throw Error(start, "Unterminated flow collection.");
				}
			}
			else if (c is '#' && IsWhiteSpace(CharAt(_position - 1)))
			{
				SkipToEndOfLine();
			}
			else
			{
				break;
			}
		}
	}


	private ScalarNode ReadQuotedScalar()
	{
		var quote = Peek();
		var start = _position;
		_position++;

		var style = quote is '"' ? ScalarStyle.DoubleQuoted : ScalarStyle.SingleQuoted;
		var specials = quote is '"' ? DoubleQuotedSpecials : SingleQuotedSpecials;

		// most quoted scalars have no escapes and line breaks, so they are a substring of the text
		var rest = text.AsSpan(_position);
		if (rest.IndexOfAny(specials) is var end and >= 0 && rest[end] == quote && (quote is '"' || CharAt(_position + end + 1) is not '\''))
		{
			_position += end + 1;
			return new(Value: rest[..end].ToString(), Style: style);
		}

		using var lease = StringBuilderPool.GetPooledObject(out var builder);

		// the part of the builder which can't be trimmed by folding, because it was escaped
		var keep = 0;
		while (true)
		{
			if (AtEnd)
			{
				throw Error(start, "Unterminated quoted scalar.");
			}

			var c = Peek();
			if (c == quote)
			{
				if (quote is '\'' && Peek(1) is '\'')
				{
					builder.Append('\'');
					_position += 2;
					keep = builder.Length;
					continue;
				}

				_position++;
				break;
			}

			if (IsBreak(c))
			{
				FoldQuotedLineBreak(builder, keep, start);
				keep = builder.Length;
				continue;
			}

			if (quote is '"' && c is '\\')
			{
				ReadEscapeSequence(builder);
				keep = builder.Length;
				continue;
			}

			// copy everything up to the next character which needs attention
			var chunk = text.AsSpan(_position);
			var length = chunk.IndexOfAny(specials) is var index and >= 0 ? index : chunk.Length;
			builder.Append(chunk[..length]);
			_position += length;
		}

		return new(Value: builder.ToString(), Style: style);
	}

	private static readonly SearchValues<char> SingleQuotedSpecials = SearchValues.Create("'\r\n");

	private static readonly SearchValues<char> DoubleQuotedSpecials = SearchValues.Create("\"\\\r\n");

	private void FoldQuotedLineBreak(StringBuilder builder, int keep, int start)
	{
		// trailing white space is trimmed
		var length = builder.Length;
		while (length > keep && builder[length - 1] is ' ' or '\t')
		{
			length--;
		}
		builder.Length = length;

		ConsumeLineBreak();
		var emptyLines = 0;
		while (true)
		{
			if (IsAtDocumentMarker())
			{
				throw Error(start, "Unterminated quoted scalar.");
			}

			while (Peek() is ' ' or '\t')
			{
				_position++;
			}

			if (!IsBreak(Peek()))
			{
				break;
			}

			emptyLines++;
			ConsumeLineBreak();
		}

		AppendFoldedLineBreaks(builder, emptyLines);
	}

	private void ReadEscapeSequence(StringBuilder builder)
	{
		var start = _position;

		// skip the backslash
		_position++;
		var c = Peek();

		// escaped line break
		if (IsBreak(c))
		{
			ConsumeLineBreak();
			while (true)
			{
				while (Peek() is ' ' or '\t')
				{
					_position++;
				}

				if (!IsBreak(Peek()))
				{
					break;
				}

				builder.Append('\n');
				ConsumeLineBreak();
			}
			return;
		}

		_position++;
		switch (c)
		{
			case '0': builder.Append('\0'); break;
			case 'a': builder.Append('\a'); break;
			case 'b': builder.Append('\b'); break;
			case 't' or '\t': builder.Append('\t'); break;
			case 'n': builder.Append('\n'); break;
			case 'v': builder.Append('\v'); break;
			case 'f': builder.Append('\f'); break;
			case 'r': builder.Append('\r'); break;
			case 'e': builder.Append('\x1B'); break;
			case ' ': builder.Append(' '); break;
			case '"': builder.Append('"'); break;
			case '/': builder.Append('/'); break;
			case '\\': builder.Append('\\'); break;
			case 'N': builder.Append('\u0085'); break;
			case '_': builder.Append((char)0x00A0); break;
			case 'L': builder.Append((char)0x2028); break;
			case 'P': builder.Append((char)0x2029); break;
			case 'x': builder.Append((char)ReadHex(2, start)); break;
			case 'u': builder.Append((char)ReadHex(4, start)); break;
			case 'U':
				var codePoint = ReadHex(8, start);
				if (codePoint is < 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
				{
					throw Error(start, "Invalid Unicode code point in escape sequence.");
				}
				builder.Append(Char.ConvertFromUtf32(codePoint));
				break;

			default:
				throw Error(start, "Invalid escape sequence.");
		}
	}

	private int ReadHex(int length, int start)
	{
		if (_position + length > text.Length || text.AsSpan(_position, length).ContainsAnyExcept(HexDigits))
		{
			throw Error(start, "Invalid escape sequence.");
		}

		var value = Int32.Parse(text.AsSpan(_position, length), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
		_position += length;
		return value;
	}

	private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");


	private void SkipToContent()
	{
		ConsumeLineBreak();
		while (!AtEnd)
		{
			var contentStart = _position;
			while (Peek() is ' ')
			{
				_position++;
			}
			var indentEnd = _position;
			while (Peek() is ' ' or '\t')
			{
				_position++;
			}

			var c = Peek();
			if (IsBreak(c))
			{
				ConsumeLineBreak();
				continue;
			}

			if (c is '\0')
			{
				return;
			}

			if (c is '#')
			{
				SkipToEndOfLine();
				ConsumeLineBreak();
				continue;
			}

			if (_position != indentEnd)
			{
				throw Error(contentStart, "Tabs are not allowed for indentation.");
			}

			return;
		}
	}

	private void FinishLine()
	{
		SkipSpaces();

		var c = Peek();
		if (c is '#')
		{
			if (!IsWhiteSpace(CharAt(_position - 1)))
			{
				throw Error("A comment must be separated from other tokens by white space.");
			}
			SkipToEndOfLine();
			return;
		}

		if (c is not '\0' && !IsBreak(c))
		{
			throw Error($"Unexpected character '{c}'.");
		}
	}

	private void SkipSpaces()
	{
		while (Peek() is ' ' or '\t')
		{
			_position++;
		}
	}

	private void SkipToEndOfLine()
	{
		while (Peek() is not '\0' && !IsBreak(Peek()))
		{
			_position++;
		}
	}

	private void ConsumeLineBreak()
	{
		switch (Peek())
		{
			case '\r':
				_position += Peek(1) is '\n' ? 2 : 1;
				break;

			case '\n':
				_position++;
				break;
		}
	}

	private bool IsAtEndOfLineOrComment() => Peek() is '\0' or '#' || IsBreak(Peek());

	private bool IsAtDocumentMarker() => IsDocumentMarkerAt(_position);

	private bool IsAtDocumentStart() => IsAtDocumentMarker() && Peek() is '-';

	private bool IsAtDocumentEnd() => IsAtDocumentMarker() && Peek() is '.';

	private bool IsDocumentMarkerAt(int index) =>
		(index is 0 || IsBreak(text[index - 1])) &&
		CharAt(index) is '-' or '.' &&
		CharAt(index + 1) == CharAt(index) &&
		CharAt(index + 2) == CharAt(index) &&
		IsBlank(CharAt(index + 3));

	private void ThrowIfUnsupported()
	{
		if (Peek() is '&' or '*' or '!')
		{
			throw Error("Anchors, aliases and tags are not supported.");
		}
	}


	private char Peek(int offset = 0) => CharAt(_position + offset);

	private char CharAt(int index) => index >= 0 && index < text.Length ? text[index] : '\0';

	private static bool IsBreak(char c) => c is '\n' or '\r';

	private static bool IsWhiteSpace(char c) => c is ' ' or '\t' or '\n' or '\r';

	private static bool IsBlank(char c) => c is '\0' or ' ' or '\t' or '\n' or '\r';

	private static bool IsFlowBlank(char c) => IsBlank(c) || c is ',' or '[' or ']' or '{' or '}';


	private YamlException Error(string message) => Error(_position, message);

	public YamlException Error(int position, string message)
	{
		var line = 1;
		var lineStart = 0;
		for (var i = 0; i < position && i < text.Length; i++)
		{
			if (text[i] is '\n' || (text[i] is '\r' && CharAt(i + 1) is not '\n'))
			{
				line++;
				lineStart = i + 1;
			}
		}

		return new(message, line, position - lineStart + 1);
	}
}
