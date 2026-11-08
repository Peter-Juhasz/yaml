using static PeterJuhasz.Text.Yaml.Tests.Yaml;

namespace PeterJuhasz.Text.Yaml.Tests;

[TestClass]
public class BlockScalarTests
{
	[TestMethod]
	[DataRow("key: |\n  a\n  b\n", "a\nb\n")]
	[DataRow("key: |\n  a\n  b", "a\nb\n")]
	[DataRow("key: |\n  a\n    b\n  c\n", "a\n  b\nc\n")]
	[DataRow("key: |\n  a\n\n  b\n", "a\n\nb\n")]
	[DataRow("key: |\n\n  a\n", "\na\n")]
	[DataRow("key: |\n  # not a comment\n", "# not a comment\n")]
	[DataRow("key: | # comment\n  a\n", "a\n")]
	[DataRow("key: |\r\n  a\r\n  b\r\n", "a\nb\n")]
	[DataRow("key: |\n", "")]
	public void Literal(string yaml, string expected)
	{
		Assert.AreEqual(Map(("key", Yaml.Literal(expected))), Root(yaml));
	}

	[TestMethod]
	[DataRow("key: |-\n  a\n  b\n\n", "a\nb")]
	[DataRow("key: |\n  a\n  b\n\n", "a\nb\n")]
	[DataRow("key: |+\n  a\n  b\n\n", "a\nb\n\n")]
	[DataRow("key: |+\n  a\n", "a\n")]
	[DataRow("key: |2-\n    a\n", "  a")]
	[DataRow("key: |-2\n    a\n", "  a")]
	public void Chomping(string yaml, string expected)
	{
		Assert.AreEqual(Map(("key", Yaml.Literal(expected))), Root(yaml));
	}

	[TestMethod]
	[DataRow("key: >\n  a\n  b\n", "a b\n")]
	[DataRow("key: >\n  a\n\n  b\n", "a\nb\n")]
	[DataRow("key: >\n  a\n\n\n  b\n", "a\n\nb\n")]
	[DataRow("key: >\n  a\n    b\n  c\n", "a\n  b\nc\n")]
	[DataRow("key: >\n  a\n\n    b\n", "a\n\n  b\n")]
	[DataRow("key: >-\n  a\n  b\n", "a b")]
	[DataRow("key: >\n\n  a\n", "\na\n")]
	public void Folded(string yaml, string expected)
	{
		Assert.AreEqual(Map(("key", Yaml.Folded(expected))), Root(yaml));
	}

	[TestMethod]
	public void FollowedByOtherEntries()
	{
		Assert.AreEqual(
			Map(("a", Yaml.Literal("x\ny\n")), ("b", Yaml.Plain("c"))),
			Root("a: |\n  x\n  y\n\nb: c")
		);
	}

	[TestMethod]
	public void InSequence()
	{
		Assert.AreEqual(
			Seq(Yaml.Literal("a\n"), Yaml.Folded("b c\n")),
			Root("- |\n  a\n- >\n  b\n  c\n")
		);
	}

	[TestMethod]
	public void TopLevel()
	{
		Assert.AreEqual(Yaml.Literal("a\nb\n"), Root("--- |\na\nb\n"));
	}
}
