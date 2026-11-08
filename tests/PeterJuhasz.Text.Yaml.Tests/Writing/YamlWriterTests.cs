using System.Buffers;
using System.Numerics;
using System.Text;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Parsing;
using PeterJuhasz.Text.Yaml.Writing;
using static PeterJuhasz.Text.Yaml.Tests.Yaml;

namespace PeterJuhasz.Text.Yaml.Tests.Writing;

[TestClass]
public class YamlWriterTests
{
	private static string Write(Action<YamlWriter<ArrayBufferWriter<char>>> write, YamlWriterFormattingOptions? options = null)
	{
		var buffer = new ArrayBufferWriter<char>();
		write(YamlWriter.Create(buffer, options));
		return buffer.WrittenSpan.ToString();
	}

	private static string Write(Node node, YamlWriterFormattingOptions? options = null) => Write(writer => writer.WriteNode(node), options);

	[TestMethod]
	public void Mapping()
	{
		var yaml = Write(static writer =>
		{
			writer.WriteStartMapping();
			writer.WriteKey("name");
			writer.WriteString("app");
			writer.WriteKey("port");
			writer.WriteNumber(80);
			writer.WriteKey("enabled");
			writer.WriteBoolean(true);
			writer.WriteKey("parent");
			writer.WriteNull();
			writer.WriteEndMapping();
		});

		Assert.AreEqual("name: app\nport: 80\nenabled: true\nparent: null\n", yaml);
	}

	[TestMethod]
	public void NestedMapping() => Assert.AreEqual("a:\n  b:\n    c: 1\n  d: 2\ne: 3\n", Write(Map(("a", Map(("b", Map(("c", Plain("1")))), ("d", Plain("2")))), ("e", Plain("3")))));

	[TestMethod]
	public void SequenceInMapping()
	{
		var node = Map(("a", Seq(Plain("1"), Plain("2"))), ("b", Plain("3")));
		Assert.AreEqual("a:\n  - 1\n  - 2\nb: 3\n", Write(node));
		Assert.AreEqual("a:\n- 1\n- 2\nb: 3\n", Write(node, new(IndentSequences: false)));
		Assert.AreEqual("a:\n    - 1\n    - 2\nb: 3\n", Write(node, new(IndentSize: 4)));
	}

	[TestMethod]
	public void MappingsInSequence() => Assert.AreEqual("- a: 1\n  b: 2\n- c:\n    d: 3\n", Write(Seq(Map(("a", Plain("1")), ("b", Plain("2"))), Map(("c", Map(("d", Plain("3"))))))));

	[TestMethod]
	public void NestedSequences() => Assert.AreEqual("- - a\n  - - b\n- c\n", Write(Seq(Seq(Plain("a"), Seq(Plain("b"))), Plain("c"))));

	[TestMethod]
	public void SequenceOfMappingsInMapping() => Assert.AreEqual("items:\n  - name: a\n    value: 1\n", Write(Map(("items", Seq(Map(("name", Plain("a")), ("value", Plain("1"))))))));

	[TestMethod]
	public void EmptyCollections()
	{
		Assert.AreEqual("{}\n", Write(new MappingNode(Entries: [])));
		Assert.AreEqual("[]\n", Write(Seq()));
		Assert.AreEqual("a: {}\nb: []\n", Write(Map(("a", new MappingNode(Entries: [])), ("b", Seq()))));
		Assert.AreEqual("- {}\n- []\n- - []\n", Write(Seq(new MappingNode(Entries: []), Seq(), Seq(Seq()))));
	}

	[TestMethod]
	public void EmptyPlainScalar()
	{
		Assert.AreEqual("a:\nb: 1\n", Write(Map(("a", Empty), ("b", Plain("1")))));
		Assert.AreEqual("-\n- b\n", Write(Seq(Empty, Plain("b"))));

		// an empty key cannot be written as a plain scalar
		Assert.AreEqual("\"\": 1\n", Write(Map((Empty, Plain("1")))));
	}

	[TestMethod]
	public void RootScalars()
	{
		Assert.AreEqual("text\n", Write(Plain("text")));
		Assert.AreEqual("'text'\n", Write(Single("text")));
		Assert.AreEqual("|-\n  a\n  b\n", Write(Literal("a\nb")));
	}

