using BenchmarkDotNet.Attributes;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Parsing;
using YamlDotNet.RepresentationModel;

namespace PeterJuhasz.Text.Yaml.Benchmarks;

// both libraries parse the sample to their object model, without resolving scalars to types
[MemoryDiagnoser]
public class ParseBenchmarks
{
	private string _yaml = null!;

	[GlobalSetup]
	public void Setup()
	{
		_yaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sample.yaml"));
	}

	[Benchmark(Baseline = true, Description = "YamlDotNet")]
	public YamlStream YamlDotNet()
	{
		var stream = new YamlStream();
		using var reader = new StringReader(_yaml);
		stream.Load(reader);
		return stream;
	}

	[Benchmark(Description = "PeterJuhasz.Text.Yaml")]
	public DocumentNode PeterJuhasz()
	{
		return Parser.Parse(_yaml);
	}
}
