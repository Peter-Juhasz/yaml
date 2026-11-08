using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Parsing;
using static PeterJuhasz.Text.Yaml.Tests.Yaml;

namespace PeterJuhasz.Text.Yaml.Tests;

[TestClass]
public class DocumentTests
{
	[TestMethod]
	[DataRow("")]
	[DataRow("\n\n")]
	[DataRow("# comment")]
	public void EmptyStream(string yaml)
	{
		Assert.IsEmpty(Parser.ParseStream(yaml));
		Assert.AreEqual(Empty, Root(yaml));
	}

	[TestMethod]
	public void ExplicitEmptyDocument()
	{
		Assert.AreEqual(new DocumentNode(Root: Empty), Parser.Parse("---"));
	}

	[TestMethod]
	[DataRow("---\na: 1")]
	[DataRow("--- # comment\na: 1\n...")]
	[DataRow("%YAML 1.2\n---\na: 1")]
	[DataRow("﻿a: 1")]
	public void SingleDocument(string yaml)
	{
		Assert.AreEqual(new DocumentNode(Root: Map(("a", Plain("1")))), Parser.Parse(yaml));
	}

	[TestMethod]
	public void InlineContent()
	{
		Assert.AreEqual(Plain("text"), Root("--- text"));
	}

	[TestMethod]
	public void MultipleDocuments()
	{
		var documents = Parser.ParseStream("a: 1\n---\n- b\n...\n---\nc\n---\n");

		Assert.HasCount(4, documents);
		Assert.AreEqual(Map(("a", Plain("1"))), documents[0].Root);
		Assert.AreEqual(Seq(Plain("b")), documents[1].Root);
		Assert.AreEqual(Plain("c"), documents[2].Root);
		Assert.AreEqual(Empty, documents[3].Root);
	}

	[TestMethod]
	public void DocumentMarkerEndsBlockScalar()
	{
		var documents = Parser.ParseStream("--- |\na\n---\nb");

		Assert.HasCount(2, documents);
		Assert.AreEqual(Literal("a\n"), documents[0].Root);
		Assert.AreEqual(Plain("b"), documents[1].Root);
	}

	[TestMethod]
	public void ParseRejectsMultipleDocuments()
	{
		Assert.ThrowsExactly<YamlException>(static () => Parser.Parse("a\n---\nb"));
	}
}
