using System.Diagnostics.CodeAnalysis;
using PeterJuhasz.Text.Yaml.Serialization;

namespace PeterJuhasz.Text.Yaml.Tests.Serialization;

[TestClass]
public class ConstructorTests
{
	public sealed record class Server(string Host, int Port = 80, Uri? Proxy = null, EnumTests.Level Level = EnumTests.Level.Information);

	public sealed record class Cluster(string Name, IReadOnlyList<Server> Servers, string? Description)
	{
		public int Replicas { get; init; } = 1;

		public string? Region { get; set; }
	}

	public sealed record class Renamed([property: YamlPropertyName("host-name")] string HostName, [YamlPropertyName("port-number")] int Port);

	public sealed record class Naming(string FirstName, int MaxRetryCount);

	public record struct Point(int X, int Y);

	public readonly record struct ReadOnlyPoint(int X, int Y);

	public sealed record class Required(string Name)
	{
		public required int Count { get; init; }
	}

	public sealed record class Defaults(string? Name, int? Count, DateOnly Date = default, TimeSpan Timeout = default);

	// a regular class, which assigns the parameter to a get only property of a different casing
	public sealed class Immutable(string name, int count)
	{
		public string Name { get; } = name;

		public int Count { get; } = count;
	}

	public sealed class Marked
	{
		public Marked()
		{
			Name = "parameterless";
		}

		[YamlConstructor]
		public Marked(string name)
		{
			Name = name;
		}

		public string Name { get; }
	}

	public sealed class MarkedPrivate
	{
		[YamlConstructor]
		private MarkedPrivate(string name) => Name = name;

		public string Name { get; }
	}

	public sealed class Validated
	{
		public Validated(int port)
		{
			ArgumentOutOfRangeException.ThrowIfNegative(port);
			Port = port;
		}

		public int Port { get; }
	}

	public sealed class SetsRequired
	{
		[SetsRequiredMembers]
		public SetsRequired(string name) => Name = name;

		public required string Name { get; init; }
	}

	[TestMethod]
	public void Record()
	{
		Assert.AreEqual(new Server(Host: "a", Port: 8080, Proxy: new("http://proxy/"), Level: EnumTests.Level.Debug), YamlSerializer.Deserialize<Server>("Host: a\nPort: 8080\nProxy: http://proxy/\nLevel: debug"));
	}

	[TestMethod]
	public void DefaultValues()
	{
		Assert.AreEqual(new Server(Host: "a"), YamlSerializer.Deserialize<Server>("Host: a"));
		Assert.AreEqual(new Defaults(Name: null, Count: null), YamlSerializer.Deserialize<Defaults>("{}"));
	}

	[TestMethod]
	public void ParametersAndProperties()
	{
		var yaml = """
			Name: main
			Region: eu
			Replicas: 3
			Servers:
			  - Host: a
			  - Host: b
			    Port: 81
			""";

		var cluster = YamlSerializer.Deserialize<Cluster>(yaml);

		Assert.IsNotNull(cluster);
		Assert.AreEqual("main", cluster.Name);
		Assert.IsNull(cluster.Description);
		Assert.AreEqual(3, cluster.Replicas);
		Assert.AreEqual("eu", cluster.Region);
		Assert.AreSequenceEqual([new Server(Host: "a"), new Server(Host: "b", Port: 81)], cluster.Servers);
	}

	[TestMethod]
	public void PropertyDefaultsKept()
	{
		Assert.AreEqual(1, YamlSerializer.Deserialize<Cluster>("Name: x\nServers: []")?.Replicas);
	}

	[TestMethod]
	public void MissingParameter()
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Cluster>("Servers: []"));
		Assert.AreEqual("The required constructor parameter 'Name' is missing.", exception.Reason);

		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Point>("X: 1"));
	}

	[TestMethod]
	public void NullParameter()
	{
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Server>("Host: a\nPort: ~"));
		Assert.IsNull(YamlSerializer.Deserialize<Defaults>("Count: ~")?.Count);
	}

	[TestMethod]
	public void NamingPolicy()
	{
		var options = new YamlSerializerOptions { PropertyNamingPolicy = YamlNamingPolicy.SnakeCaseLower };

		Assert.AreEqual(new Naming(FirstName: "John", MaxRetryCount: 3), YamlSerializer.Deserialize<Naming>("first_name: John\nmax_retry_count: 3", options));
	}

	[TestMethod]
	public void CaseInsensitive()
	{
		var options = new YamlSerializerOptions { PropertyNameCaseInsensitive = true };

		Assert.AreEqual(new Naming(FirstName: "John", MaxRetryCount: 3), YamlSerializer.Deserialize<Naming>("firstname: John\nMAXRETRYCOUNT: 3", options));
	}

	[TestMethod]
	public void PropertyNameAttribute()
	{
		Assert.AreEqual(new Renamed(HostName: "a", Port: 1), YamlSerializer.Deserialize<Renamed>("host-name: a\nport-number: 1"));
	}

	[TestMethod]
	public void RecordStructs()
	{
		Assert.AreEqual(new Point(X: 1, Y: 2), YamlSerializer.Deserialize<Point>("{X: 1, Y: 2}"));
		Assert.AreEqual(new ReadOnlyPoint(X: 1, Y: 2), YamlSerializer.Deserialize<ReadOnlyPoint>("{X: 1, Y: 2}"));
		Assert.AreEqual(new ReadOnlyPoint(X: 3, Y: 4), YamlSerializer.Deserialize<ReadOnlyPoint?>("{X: 3, Y: 4}"));
	}

	[TestMethod]
	public void RequiredMembers()
	{
		Assert.AreEqual(new Required(Name: "a") { Count = 2 }, YamlSerializer.Deserialize<Required>("Name: a\nCount: 2"));

		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Required>("Name: a"));
		Assert.AreEqual("The required property 'Count' is missing.", exception.Reason);

		Assert.AreEqual("a", YamlSerializer.Deserialize<SetsRequired>("Name: a")?.Name);
	}

	[TestMethod]
	public void ClassWithPrimaryConstructor()
	{
		var immutable = YamlSerializer.Deserialize<Immutable>("Name: a\nCount: 2");

		Assert.IsNotNull(immutable);
		Assert.AreEqual("a", immutable.Name);
		Assert.AreEqual(2, immutable.Count);
	}

	[TestMethod]
	public void MarkedConstructor()
	{
		Assert.AreEqual("a", YamlSerializer.Deserialize<Marked>("Name: a")?.Name);
		Assert.AreEqual("b", YamlSerializer.Deserialize<MarkedPrivate>("Name: b")?.Name);
	}

	[TestMethod]
	public void ConstructorExceptionsNotWrapped()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () => YamlSerializer.Deserialize<Validated>("Port: -1"));
	}

	[TestMethod]
	public void ErrorPath()
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Cluster>("Name: x\nServers:\n  - Host: a\n    Port: abc"));
		Assert.AreEqual("$.Servers[0].Port", exception.Path);
	}
}
