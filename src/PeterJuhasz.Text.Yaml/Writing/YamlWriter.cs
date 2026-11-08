using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PeterJuhasz.Text.Yaml.Model;

namespace PeterJuhasz.Text.Yaml.Writing;

// writes yaml in block style, empty collections are written in flow style, as block collections cannot be empty
// the requested style of a scalar is used when it represents the value exactly, otherwise a quoted style is written instead
public class YamlWriter<TWriter>(TWriter writer, YamlWriterFormattingOptions? options = null) : IYamlEmitter where TWriter : IBufferWriter<char>
{
	// not readonly, so struct writers are mutated in place instead of on defensive copies
	private TWriter writer = writer ?? throw new ArgumentNullException(nameof(writer));
	private readonly YamlWriterFormattingOptions options = Validate(options ?? YamlWriterFormattingOptions.Default);
	private readonly List<Frame> frames = [];

	// whether the current line has content, which is terminated before the next line is started
	private bool lineStarted;

	// whether a space separates the next content from the indicator written before it, like after 'key:' or '-'
	private bool pendingSpace;

	// whether the root node of the current document is complete
	private bool rootWritten;

	public YamlWriterFormattingOptions Options => options;

	// the number of open mappings and sequences
	public int CurrentDepth => frames.Count;

	private ref Frame Top => ref CollectionsMarshal.AsSpan(frames)[^1];

	private Slot Position => frames.Count switch
	{
		0 => Slot.Root,
		_ when Top.Kind is Container.Sequence => Slot.Item,
		_ when Top.ExpectingValue => Slot.Value,
		_ => Slot.Key,
	};

	public void WriteStartMapping() => WriteStart(Container.Mapping);

	public void WriteEndMapping() => WriteEnd(Container.Mapping);

	public void WriteStartSequence() => WriteStart(Container.Sequence);

	public void WriteEndSequence() => WriteEnd(Container.Sequence);

	public void WriteKey(ReadOnlySpan<char> key, ScalarStyle style = ScalarStyle.Plain)
	{
		if (Position is not Slot.Key)
		{
			throw new InvalidOperationException("A key can only be written in a mapping, which expects a key.");
		}

		WriteScalar(key, style);
	}

	// writes the value in the requested style, or in a quoted style, when the requested one cannot represent it exactly
	// an empty plain scalar is written as nothing, which is read as null
	public void WriteScalar(ReadOnlySpan<char> value, ScalarStyle style = ScalarStyle.Plain)
	{
		var slot = Position;
		style = ResolveStyle(value, style, slot);
		var contentIndent = slot switch
		{
			Slot.Root => options.IndentSize,
			Slot.Item => Top.Indent + 2,
			_ => Top.Indent + options.IndentSize,
		};

		BeginNode(slot);
		switch (style)
		{
			case ScalarStyle.Plain when value.IsEmpty:
				break;

			case ScalarStyle.Plain:
				WritePendingSpace();
				WriteCore(value);
				break;

			case ScalarStyle.SingleQuoted:
				WritePendingSpace();
				WriteSingleQuoted(value);
				break;

			case ScalarStyle.DoubleQuoted:
				WritePendingSpace();
				WriteDoubleQuoted(value);
				break;

			default:
				WritePendingSpace();
				WriteBlock(value, style, contentIndent);
				break;
		}
		EndNode();
	}

	// writes text, so it is read back as a string: as a plain scalar, unless it would be read as another type, or as a literal block scalar for multiple lines
	public void WriteString(ReadOnlySpan<char> value) => WriteScalar(value, SyntaxFacts.GetTextStyle(value));

	public void WriteNull() => WriteScalar("null");

	public void WriteBoolean(bool value) => WriteScalar(value ? "true" : "false");

	// writes the invariant text of the number, and the core schema symbols for infinities and not a number
	public void WriteNumber<T>(T value) where T : INumberBase<T>
	{
		if (T.IsNaN(value))
		{
			WriteScalar(".nan");
			return;
		}

		if (T.IsInfinity(value))
		{
			WriteScalar(T.IsNegative(value) ? "-.inf" : ".inf");
			return;
		}

		BeginNode(Position);
		WritePendingSpace();

		// numbers are formatted directly into the buffer, which is grown until the text fits
		for (var size = 32; ; size *= 2)
		{
			var span = writer.GetSpan(size);
			if (value.TryFormat(span, out var written, format: default, CultureInfo.InvariantCulture))
			{
				writer.Advance(written);
				break;
			}
		}
		EndNode();
	}