	[TestMethod]
	public void Quoting()
	{
		Assert.AreEqual("a: 'it''s'\n", Write(Map(("a", Single("it's")))));
		Assert.AreEqual("a: \"say \\\"hi\\\" \\\\ bye\"\n", Write(Map(("a", Double("say \"hi\" \\ bye")))));

		// the requested style is used only when it represents the value exactly
		Assert.AreEqual("a: \"key: value\"\n", Write(Map(("a", Plain("key: value")))));
		Assert.AreEqual("a: \"line\\nbreak\"\n", Write(Map(("a", Single("line\nbreak")))));
		Assert.AreEqual("\"multi\\nline\": 1\n", Write(Map((Literal("multi\nline"), Plain("1")))));
	}

	[TestMethod]
	public void Escapes()
	{
		var value = new string([(char)0, (char)7, (char)8, '\t', '\n', (char)0xB, (char)0xC, '\r', (char)0x1B, (char)0x85, (char)0x2028, (char)0x2029, (char)1, (char)0x7F, (char)0x9F, (char)0xFEFF, (char)0xD800, 'x']);
		Assert.AreEqual("\"\\0\\a\\b\\t\\n\\v\\f\\r\\e\\N\\L\\P\\x01\\x7F\\x9F\\uFEFF\\uD800x\"\n", Write(Double(value)));
		Assert.AreEqual(value, ((ScalarNode)Root(Write(Double(value)))).Value);

		// surrogate pairs are written as they are
		Assert.AreEqual("\"\U0001F600\"\n", Write(Double("\U0001F600")));
	}

	[TestMethod]
	public void LiteralChomping()
	{
		Assert.AreEqual("a: |-\n  x\n  y\n", Write(Map(("a", Literal("x\ny")))));
		Assert.AreEqual("a: |\n  x\n  y\nb: 1\n", Write(Map(("a", Literal("x\ny\n")), ("b", Plain("1")))));
		Assert.AreEqual("a: |+\n  x\n\n\nb: 1\n", Write(Map(("a", Literal("x\n\n\n")), ("b", Plain("1")))));
		Assert.AreEqual("- |-\n  x\n\n  y\n", Write(Seq(Literal("x\n\ny"))));
		Assert.AreEqual("- a: |-\n    x\n", Write(Seq(Map(("a", Literal("x"))))));
	}

	[TestMethod]
	public void FoldedScalars()
	{
		Assert.AreEqual("a: >-\n  x y\n\n  z\n", Write(Map(("a", Folded("x y\nz")))));

		// more indented lines are not folded, so the value is written as a literal
		Assert.AreEqual("a: |-\n  x\n    y\n", Write(Map(("a", Folded("x\n  y")))));

		// block scalars cannot start with a space, nor contain lines of only white space
		Assert.AreEqual("a: \" x\\ny\"\n", Write(Map(("a", Literal(" x\ny")))));
		Assert.AreEqual("a: \"x\\n  \\ny\"\n", Write(Map(("a", Literal("x\n  \ny")))));
	}

	[TestMethod]
	public void Strings()
	{
		var yaml = Write(static writer =>
		{
			writer.WriteStartSequence();
			writer.WriteString("text");
			writer.WriteString("42");
			writer.WriteString("null");
			writer.WriteString("");
			writer.WriteString("a\nb\n");
			writer.WriteEndSequence();
		});

		Assert.AreEqual("- text\n- \"42\"\n- \"null\"\n- \"\"\n- |\n  a\n  b\n", yaml);
	}

	[TestMethod]
	public void Numbers()
	{
		var yaml = Write(static writer =>
		{
			writer.WriteStartSequence();
			writer.WriteNumber(-42);
			writer.WriteNumber(1.5);
			writer.WriteNumber(0.1f);
			writer.WriteNumber(1.50m);
			writer.WriteNumber(System.Double.NaN);
			writer.WriteNumber(System.Double.PositiveInfinity);
			writer.WriteNumber(System.Single.NegativeInfinity);
			writer.WriteNumber(BigInteger.Pow(10, 100));
			writer.WriteEndSequence();
		});

		Assert.AreEqual($"- -42\n- 1.5\n- 0.1\n- 1.50\n- .nan\n- .inf\n- -.inf\n- 1{new string('0', 100)}\n", yaml);
	}

