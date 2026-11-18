using BenchmarkDotNet.Attributes;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Parsing;
using PeterJuhasz.Text.Yaml.Serialization;

namespace PeterJuhasz.Text.Yaml.Benchmarks;

// both serializers convert between the parsed object model and the same .NET objects, the converters of reflection are cached by the options
[MemoryDiagnoser]
public class SerializationBenchmarks
{
	private static readonly YamlSerializerOptions Options = new() { PropertyNamingPolicy = YamlNamingPolicy.SnakeCaseLower };
	private static readonly BenchmarkYamlContext Context = new(Options);

	private Node _node = null!;
	private Configuration _configuration = null!;

	[GlobalSetup]
	public void Setup()
	{
		var yaml = String.Join('\n', [
			"name: api",
			"timeout: 00:00:30",
			"servers:",
			.. Enumerable.Range(0, 50).SelectMany(static i => (string[])[$"  - host: server{i}.example.com", $"    port: {8000 + i}", "    tags: [web, public]"]),
			"limits:",
			.. Enumerable.Range(0, 20).Select(static i => $"  limit{i}: {i}"),
		]);
		_node = Parser.Parse(yaml).Root;
		_configuration = YamlSerializer.Deserialize(_node, Context.Configuration)!;
	}

	[Benchmark(Baseline = true, Description = "Deserialize, reflection")]
	public Configuration? DeserializeReflection() => YamlSerializer.Deserialize<Configuration>(_node, Options);

	[Benchmark(Description = "Deserialize, generated")]
	public Configuration? DeserializeGenerated() => YamlSerializer.Deserialize(_node, Context.Configuration);

	[Benchmark(Description = "Serialize, reflection")]
	public string SerializeReflection() => YamlSerializer.Serialize(_configuration, Options);

	[Benchmark(Description = "Serialize, generated")]
	public string SerializeGenerated() => YamlSerializer.Serialize(_configuration, Context.Configuration);
}

public sealed class Configuration
{
	public string? Name { get; set; }

	public TimeSpan Timeout { get; set; }

	public List<Server> Servers { get; set; } = [];

	public Dictionary<string, int> Limits { get; set; } = [];
}

public sealed record class Server(string Host, int Port = 80)
{
	public string[] Tags { get; init; } = [];
}

[YamlSerializable(typeof(Configuration))]
internal sealed partial class BenchmarkYamlContext : YamlSerializerContext;