	// writes each line of the comment on its own line, it cannot be written between a key and its value
	public void WriteComment(ReadOnlySpan<char> comment)
	{
		var indent = 0;
		if (frames.Count > 0)
		{
			ref var frame = ref Top;
			if (frame is { Kind: Container.Mapping, ExpectingValue: true })
			{
				throw new InvalidOperationException("A comment cannot be written between a key and its value.");
			}

			// the first entry cannot continue the line of the indicator anymore
			indent = frame.Indent;
			if (!frame.HasItems)
			{
				frame.Interrupted = true;
			}
		}

		foreach (var line in comment.EnumerateLines())
		{
			StartLine(indent);
			WriteCore('#');
			if (!line.IsEmpty)
			{
				WriteCore(' ');
				WriteCore(line);
			}
			EndLine();
		}
	}

	// starts a new document, which is needed to write more than one root node
	public void WriteDocumentStart()
	{
		ThrowIfNotAtRoot();
		StartLine(0);
		WriteCore("---");
		EndLine();
		rootWritten = false;
	}

	public void WriteDocumentEnd()
	{
		ThrowIfNotAtRoot();
		StartLine(0);
		WriteCore("...");
		EndLine();
	}

	// writes the node with the styles of its scalars, documents are written as their root
	public void WriteNode(Node node)
	{
		ArgumentNullException.ThrowIfNull(node);
		switch (node)
		{
			case DocumentNode document:
				WriteNode(document.Root);
				break;

			case ScalarNode scalar:
				WriteScalar(scalar.Value, scalar.Style);
				break;

			case SequenceNode sequence:
				WriteStartSequence();
				foreach (var item in sequence.Items)
				{
					WriteNode(item);
				}
				WriteEndSequence();
				break;

			case MappingNode mapping:
				WriteStartMapping();
				foreach (var entry in mapping.Entries)
				{
					WriteNode(entry.Key);
					WriteNode(entry.Value);
				}
				WriteEndMapping();
				break;

			default:
				throw new NotSupportedException($"The node of type '{node.GetType()}' is not supported.");
		}
	}

	void IYamlEmitter.WriteScalar(string value, ScalarStyle style) => WriteScalar(value, style);

	void IYamlEmitter.WriteString(string value) => WriteString(value);

	private void WriteStart(Container kind)
	{
		var slot = Position;
		if (slot is Slot.Key)
		{
			throw new NotSupportedException("Only scalars can be written as keys of mappings.");
		}

		// entries of collections in sequences continue the line of the indicator, like '- key: value'
		var parentIndent = slot is Slot.Root ? 0 : Top.Indent;
		var (indent, flowIndent, inline) = slot switch
		{
			Slot.Root => (0, 0, false),
			Slot.Item => (parentIndent + 2, parentIndent + 2, true),
			_ => (kind is Container.Sequence && !options.IndentSequences ? parentIndent : parentIndent + options.IndentSize, parentIndent + options.IndentSize, false),
		};

		BeginNode(slot, isCollection: true);
		frames.Add(new(kind, indent, flowIndent, inline));
	}

	private void WriteEnd(Container kind)
	{
		if (frames.Count is 0 || Top.Kind != kind)
		{
			throw new InvalidOperationException($"There is no open {kind.ToString().ToLowerInvariant()} to end.");
		}

		var frame = Top;
		if (frame.ExpectingValue)
		{
			throw new InvalidOperationException("The last key of the mapping has no value.");
		}

		frames.RemoveAt(frames.Count - 1);
		if (!frame.HasItems)
		{
			// the root starts its own line, and so does a collection interrupted by a comment
			if (frames.Count is 0 || frame.Interrupted)
			{
				StartLine(frame.FlowIndent);
			}
			else
			{
				WritePendingSpace();
			}

			WriteCore(kind is Container.Mapping ? "{}" : "[]");
		}
		EndNode();
	}