	[TestMethod]
	public void NumberKeys()
	{
		var yaml = Write(static writer =>
		{
			writer.WriteStartMapping();
			writer.WriteNumber(1);
			writer.WriteString("one");
			writer.WriteEndMapping();
		});

		Assert.AreEqual("1: one\n", yaml);
	}

	[TestMethod]
	public void Comments()
	{
		var yaml = Write(static writer =>
		{
			writer.WriteComment("header");
			writer.WriteStartMapping();
			writer.WriteKey("a");
			writer.WriteStartMapping();
			writer.WriteComment("first\nsecond");
			writer.WriteKey("b");
			writer.WriteNumber(1);
			writer.WriteEndMapping();
			writer.WriteComment("");
			writer.WriteKey("c");
			writer.WriteStartSequence();
			writer.WriteComment("empty");
			writer.WriteEndSequence();
			writer.WriteKey("d");
			writer.WriteStartSequence();
			writer.WriteStartMapping();
			writer.WriteComment("in item");
			writer.WriteKey("e");
			writer.WriteNumber(2);
			writer.WriteEndMapping();
			writer.WriteEndSequence();
			writer.WriteEndMapping();
			writer.WriteComment("footer");
		});

		Assert.AreEqual("# header\na:\n  # first\n  # second\n  b: 1\n#\nc:\n  # empty\n  []\nd:\n  -\n    # in item\n    e: 2\n# footer\n", yaml);
		Assert.AreEqual(Map(("a", Map(("b", Plain("1")))), ("c", Seq()), ("d", Seq(Map(("e", Plain("2")))))), Root(yaml));
	}

	[TestMethod]
	public void Documents()
	{
		var yaml = Write(static writer =>
		{
			writer.WriteStartMapping();
			writer.WriteKey("a");
			writer.WriteNumber(1);
			writer.WriteEndMapping();
			writer.WriteDocumentStart();
			writer.WriteString("b");
			writer.WriteDocumentEnd();
			writer.WriteDocumentStart();
			writer.WriteScalar("c\n", ScalarStyle.Literal);
		});

		Assert.AreEqual("a: 1\n---\nb\n...\n---\n|\n  c\n", yaml);
		Assert.HasCount(3, Parser.ParseStream(yaml));
	}

	[TestMethod]
	public void NewLine() => Assert.AreEqual("a:\r\n  b: |-\r\n    x\r\n    y\r\n", Write(Map(("a", Map(("b", Literal("x\ny"))))), new(NewLine: "\r\n")));

	[TestMethod]
	public void StructWriter()
	{
		var builder = new StringBuilder();
		var writer = YamlWriter.Create(new StringBuilderBufferWriter(builder));
		writer.WriteNode(Map(("a", Seq(Plain("1")))));
		Assert.AreEqual("a:\n  - 1\n", builder.ToString());
	}

	[TestMethod]
	public void Depth()
	{
		var writer = YamlWriter.Create(new ArrayBufferWriter<char>());
		writer.WriteStartSequence();
		writer.WriteStartMapping();
		Assert.AreEqual(2, writer.CurrentDepth);
	}

