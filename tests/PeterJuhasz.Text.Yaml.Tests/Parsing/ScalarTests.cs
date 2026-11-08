using PeterJuhasz.Text.Yaml.Model;
using static PeterJuhasz.Text.Yaml.Tests.Yaml;

namespace PeterJuhasz.Text.Yaml.Tests.Parsing;

[TestClass]
public class ScalarTests
{
	[TestMethod]
	[DataRow("hello", "hello")]
	[DataRow("hello world", "hello world")]
	[DataRow("  hello  ", "hello")]
	[DataRow("123", "123")]
	[DataRow("-1", "-1")]
	[DataRow("3.14", "3.14")]
	[DataRow("true", "true")]
	[DataRow("http://example.org/a#b", "http://example.org/a#b")]
	[DataRow("a:b", "a:b")]
	[DataRow("-a", "-a")]
	[DataRow("hello # comment", "hello")]
	[DataRow("# comment\nhello", "hello")]
	[DataRow("hello\n", "hello")]
	[DataRow("\n\nhello\n\n", "hello")]
	public void Plain(string yaml, string expected)
	{
		Assert.AreEqual(Yaml.Plain(expected), Root(yaml));
	}

	[TestMethod]
	[DataRow("a\nb", "a b")]
	[DataRow("a\n  b\n c", "a b c")]
	[DataRow("a\n\nb", "a\nb")]
	[DataRow("a\n\n\nb", "a\n\nb")]
	[DataRow("a\r\nb", "a b")]
	[DataRow("a\nb # comment", "a b")]
	public void PlainMultiline(string yaml, string expected)
	{
		Assert.AreEqual(Yaml.Plain(expected), Root(yaml));
	}

	[TestMethod]
	public void PlainMultilineAsMappingValue()
	{
		Assert.AreEqual(Map(("key", Yaml.Plain("a b c"))), Root("key: a\n  b\n  c"));
	}

	[TestMethod]
	public void PlainEndsAtComment()
	{
		Assert.AreEqual(Map(("key", Yaml.Plain("a")), ("other", Yaml.Plain("b"))), Root("key: a # comment\n  # comment\nother: b"));
	}

	[TestMethod]
	[DataRow("'hello'", "hello")]
	[DataRow("'it''s'", "it's")]
	[DataRow("''", "")]
	[DataRow("'a # b'", "a # b")]
	[DataRow("'a: b'", "a: b")]
	[DataRow("'\\n'", "\\n")]
	[DataRow("'a\n  b'", "a b")]
	[DataRow("'a  \n\n  b'", "a\nb")]
	[DataRow("'a' # comment", "a")]
	[DataRow("'abc''def''ghi'", "abc'def'ghi")]
	[DataRow("'abc\ndef''ghi'", "abc def'ghi")]
	[DataRow("'\"\\'", "\"\\")]
	public void SingleQuoted(string yaml, string expected)
	{
		Assert.AreEqual(Single(expected), Root(yaml));
	}

	[TestMethod]
	[DataRow("\"hello\"", "hello")]
	[DataRow("\"\"", "")]
	[DataRow("\"a\\nb\"", "a\nb")]
	[DataRow("\"a\\tb\"", "a\tb")]
	[DataRow("\"a\\\"b\"", "a\"b")]
	[DataRow("\"a\\\\b\"", "a\\b")]
	[DataRow("\"a\\/b\"", "a/b")]
	[DataRow("\"\\x41\"", "A")]
	[DataRow("\"\\u00e9\"", "\u00e9")]
	[DataRow("\"\\U0001F600\"", "\U0001F600")]
	[DataRow("\"\\0\\a\\b\\v\\f\\r\\e\"", "\0\a\b\v\f\r\x1B")]
	[DataRow("\"\\N\\_\\L\\P\"", "\u0085\u00A0\u2028\u2029")]
	[DataRow("\"a\\ \"", "a ")]
	[DataRow("\"a\n  b\"", "a b")]
	[DataRow("\"a\n\n  b\"", "a\nb")]
	[DataRow("\"a\\\n  b\"", "ab")]
	[DataRow("\"a\\ \n  b\"", "a  b")]
	[DataRow("\"'\"", "'")]
	[DataRow("\"abc\\ndef\\tghi\"", "abc\ndef\tghi")]
	[DataRow("\"abc\n  def\\\"ghi\"", "abc def\"ghi")]
	[DataRow("\"a ' b\"", "a ' b")]
	public void DoubleQuoted(string yaml, string expected)
	{
		Assert.AreEqual(Double(expected), Root(yaml));
	}

	[TestMethod]
	[DataRow("", true)]
	[DataRow("~", true)]
	[DataRow("null", true)]
	[DataRow("Null", true)]
	[DataRow("NULL", true)]
	[DataRow("nil", false)]
	[DataRow("'null'", false)]
	[DataRow("\"\"", false)]
	public void IsNull(string yaml, bool expected)
	{
		var scalar = Root(yaml) as ScalarNode;
		Assert.IsNotNull(scalar);
		Assert.AreEqual(expected, scalar.IsNull);
	}

	[TestMethod]
	public void Conversions()
	{
		var mapping = (MappingNode)Root("enabled: true\ncount: 3\nratio: 0.25\nname: '42'");

		Assert.IsTrue(((ScalarNode)mapping["enabled"]).TryGetBoolean(out var enabled));
		Assert.IsTrue(enabled);
		Assert.IsTrue(((ScalarNode)mapping["count"]).TryGetInt32(out var count));
		Assert.AreEqual(3, count);
		Assert.IsTrue(((ScalarNode)mapping["ratio"]).TryGetDouble(out var ratio));
		Assert.AreEqual(0.25, ratio);
		Assert.IsFalse(((ScalarNode)mapping["name"]).TryGetInt32(out _));
	}
}