	// positions the output for a node, the indicator of sequence items is written here, collections at the root start their lines with their entries
	private void BeginNode(Slot slot, bool isCollection = false)
	{
		switch (slot)
		{
			case Slot.Root when rootWritten:
				throw new InvalidOperationException("The document already has a root node, write a document start to write another one.");

			case Slot.Root when !isCollection:
				StartLine(0);
				break;

			case Slot.Key:
				StartEntry(ref Top);
				break;

			case Slot.Item:
				StartEntry(ref Top);
				WriteCore('-');
				pendingSpace = true;
				break;
		}
	}

	// a key is followed by its indicator, and the root by the end of its line
	private void EndNode()
	{
		if (frames.Count is 0)
		{
			EndLine();
			rootWritten = true;
			return;
		}

		ref var frame = ref Top;
		if (frame.Kind is Container.Sequence)
		{
			return;
		}

		if (frame.ExpectingValue)
		{
			frame.ExpectingValue = false;
			return;
		}

		WriteCore(':');
		frame.ExpectingValue = true;
		pendingSpace = true;
	}

	private void StartEntry(ref Frame frame)
	{
		if (frame is { Inline: true, HasItems: false, Interrupted: false })
		{
			WritePendingSpace();
		}
		else
		{
			StartLine(frame.Indent);
		}

		frame.HasItems = true;
	}

	private static ScalarStyle ResolveStyle(ReadOnlySpan<char> value, ScalarStyle style, Slot slot) => style switch
	{
		// keys are written on a single line, and an empty plain key could not be written
		ScalarStyle.Plain when value.IsEmpty => slot is Slot.Key ? ScalarStyle.DoubleQuoted : ScalarStyle.Plain,
		ScalarStyle.Plain => SyntaxFacts.CanWritePlain(value) ? ScalarStyle.Plain : ScalarStyle.DoubleQuoted,
		ScalarStyle.SingleQuoted => SyntaxFacts.CanWriteSingleQuoted(value) ? ScalarStyle.SingleQuoted : ScalarStyle.DoubleQuoted,
		ScalarStyle.DoubleQuoted => ScalarStyle.DoubleQuoted,
		ScalarStyle.Literal or ScalarStyle.Folded when slot is Slot.Key => ScalarStyle.DoubleQuoted,
		ScalarStyle.Folded when SyntaxFacts.CanWriteFolded(value) => ScalarStyle.Folded,
		ScalarStyle.Literal or ScalarStyle.Folded when SyntaxFacts.CanWriteBlock(value) => ScalarStyle.Literal,
		ScalarStyle.Literal or ScalarStyle.Folded => ScalarStyle.DoubleQuoted,
		_ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown scalar style."),
	};

	private void WriteSingleQuoted(ReadOnlySpan<char> value)
	{
		WriteCore('\'');
		int index;
		while ((index = value.IndexOf('\'')) >= 0)
		{
			WriteCore(value[..(index + 1)]);
			WriteCore('\'');
			value = value[(index + 1)..];
		}
		WriteCore(value);
		WriteCore('\'');
	}

	private void WriteDoubleQuoted(ReadOnlySpan<char> value)
	{
		WriteCore('"');
		var start = 0;
		for (var i = 0; i < value.Length; i++)
		{
			var c = value[i];

			// surrogate pairs are written as is, lone surrogates are escaped
			if (Char.IsHighSurrogate(c) && i + 1 < value.Length && Char.IsLowSurrogate(value[i + 1]))
			{
				i++;
				continue;
			}

			if (c is not ('"' or '\\') && SyntaxFacts.IsPrintable(c) && c is not '\t')
			{
				continue;
			}

			WriteCore(value[start..i]);
			start = i + 1;
			switch ((int)c)
			{
				case '"': WriteCore("\\\""); break;
				case '\\': WriteCore("\\\\"); break;
				case 0x00: WriteCore("\\0"); break;
				case 0x07: WriteCore("\\a"); break;
				case 0x08: WriteCore("\\b"); break;
				case 0x09: WriteCore("\\t"); break;
				case 0x0A: WriteCore("\\n"); break;
				case 0x0B: WriteCore("\\v"); break;
				case 0x0C: WriteCore("\\f"); break;
				case 0x0D: WriteCore("\\r"); break;
				case 0x1B: WriteCore("\\e"); break;
				case 0x85: WriteCore("\\N"); break;
				case 0x2028: WriteCore("\\L"); break;
				case 0x2029: WriteCore("\\P"); break;
				case <= 0xFF: WriteEscape('x', c, digits: 2); break;
				default: WriteEscape('u', c, digits: 4); break;
			}
		}
		WriteCore(value[start..]);
		WriteCore('"');
	}

