using PeterJuhasz.Text.Yaml.Serialization;

namespace PeterJuhasz.Text.Yaml.Tests.Serialization;

[TestClass]
public class NamingPolicyTests
{
	[TestMethod]
	[DataRow("Name", "name")]
	[DataRow("FirstName", "firstName")]
	[DataRow("URLValue", "urlValue")]
	[DataRow("ID", "id")]
	[DataRow("IOStream", "ioStream")]
	[DataRow("name", "name")]
	[DataRow("", "")]
	public void CamelCase(string name, string expected)
	{
		Assert.AreEqual(expected, YamlNamingPolicy.CamelCase.ConvertName(name));
	}

	[TestMethod]
	[DataRow("Name", "name")]
	[DataRow("FirstName", "first_name")]
	[DataRow("HTMLParser", "html_parser")]
	[DataRow("ID", "id")]
	[DataRow("Version2", "version2")]
	[DataRow("Abc1Def", "abc1_def")]
	[DataRow("already_snake", "already_snake")]
	[DataRow("camelCase", "camel_case")]
	[DataRow("", "")]
	public void SnakeCaseLower(string name, string expected)
	{
		Assert.AreEqual(expected, YamlNamingPolicy.SnakeCaseLower.ConvertName(name));
	}

	[TestMethod]
	public void OtherSeparatorPolicies()
	{
		Assert.AreEqual("FIRST_NAME", YamlNamingPolicy.SnakeCaseUpper.ConvertName("FirstName"));
		Assert.AreEqual("first-name", YamlNamingPolicy.KebabCaseLower.ConvertName("FirstName"));
		Assert.AreEqual("FIRST-NAME", YamlNamingPolicy.KebabCaseUpper.ConvertName("FirstName"));
		Assert.AreEqual("html-parser", YamlNamingPolicy.KebabCaseLower.ConvertName("HTMLParser"));
	}
}