	[TestMethod]
	public void InvalidOperations()
	{
		Assert.ThrowsExactly<InvalidOperationException>(static () => Run(static w => { w.WriteString("a"); w.WriteString("b"); }));
		Assert.ThrowsExactly<InvalidOperationException>(static () => Run(static w => w.WriteEndMapping()));
		Assert.ThrowsExactly<InvalidOperationException>(static () => Run(static w => { w.WriteStartSequence(); w.WriteEndMapping(); }));
		Assert.ThrowsExactly<InvalidOperationException>(static () => Run(static w => { w.WriteStartMapping(); w.WriteKey("a"); w.WriteEndMapping(); }));
		Assert.ThrowsExactly<InvalidOperationException>(static () => Run(static w => { w.WriteStartMapping(); w.WriteKey("a"); w.WriteComment("between"); }));
		Assert.ThrowsExactly<InvalidOperationException>(static () => Run(static w => { w.WriteStartSequence(); w.WriteKey("a"); }));
		Assert.ThrowsExactly<InvalidOperationException>(static () => Run(static w => { w.WriteStartSequence(); w.WriteDocumentStart(); }));
		Assert.ThrowsExactly<NotSupportedException>(static () => Run(static w => { w.WriteStartMapping(); w.WriteStartSequence(); }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () => YamlWriter.Create(new ArrayBufferWriter<char>(), new(IndentSize: 0)));
		Assert.ThrowsExactly<ArgumentException>(static () => YamlWriter.Create(new ArrayBufferWriter<char>(), new(NewLine: "\r")));
	}

	private static void Run(Action<YamlWriter<ArrayBufferWriter<char>>> write) => write(YamlWriter.Create(new ArrayBufferWriter<char>()));

	// every value is written in every style, at every kind of position, and is read back as the same text
	[TestMethod]
	public void RoundTrip()
	{
		string[] values =
		[
			"", "text", "two words", "42", "-1.5", "null", "~", "true", "0x2A", ".inf",
			" leading", "trailing ", "\ttab", "tab\t", "key: value", "a:b", "ends:", "text #comment", "a#b", "#hash",
			"- item", "-", "-x", "?", "? x", ":", ":x", "[a]", "{a}", "a, b", "*alias", "&anchor", "!tag", "|", ">", "'", "\"", "%", "@", "`",
			"---", "...", "--- x", "it's", "\"quoted\"", "back\\slash",
			"line\nbreak", "trailing\n", "two\n\n", "\nleading", "\n", "\n\n", "a\n\nb", "a\n \nb", " a\nb", "a\n  b", "a\n\tb",
			"carriage\rreturn", "crlf\r\n", "\0", ((char)0x85).ToString(), ((char)0x2028).ToString(), ((char)0xFEFF).ToString(), ((char)0xD800).ToString(), "\U0001F600", "caf\u00E9",
		];
		ScalarStyle[] styles = [ScalarStyle.Plain, ScalarStyle.SingleQuoted, ScalarStyle.DoubleQuoted, ScalarStyle.Literal, ScalarStyle.Folded];

		foreach (var value in values)
		{
			foreach (var style in styles)
			{
				var scalar = new ScalarNode(Value: value, Style: style);
				Node[] nodes =
				[
					scalar,
					Map(("key", scalar), ("next", Plain("x"))),
					Map((scalar, Plain("x"))),
					Seq(scalar, Plain("x")),
					Seq(Map(("key", scalar)), Seq(scalar)),
					Map(("key", Seq(scalar, Map(("nested", scalar))))),
				];

				foreach (var node in nodes)
				{
					var yaml = Write(node);
					var (parsed, error) = TryParse(yaml);
					Assert.IsNotNull(parsed, $"The value '{Escape(value)}' in style {style} was written as invalid yaml '{Escape(yaml)}': {error}");
					Assert.AreEqual(Values(node), Values(parsed), $"The value '{Escape(value)}' in style {style} was written as '{Escape(yaml)}'.");
				}
			}
		}
	}

	private static (Node? Node, string? Error) TryParse(string yaml)
	{
		try
		{
			return (Root(yaml), null);
		}
		catch (YamlException exception)
		{
			return (null, exception.Message);
		}
	}

	// the styles may differ, as they are replaced when they cannot represent a value
	private static string Values(Node node) => node switch
	{
		ScalarNode scalar => $"<{Escape(scalar.Value)}>",
		SequenceNode sequence => $"[{String.Join(", ", sequence.Items.Select(Values))}]",
		MappingNode mapping => $"{{{String.Join(", ", mapping.Entries.Select(static e => $"{Values(e.Key)}: {Values(e.Value)}"))}}}",
		_ => node.ToString(),
	};

	private static string Escape(string value) => String.Concat(value.Select(static c => c is >= ' ' and <= '~' ? c.ToString() : $"\\x{(int)c:X}"));
}