	private void WriteEscape(char kind, char c, int digits)
	{
		var span = writer.GetSpan(2 + digits);
		span[0] = '\\';
		span[1] = kind;
		((int)c).TryFormat(span[2..], out _, digits is 2 ? "X2" : "X4", CultureInfo.InvariantCulture);
		writer.Advance(2 + digits);
	}

	// the chomping indicator keeps the trailing line breaks, folded scalars separate their lines by an empty line, so they are not folded into spaces
	private void WriteBlock(ReadOnlySpan<char> value, ScalarStyle style, int indent)
	{
		var content = value.TrimEnd('\n');
		var trailing = value.Length - content.Length;
		WriteCore(style is ScalarStyle.Literal ? '|' : '>');
		switch (trailing)
		{
			case 0:
				WriteCore('-');
				break;

			case > 1:
				WriteCore('+');
				break;
		}

		var hasContent = false;
		foreach (var range in content.Split('\n'))
		{
			var line = content[range];
			if (style is ScalarStyle.Folded && hasContent && !line.IsEmpty)
			{
				WriteNewLine();
			}

			WriteNewLine();
			if (!line.IsEmpty)
			{
				WriteIndent(indent);
				WriteCore(line);
				hasContent = true;
			}
		}

		// the first trailing line break ends the last line, the rest are empty lines
		for (var i = 1; i < trailing; i++)
		{
			WriteNewLine();
		}

		lineStarted = true;
	}

	private void ThrowIfNotAtRoot()
	{
		if (frames.Count > 0)
		{
			throw new InvalidOperationException("Documents can only be started and ended outside of mappings and sequences.");
		}
	}

	private void StartLine(int indent)
	{
		if (lineStarted)
		{
			WriteNewLine();
		}

		pendingSpace = false;
		WriteIndent(indent);
		lineStarted = true;
	}

	private void EndLine()
	{
		WriteNewLine();
		lineStarted = false;
		pendingSpace = false;
	}

	private void WritePendingSpace()
	{
		if (pendingSpace)
		{
			WriteCore(' ');
			pendingSpace = false;
		}
	}

	private void WriteNewLine() => WriteCore(options.NewLine);

	private void WriteIndent(int indent)
	{
		if (indent is 0)
		{
			return;
		}

		writer.GetSpan(indent)[..indent].Fill(' ');
		writer.Advance(indent);
	}

	// avoids BuffersExtensions.Write, which boxes struct writers and relies on GetSpan(0)
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void WriteCore(ReadOnlySpan<char> value)
	{
		if (value.IsEmpty)
		{
			return;
		}

		value.CopyTo(writer.GetSpan(value.Length));
		writer.Advance(value.Length);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void WriteCore(char value)
	{
		writer.GetSpan(1)[0] = value;
		writer.Advance(1);
	}

	private static YamlWriterFormattingOptions Validate(YamlWriterFormattingOptions options)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(options.IndentSize, 1, nameof(options));
		if (options.NewLine is not ("\n" or "\r\n"))
		{
			throw new ArgumentException("The new line must be either '\\n' or '\\r\\n'.", nameof(options));
		}

		return options;
	}

	private enum Container : byte
	{
		Mapping,
		Sequence,
	}

	// where the next node is written
	private enum Slot : byte
	{
		Root,
		Key,
		Value,
		Item,
	}

	// an open collection, whose entries are written at the indentation, and its flow form at the flow indentation when it is empty after a comment
	private struct Frame(Container kind, int indent, int flowIndent, bool inline)
	{
		public readonly Container Kind = kind;
		public readonly int Indent = indent;
		public readonly int FlowIndent = flowIndent;
		public readonly bool Inline = inline;
		public bool HasItems;
		public bool ExpectingValue;
		public bool Interrupted;
	}
}

public class YamlWriter(IBufferWriter<char> writer, YamlWriterFormattingOptions? options = null) : YamlWriter<IBufferWriter<char>>(writer, options)
{
	public static YamlWriter<TWriter> Create<TWriter>(TWriter writer, YamlWriterFormattingOptions? options = null) where TWriter : IBufferWriter<char> => new(writer, options);
}
