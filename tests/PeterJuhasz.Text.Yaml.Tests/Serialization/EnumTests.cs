using System.Runtime.Serialization;
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

	public enum Status
	{
		[EnumMember(Value = "in-progress")]
		InProgress,

		[EnumMember(Value = "done")]
		Completed,

		// the explicit value of another member wins over this name
		Done,

		[EnumMember]
		Cancelled,
	}

	[Flags]
	public enum Access
	{
		[EnumMember(Value = "r")]
		Read = 1,

		[EnumMember(Value = "w")]
		Write = 2,
	}

	[TestMethod]
	[DataRow("in-progress", Status.InProgress)]
	[DataRow("IN-PROGRESS", Status.InProgress)]
	[DataRow("done", Status.Completed)]
	[DataRow("Cancelled", Status.Cancelled)]
	[DataRow("0", Status.InProgress)]
	public void EnumMember(string yaml, Status expected)
	{
		Assert.AreEqual(expected, YamlSerializer.Deserialize<Status>(yaml));
	}

	[TestMethod]
	[DataRow("InProgress")]
	[DataRow("Completed")]
	public void EnumMemberReplacesName(string yaml)
	{
		Assert.ThrowsExactly<YamlSerializationException>(() => YamlSerializer.Deserialize<Status>(yaml));
	}

	[TestMethod]
	public void EnumMemberNotConvertedByNamingPolicy()
	{
		var options = new YamlSerializerOptions { EnumNamingPolicy = YamlNamingPolicy.SnakeCaseLower };
		Assert.AreEqual(Status.InProgress, YamlSerializer.Deserialize<Status>("in-progress", options));
		Assert.ThrowsExactly<YamlSerializationException>(() => YamlSerializer.Deserialize<Status>("in_progress", options));
	}

	[TestMethod]
	public void EnumMemberFlags()
	{
		Assert.AreEqual(Access.Read | Access.Write, YamlSerializer.Deserialize<Access>("r, w"));
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
