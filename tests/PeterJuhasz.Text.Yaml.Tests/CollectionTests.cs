using PeterJuhasz.Text.Yaml.Model;
using static PeterJuhasz.Text.Yaml.Tests.Yaml;

namespace PeterJuhasz.Text.Yaml.Tests;

[TestClass]
public class CollectionTests
{
	[TestMethod]
	public void Mapping()
	{
		Assert.AreEqual(Map(("a", Plain("1")), ("b", Plain("2"))), Root("a: 1\nb: 2"));
	}

	[TestMethod]
	public void MappingWithEmptyValues()
	{
		Assert.AreEqual(Map(("a", Empty), ("b", Empty)), Root("a:\nb:"));
	}

	[TestMethod]
	public void MappingWithQuotedKeys()
	{
		Assert.AreEqual(Map((Double("a b"), Plain("1")), (Single("c: d"), Plain("2"))), Root("\"a b\": 1\n'c: d' : 2"));
	}

	[TestMethod]
	public void MappingWithSpacesInKey()
	{
		Assert.AreEqual(Map(("a b", Plain("c d"))), Root("a b: c d"));
	}

	[TestMethod]
	public void MappingWithQuotedValues()
	{
		Assert.AreEqual(Map(("a", Double("x")), ("b", Single("y"))), Root("a: \"x\"\nb: 'y'"));
	}

	[TestMethod]
	public void MappingWithComments()
	{
		Assert.AreEqual(Map(("a", Plain("1")), ("b", Plain("2"))), Root("# header\na: 1 # one\n\n# between\nb: 2\n# footer"));
	}

	[TestMethod]
	public void NestedMapping()
	{
		Assert.AreEqual(
			Map(("a", Map(("b", Plain("1")), ("c", Map(("d", Plain("2")))))), ("e", Plain("3"))),
			Root("a:\n  b: 1\n  c:\n    d: 2\ne: 3")
		);
	}

	[TestMethod]
	public void Sequence()
	{
		Assert.AreEqual(Seq(Plain("a"), Plain("b"), Plain("c")), Root("- a\n- b\n- c"));
	}

	[TestMethod]
	public void SequenceWithEmptyItems()
	{
		Assert.AreEqual(Seq(Empty, Plain("b"), Empty), Root("-\n- b\n-"));
	}

	[TestMethod]
	public void NestedSequence()
	{
		Assert.AreEqual(Seq(Seq(Plain("a"), Plain("b")), Seq(Plain("c"))), Root("- - a\n  - b\n-\n  - c"));
	}

	[TestMethod]
	public void SequenceInMapping()
	{
		Assert.AreEqual(Map(("a", Seq(Plain("1"), Plain("2"))), ("b", Plain("3"))), Root("a:\n  - 1\n  - 2\nb: 3"));
	}

	[TestMethod]
	public void SequenceInMappingOnSameIndentation()
	{
		Assert.AreEqual(Map(("a", Seq(Plain("1"), Plain("2"))), ("b", Plain("3"))), Root("a:\n- 1\n- 2\nb: 3"));
	}

	[TestMethod]
	public void MappingInSequence()
	{
		Assert.AreEqual(
			Seq(Map(("a", Plain("1")), ("b", Plain("2"))), Map(("c", Plain("3")))),
			Root("- a: 1\n  b: 2\n- c: 3")
		);
	}

	[TestMethod]
	public void MappingInSequenceOnNextLine()
	{
		Assert.AreEqual(Seq(Map(("a", Plain("1")), ("b", Plain("2")))), Root("-\n  a: 1\n  b: 2"));
	}

	[TestMethod]
	public void Complex()
	{
		var yaml = """
			name: test
			on:
			  push:
			    branches:
			      - main
			jobs:
			  build:
			    runs-on: ubuntu-latest
			    steps:
			    - uses: actions/checkout@v4
			    - name: Build
			      run: dotnet build
			    - run: |
			        dotnet test
			        dotnet pack
			""";

		Assert.AreEqual(
			Map(
				("name", Plain("test")),
				("on", Map(("push", Map(("branches", Seq(Plain("main"))))))),
				("jobs", Map(("build", Map(
					("runs-on", Plain("ubuntu-latest")),
					("steps", Seq(
						Map(("uses", Plain("actions/checkout@v4"))),
						Map(("name", Plain("Build")), ("run", Plain("dotnet build"))),
						Map(("run", Literal("dotnet test\ndotnet pack\n")))
					))
				))))
			),
			Root(yaml)
		);
	}

	[TestMethod]
	public void Indexer()
	{
		var mapping = (MappingNode)Root("a: 1\nb: 2");
		Assert.AreEqual(Plain("2"), mapping["b"]);
		Assert.ThrowsExactly<KeyNotFoundException>(() => mapping["c"]);
	}

	[TestMethod]
	public void TryGetValue()
	{
		var mapping = (MappingNode)Root("a: 1");
		Assert.IsTrue(mapping.TryGetValue("a", out var value));
		Assert.AreEqual(Plain("1"), value);
		Assert.IsFalse(mapping.TryGetValue("b", out _));
	}
}
