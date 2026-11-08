using PeterJuhasz.Text.Yaml.Model;
using static PeterJuhasz.Text.Yaml.Tests.Yaml;

namespace PeterJuhasz.Text.Yaml.Tests.Model;

[TestClass]
public class ModelTests
{
	[TestMethod]
	public void ScalarEquality()
	{
		Assert.AreEqual(Plain("a"), Plain("a"));
		Assert.AreNotEqual(Plain("a"), Plain("b"));
		Assert.AreNotEqual<Node>(Plain("a"), Double("a"));
	}

	[TestMethod]
	public void SequenceEquality()
	{
		Assert.AreEqual(Seq(Plain("a"), Seq(Plain("b"))), Seq(Plain("a"), Seq(Plain("b"))));
		Assert.AreEqual(Seq(Plain("a"), Seq(Plain("b"))).GetHashCode(), Seq(Plain("a"), Seq(Plain("b"))).GetHashCode());
		Assert.AreNotEqual(Seq(Plain("a")), Seq(Plain("a"), Plain("b")));
		Assert.AreEqual(new SequenceNode(Items: default), Seq());
	}

	[TestMethod]
	public void MappingEquality()
	{
		Assert.AreEqual(Map(("a", Seq(Plain("b")))), Map(("a", Seq(Plain("b")))));
		Assert.AreEqual(Map(("a", Seq(Plain("b")))).GetHashCode(), Map(("a", Seq(Plain("b")))).GetHashCode());
		Assert.AreNotEqual(Map(("a", Plain("b"))), Map(("a", Plain("c"))));
		Assert.AreNotEqual<Node>(Map(("a", Plain("b"))), Seq(Plain("a"), Plain("b")));
	}
}
