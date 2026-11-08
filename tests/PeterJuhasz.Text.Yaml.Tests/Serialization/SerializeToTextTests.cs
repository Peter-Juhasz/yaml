using System.Buffers;
using PeterJuhasz.Text.Yaml.Serialization;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Tests.Serialization;

[TestClass]
public class SerializeToTextTests
{
	public sealed class Configuration
	{
		public string? Name { get; set; }

		public int Port { get; set; }

		public string? Script { get; set; }

		public List<Server> Servers { get; set; } = [];

		public Dictionary<string, string> Labels { get; set; } = [];

		public Animal? Pet { get; set; }
	}

	public sealed record class Server(string Host, int Port = 80);

	[YamlDerivedType(typeof(Cat), "cat")]
	public abstract class Animal;

	public sealed class Cat : Animal
	{
		public int Lives { get; set; }
	}

	private static readonly Configuration Sample = new()
	{
		Name = "app",
		Port = 8080,
		Script = "echo a\necho b\n",
		Servers = [new(Host: "a.example.com"), new(Host: "b.example.com", Port: 81)],
		Labels = new() { ["env"] = "true" },
		Pet = new Cat { Lives = 9 },
	};

	[TestMethod]
	public void Text()
	{
		var expected = """
			Name: app
			Port: 8080
			Script: |
			  echo a
			  echo b
			Servers:
			  - Host: a.example.com
			    Port: 80
			  - Host: b.example.com
			    Port: 81
			Labels:
			  env: "true"
			Pet:
			  $type: cat
			  Lives: 9

			""".ReplaceLineEndings("\n");
		Assert.AreEqual(expected, YamlSerializer.Serialize(Sample));
	}

	[TestMethod]
	public void RoundTrip()
	{
		var result = YamlSerializer.Deserialize<Configuration>(YamlSerializer.Serialize(Sample), new() { UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow })!;
		Assert.AreEqual(Sample.Name, result.Name);
		Assert.AreEqual(Sample.Script, result.Script);
		Assert.AreSequenceEqual(Sample.Servers, result.Servers);
		Assert.AreEqual("true", result.Labels["env"]);
		Assert.AreEqual(9, ((Cat)result.Pet!).Lives);
	}

	[TestMethod]
	public void MatchesModel() => Assert.AreEqual(YamlSerializer.SerializeToNode(Sample), Yaml.Root(YamlSerializer.Serialize(Sample)));

	[TestMethod]
	public void Formatting()
	{
		var options = new YamlSerializerOptions
		{
			PropertyNamingPolicy = YamlNamingPolicy.SnakeCaseLower,
			FormattingOptions = new(IndentSize: 4, IndentSequences: false, NewLine: "\r\n"),
		};
		var yaml = YamlSerializer.Serialize(new Dictionary<string, object> { ["a"] = new Dictionary<string, int[]> { ["b"] = [1] } }, options);
		Assert.AreEqual("a:\r\n    b:\r\n    - 1\r\n", yaml);
	}

	[TestMethod]
	public void Scalars()
	{
		Assert.AreEqual("42\n", YamlSerializer.Serialize(42));
		Assert.AreEqual("null\n", YamlSerializer.Serialize<string?>(null));
		Assert.AreEqual("\"42\"\n", YamlSerializer.Serialize("42"));
		Assert.AreEqual("[]\n", YamlSerializer.Serialize(Array.Empty<int>()));
	}

	[TestMethod]
	public void IntoWriter()
	{
		var buffer = new ArrayBufferWriter<char>();
		var writer = YamlWriter.Create(buffer);
		writer.WriteComment("generated");
		writer.WriteStartMapping();
		writer.WriteKey("version");
		writer.WriteNumber(1);
		writer.WriteKey("servers");
		YamlSerializer.Serialize(writer, Sample.Servers);
		writer.WriteEndMapping();

		Assert.AreEqual("# generated\nversion: 1\nservers:\n  - Host: a.example.com\n    Port: 80\n  - Host: b.example.com\n    Port: 81\n", buffer.WrittenSpan.ToString());
	}

	[TestMethod]
	public void ErrorPath()
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Serialize(new Dictionary<string, object> { ["key"] = new int[1, 1] }));
		Assert.AreEqual("$.key", exception.Path);
	}
}
