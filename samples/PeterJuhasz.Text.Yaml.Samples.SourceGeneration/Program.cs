using PeterJuhasz.Text.Yaml.Samples.SourceGeneration;
using PeterJuhasz.Text.Yaml.Serialization;

// the generated metadata is used instead of reflection, so this works with trimming and native AOT
var yaml = """
	name: api
	servers:
	  - host: a.example.com
	  - host: b.example.com
	    port: 8443
	log:
	  level: debug
	checks:
	  - kind: http
	    url: https://example.com/health
	  - kind: tcp
	    port: 5432
	""";

var context = new SampleYamlContext(new() { PropertyNamingPolicy = YamlNamingPolicy.SnakeCaseLower, EnumNamingPolicy = YamlNamingPolicy.SnakeCaseLower });
var configuration = YamlSerializer.Deserialize(yaml, context.Configuration)!;

Console.WriteLine($"{configuration.Name} has {configuration.Servers.Count} servers and {configuration.Checks.Count} checks, logging at {configuration.Log.Level}");
Console.Write(YamlSerializer.Serialize(configuration with { Name = "copy" }, context.Configuration));

namespace PeterJuhasz.Text.Yaml.Samples.SourceGeneration
{
	public sealed record class Configuration(string Name, IReadOnlyList<Server> Servers)
	{
		public Log Log { get; init; } = new();

		public List<Check> Checks { get; init; } = [];
	}

	public sealed record class Server(string Host, int Port = 443);

	public sealed class Log
	{
		public Level Level { get; set; } = Level.Information;
	}

	public enum Level
	{
		Debug,
		Information,
		Warning,
	}

	[YamlDerivedType(typeof(HttpCheck), "http")]
	[YamlDerivedType(typeof(TcpCheck), "tcp")]
	[YamlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
	public abstract record class Check;

	public sealed record class HttpCheck(Uri Url) : Check;

	public sealed record class TcpCheck(int Port) : Check;

	[YamlSerializable(typeof(Configuration))]
	internal sealed partial class SampleYamlContext : YamlSerializerContext;
}
