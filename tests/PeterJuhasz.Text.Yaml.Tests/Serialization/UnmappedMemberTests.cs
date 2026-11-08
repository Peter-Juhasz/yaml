using PeterJuhasz.Text.Yaml.Serialization;

namespace PeterJuhasz.Text.Yaml.Tests.Serialization;

[TestClass]
public class UnmappedMemberTests
{
	private static readonly YamlSerializerOptions Disallow = new() { UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow };

	public sealed class Server
	{
		public string? Host { get; set; }

		public int Port { get; set; }

		[YamlIgnore]
		public string? Ignored { get; set; }

		public string ReadOnly { get; } = "initial";
	}

	public sealed record class Endpoint(string Host, int Port = 80)
	{
		public string? Path { get; init; }
	}

	public sealed class Root
	{
		public List<Server> Servers { get; set; } = [];

		public Dictionary<string, string>? Labels { get; set; }
	}

	[TestMethod]
	public void SkippedByDefault()
	{
		Assert.AreEqual("a", YamlSerializer.Deserialize<Server>("Host: a\nUnknown: x")?.Host);
		Assert.AreEqual(YamlUnmappedMemberHandling.Skip, YamlSerializerOptions.Default.UnmappedMemberHandling);
	}

	[TestMethod]
	public void MappedKeysAllowed()
	{
		var server = YamlSerializer.Deserialize<Server>("Host: a\nPort: 1", Disallow);

		Assert.IsNotNull(server);
		Assert.AreEqual("a", server.Host);
		Assert.AreEqual(1, server.Port);
	}

	[TestMethod]
	public void UnknownKey()
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Server>("Host: a\nUnknown: x", Disallow));

		Assert.AreEqual($"The key 'Unknown' does not map to any member of type '{typeof(Server)}'.", exception.Reason);
		Assert.AreEqual("$.Unknown", exception.Path);
	}

	[TestMethod]
	[DataRow("Ignored: x")]
	[DataRow("ReadOnly: x")]
	[DataRow("host: a")]
	public void KeysOfMembersWhichCannotBeSet(string yaml)
	{
		Assert.ThrowsExactly<YamlSerializationException>(() => YamlSerializer.Deserialize<Server>(yaml, Disallow));
	}

	[TestMethod]
	public void CaseInsensitive()
	{
		var options = new YamlSerializerOptions { UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = true };

		Assert.AreEqual("a", YamlSerializer.Deserialize<Server>("host: a", options)?.Host);
	}

	[TestMethod]
	public void NamingPolicy()
	{
		var options = new YamlSerializerOptions { UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow, PropertyNamingPolicy = YamlNamingPolicy.CamelCase };

		Assert.AreEqual(1, YamlSerializer.Deserialize<Server>("port: 1", options)?.Port);
		Assert.ThrowsExactly<YamlSerializationException>(() => YamlSerializer.Deserialize<Server>("Port: 1", options));
	}

	[TestMethod]
	public void ConstructorParameters()
	{
		Assert.AreEqual(new Endpoint(Host: "a", Port: 1) { Path = "/" }, YamlSerializer.Deserialize<Endpoint>("Host: a\nPort: 1\nPath: /", Disallow));

		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Endpoint>("Host: a\nScheme: https", Disallow));
		Assert.AreEqual("$.Scheme", exception.Path);
	}

	[TestMethod]
	public void Nested()
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Root>("Servers:\n  - Host: a\n  - Hostname: b", Disallow));

		Assert.AreEqual("$.Servers[1].Hostname", exception.Path);
	}

	[TestMethod]
	public void DictionaryKeysNotAffected()
	{
		Assert.AreEqual("b", YamlSerializer.Deserialize<Root>("Labels: {a: b}", Disallow)?.Labels?["a"]);
	}

	[TestMethod]
	public void ComplexKey()
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Server>("{[a]: b}", Disallow));

		Assert.AreEqual("$", exception.Path);
	}
}
