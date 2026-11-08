using PeterJuhasz.Text.Yaml.Model;
using static PeterJuhasz.Text.Yaml.Tests.Yaml;

namespace PeterJuhasz.Text.Yaml.Tests.Parsing;

[TestClass]
public class FlowTests
{
	[TestMethod]
	public void EmptySequence()
	{
		Assert.AreEqual(Seq(), Root("[]"));
	}

	[TestMethod]
	public void EmptyMapping()
	{
		Assert.AreEqual(new MappingNode(Entries: []), Root("{}"));
	}

	[TestMethod]
	[DataRow("[a, b, c]")]
	[DataRow("[a,b,c]")]
	[DataRow("[ a , b , c , ]")]
	[DataRow("[\n  a,\n  b, # comment\n  c\n]")]
	public void Sequence(string yaml)
	{
		Assert.AreEqual(Seq(Plain("a"), Plain("b"), Plain("c")), Root(yaml));
	}

	[TestMethod]
	public void SequenceWithMixedScalars()
	{
		Assert.AreEqual(Seq(Plain("a b"), Double("c, d"), Single("e]")), Root("[a b, \"c, d\", 'e]']"));
	}

	[TestMethod]
	public void SequenceWithMultilinePlainScalar()
	{
		Assert.AreEqual(Seq(Plain("a b")), Root("[a\n b]"));
	}

	[TestMethod]
	[DataRow("[a  b\n c d\n\n e]", "a  b c d\ne")]
	[DataRow("[a\n\n\n b\n c]", "a\n\nb c")]
	[DataRow("[a b \n c ]", "a b c")]
	public void SequenceWithFoldedPlainScalar(string yaml, string expected)
	{
		Assert.AreEqual(Seq(Plain(expected)), Root(yaml));
	}

	[TestMethod]
	[DataRow("{a: 1, b: 2}")]
	[DataRow("{ a: 1, b: 2, }")]
	[DataRow("{\n  a: 1,\n  b: 2\n}")]
	public void Mapping(string yaml)
	{
		Assert.AreEqual(Map(("a", Plain("1")), ("b", Plain("2"))), Root(yaml));
	}

	[TestMethod]
	public void MappingJsonStyle()
	{
		Assert.AreEqual(Map((Double("a"), Plain("1")), (Double("b"), Double("x"))), Root("{\"a\":1, \"b\":\"x\"}"));
	}

	[TestMethod]
	public void MappingWithEmptyValues()
	{
		Assert.AreEqual(Map(("a", Empty), ("b", Empty)), Root("{a, b:}"));
	}

	[TestMethod]
	public void PlainScalarWithColon()
	{
		Assert.AreEqual(Seq(Plain("a:b"), Plain("http://x")), Root("[a:b, http://x]"));
	}

	[TestMethod]
	public void SinglePairMapping()
	{
		Assert.AreEqual(Seq(Map(("a", Plain("1"))), Plain("b")), Root("[a: 1, b]"));
	}

	[TestMethod]
	public void Nested()
	{
		Assert.AreEqual(
			Map(("a", Seq(Plain("1"), Map(("b", Seq())))), ("c", Seq(Seq()))),
			Root("{a: [1, {b: []}], c: [[]]}")
		);
	}

	[TestMethod]
	public void InBlockMapping()
	{
		Assert.AreEqual(
			Map(("a", Seq(Plain("1"), Plain("2"))), ("b", Map(("c", Plain("d"))))),
			Root("a: [1, 2] # comment\nb: {c: d}")
		);
	}

	[TestMethod]
	public void InBlockSequence()
	{
		Assert.AreEqual(Seq(Seq(Plain("a")), Map(("b", Plain("c")))), Root("- [a]\n- {b: c}"));
	}
}
