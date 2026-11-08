using PeterJuhasz.Text.Yaml.Parsing;

namespace PeterJuhasz.Text.Yaml.Tests;

[TestClass]
public class ErrorTests
{
	[TestMethod]
	[DataRow("a: b: c")]
	[DataRow("a: 1\n  b: 2")]
	[DataRow("a: 1\n- b")]
	[DataRow("- a\nb: 1")]
	[DataRow("a: 1\na: 2")]
	[DataRow("{a: 1, a: 2}")]
	[DataRow("'abc")]
	[DataRow("\"abc")]
	[DataRow("\"\\q\"")]
	[DataRow("\"\\x4\"")]
	[DataRow("\"\\UFFFFFFFF\"")]
	[DataRow("[a, b")]
	[DataRow("{a: 1")]
	[DataRow("[a, b] c")]
	[DataRow("[a}")]
	[DataRow("[,]")]
	[DataRow("\"a\" b")]
	[DataRow("\"a\"# comment")]
	[DataRow("key: |x\n  a")]
	[DataRow("\ta: 1")]
	[DataRow("&anchor a")]
	[DataRow("*alias")]
	[DataRow("!!str a")]
	[DataRow("? a\n: b")]
	[DataRow("@a")]
	[DataRow("a\0")]
	public void Invalid(string yaml)
	{
		Assert.ThrowsExactly<YamlException>(() => Parser.ParseStream(yaml));
	}

	[TestMethod]
	public void Location()
	{
		var exception = Assert.ThrowsExactly<YamlException>(static () => Parser.Parse("a: 1\nb: 2\na: 3"));

		Assert.AreEqual(3, exception.Line);
		Assert.AreEqual(1, exception.Column);
	}

	[TestMethod]
	public void LocationWithCarriageReturnLineFeed()
	{
		var exception = Assert.ThrowsExactly<YamlException>(static () => Parser.Parse("a: 1\r\nb: 'x"));

		Assert.AreEqual(2, exception.Line);
		Assert.AreEqual(4, exception.Column);
	}

	[TestMethod]
	public void Null()
	{
		Assert.ThrowsExactly<ArgumentNullException>(static () => Parser.Parse(null!));
	}
}
