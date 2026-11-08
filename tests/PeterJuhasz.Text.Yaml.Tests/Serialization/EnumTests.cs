using PeterJuhasz.Text.Yaml.Serialization;

namespace PeterJuhasz.Text.Yaml.Tests.Serialization;

[TestClass]
public class EnumTests
{
	public enum Level
	{
		Debug,
		Information = 2,
		WarningOnly = 3,
		Negative = -1,
	}

	[Flags]
	public enum Permissions : byte
	{
		None = 0,
		Read = 1,
		Write = 2,
		Execute = 4,
	}

	[TestMethod]
	[DataRow("Information", Level.Information)]
	[DataRow("information", Level.Information)]
	[DataRow("'WARNINGONLY'", Level.WarningOnly)]
	[DataRow("2", Level.Information)]
	[DataRow("0x3", Level.WarningOnly)]
	[DataRow("-1", Level.Negative)]
	[DataRow("Negative", Level.Negative)]
	[DataRow("42", (Level)42)]
	public void Values(string yaml, Level expected)
	{
		Assert.AreEqual(expected, YamlSerializer.Deserialize<Level>(yaml));
	}

	[TestMethod]
	[DataRow("Unknown")]
	[DataRow("warning_only")]
	[DataRow("'2'")]
	[DataRow("Debug, Information")]
	[DataRow("1.5")]
	public void Invalid(string yaml)
	{
		Assert.ThrowsExactly<YamlSerializationException>(() => YamlSerializer.Deserialize<Level>(yaml));
	}

	[TestMethod]
	public void NamingPolicy()
	{
		var options = new YamlSerializerOptions { EnumNamingPolicy = YamlNamingPolicy.SnakeCaseLower };
		Assert.AreEqual(Level.WarningOnly, YamlSerializer.Deserialize<Level>("warning_only", options));
		Assert.AreEqual(Level.WarningOnly, YamlSerializer.Deserialize<Level>("WarningOnly", options));
		Assert.AreEqual(Level.WarningOnly, YamlSerializer.Deserialize<Level>("WARNING_ONLY", options));
	}

	[TestMethod]
	[DataRow("Read", Permissions.Read)]
	[DataRow("read, write", Permissions.Read | Permissions.Write)]
	[DataRow("Read,Write,Execute", Permissions.Read | Permissions.Write | Permissions.Execute)]
	[DataRow("6", Permissions.Write | Permissions.Execute)]
	[DataRow("None", Permissions.None)]
	public void Flags(string yaml, Permissions expected)
	{
		Assert.AreEqual(expected, YamlSerializer.Deserialize<Permissions>(yaml));
	}

	[TestMethod]
	public void FlagsInvalid()
	{
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Permissions>("Read, Delete"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Permissions>("Read,"));
	}

	[TestMethod]
	public void Nullable()
	{
		Assert.IsNull(YamlSerializer.Deserialize<Level?>("~"));
		Assert.AreEqual(Level.Debug, YamlSerializer.Deserialize<Level?>("debug"));
	}
}
