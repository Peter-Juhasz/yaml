using System.Diagnostics.CodeAnalysis;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Parsing;
using PeterJuhasz.Text.Yaml.Serialization;

namespace PeterJuhasz.Text.Yaml.Tests.Serialization;

[TestClass]
public class ObjectTests
{
	public sealed class Configuration
	{
		public string? Name { get; set; }

		public int Port { get; set; }

		public bool Enabled { get; set; }

		public Uri? Endpoint { get; set; }

		public TimeSpan Timeout { get; set; }

		public Server? Primary { get; set; }

		public List<Server> Servers { get; set; } = [];

		public string[]? Tags { get; set; }

		public Dictionary<string, int>? Limits { get; set; }
	}

	public sealed class Server
	{
		public string Host { get; set; } = "localhost";

		public int? Port { get; init; }
	}

	public sealed class Naming
	{
		public string? FirstName { get; set; }

		public int MaxRetryCount { get; set; }

		[YamlPropertyName("custom-key")]
		public string? Renamed { get; set; }

		[YamlIgnore]
		public string? Ignored { get; set; }

		public string ReadOnly { get; } = "initial";
	}

	public sealed class Required
	{
		public required string Name { get; set; }

		public int Optional { get; set; }
	}

	public sealed class RequiredSetByConstructor
	{
		[SetsRequiredMembers]
		public RequiredSetByConstructor() => Name = "default";

		public required string Name { get; set; }
	}

	public struct Point
	{
		public int X { get; set; }

		public int Y { get; set; }
	}

	public sealed class Collections
	{
		public IEnumerable<int>? Enumerable { get; set; }

		public IReadOnlyList<string>? ReadOnlyList { get; set; }

		public ISet<string>? Set { get; set; }

		public HashSet<int>? HashSet { get; set; }

		public LinkedList<int>? LinkedList { get; set; }

		public IReadOnlyDictionary<string, string>? ReadOnlyDictionary { get; set; }

		public SortedDictionary<int, string>? SortedDictionary { get; set; }

		public Dictionary<EnumTests.Level, bool>? EnumKeys { get; set; }

		public List<List<int>>? Nested { get; set; }
	}

	public sealed class Dynamic
	{
		public object? Anything { get; set; }

		public MappingNode? Mapping { get; set; }
	}

	public sealed class AmbiguousConstructors
	{
		public AmbiguousConstructors(string name) => Name = name;

		public AmbiguousConstructors(int id) => Name = id.ToString(System.Globalization.CultureInfo.InvariantCulture);

		public string Name { get; }
	}

	public sealed class NoPublicConstructor
	{
		private NoPublicConstructor() { }

		public string? Name { get; set; }
	}

	public class Base
	{
		public string? Inherited { get; set; }
	}

	public sealed class Derived : Base
	{
		public string? Own { get; set; }
	}

	[TestMethod]
	public void Graph()
	{
		var yaml = """
			name: api
			port: 8080
			enabled: true
			endpoint: https://example.com/
			timeout: 00:00:30
			primary:
			  host: a.example.com
			  port: 443
			servers:
			  - host: b.example.com
			  - port: 8443
			tags: [web, public]
			limits:
			  requests: 100
			  connections: 10
			unknown: skipped
			""";

		var configuration = YamlSerializer.Deserialize<Configuration>(yaml, new() { PropertyNameCaseInsensitive = true });

		Assert.IsNotNull(configuration);
		Assert.AreEqual("api", configuration.Name);
		Assert.AreEqual(8080, configuration.Port);
		Assert.IsTrue(configuration.Enabled);
		Assert.AreEqual(new Uri("https://example.com/"), configuration.Endpoint);
		Assert.AreEqual(TimeSpan.FromSeconds(30), configuration.Timeout);
		Assert.IsNotNull(configuration.Primary);
		Assert.AreEqual("a.example.com", configuration.Primary.Host);
		Assert.AreEqual(443, configuration.Primary.Port);
		Assert.HasCount(2, configuration.Servers);
		Assert.AreEqual("b.example.com", configuration.Servers[0].Host);
		Assert.IsNull(configuration.Servers[0].Port);
		Assert.AreEqual("localhost", configuration.Servers[1].Host);
		Assert.AreEqual(8443, configuration.Servers[1].Port);
		Assert.AreSequenceEqual(["web", "public"], configuration.Tags);
		Assert.IsNotNull(configuration.Limits);
		Assert.AreEqual(100, configuration.Limits["requests"]);
		Assert.AreEqual(10, configuration.Limits["connections"]);
	}

	[TestMethod]
	public void CaseSensitiveByDefault()
	{
		var server = YamlSerializer.Deserialize<Server>("host: a\nPort: 1");

		Assert.IsNotNull(server);
		Assert.AreEqual("localhost", server.Host);
		Assert.AreEqual(1, server.Port);
	}

	[TestMethod]
	public void NamingPolicy()
	{
		var yaml = """
			first_name: John
			max_retry_count: 3
			custom-key: renamed
			ignored: x
			read_only: x
			FirstName: not matched
			""";

		var naming = YamlSerializer.Deserialize<Naming>(yaml, new() { PropertyNamingPolicy = YamlNamingPolicy.SnakeCaseLower });

		Assert.IsNotNull(naming);
		Assert.AreEqual("John", naming.FirstName);
		Assert.AreEqual(3, naming.MaxRetryCount);
		Assert.AreEqual("renamed", naming.Renamed);
		Assert.IsNull(naming.Ignored);
		Assert.AreEqual("initial", naming.ReadOnly);
	}

	[TestMethod]
	public void CamelCaseNamingPolicy()
	{
		var naming = YamlSerializer.Deserialize<Naming>("firstName: Jane\nmaxRetryCount: 5", new() { PropertyNamingPolicy = YamlNamingPolicy.CamelCase });

		Assert.IsNotNull(naming);
		Assert.AreEqual("Jane", naming.FirstName);
		Assert.AreEqual(5, naming.MaxRetryCount);
	}

	[TestMethod]
	public void RequiredProperties()
	{
		Assert.AreEqual("x", YamlSerializer.Deserialize<Required>("Name: x")?.Name);
		Assert.AreEqual("default", YamlSerializer.Deserialize<RequiredSetByConstructor>("{}")?.Name);

		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Required>("Optional: 1"));
		Assert.AreEqual("The required property 'Name' is missing.", exception.Reason);
	}

	[TestMethod]
	public void Struct()
	{
		Assert.AreEqual(new Point { X = 1, Y = 2 }, YamlSerializer.Deserialize<Point>("{X: 1, Y: 2}"));
		Assert.AreEqual(new Point { X = 3 }, YamlSerializer.Deserialize<Point?>("X: 3"));
		Assert.IsNull(YamlSerializer.Deserialize<Point?>("~"));
	}

	[TestMethod]
	public void Inheritance()
	{
		var derived = YamlSerializer.Deserialize<Derived>("Inherited: a\nOwn: b");

		Assert.IsNotNull(derived);
		Assert.AreEqual("a", derived.Inherited);
		Assert.AreEqual("b", derived.Own);
	}

	[TestMethod]
	public void CollectionTypes()
	{
		var yaml = """
			Enumerable: [1, 2]
			ReadOnlyList: [a, b]
			Set: [a, a, b]
			HashSet: [1, 1]
			LinkedList: [3, 4]
			ReadOnlyDictionary: {a: b}
			SortedDictionary: {2: b, 1: a}
			EnumKeys: {debug: true, 2: false}
			Nested: [[1], [2, 3], []]
			""";

		var collections = YamlSerializer.Deserialize<Collections>(yaml);

		Assert.IsNotNull(collections);
		Assert.AreSequenceEqual([1, 2], collections.Enumerable!.ToArray());
		Assert.AreSequenceEqual(["a", "b"], collections.ReadOnlyList!.ToArray());
		Assert.IsInstanceOfType<HashSet<string>>(collections.Set);
		Assert.HasCount(2, collections.Set!);
		Assert.HasCount(1, collections.HashSet!);
		Assert.AreSequenceEqual([3, 4], collections.LinkedList!.ToArray());
		Assert.AreEqual("b", collections.ReadOnlyDictionary!["a"]);
		Assert.AreSequenceEqual([1, 2], collections.SortedDictionary!.Keys.ToArray());
		Assert.IsTrue(collections.EnumKeys![EnumTests.Level.Debug]);
		Assert.IsFalse(collections.EnumKeys[EnumTests.Level.Information]);
		Assert.HasCount(3, collections.Nested!);
		Assert.AreSequenceEqual([2, 3], collections.Nested![1]);
		Assert.IsEmpty(collections.Nested[2]);
	}

	[TestMethod]
	public void RootCollections()
	{
		Assert.AreSequenceEqual([1, 2, 3], YamlSerializer.Deserialize<int[]>("- 1\n- 2\n- 3"));
		Assert.AreSequenceEqual(["a"], YamlSerializer.Deserialize<List<string>>("[a]"));
		Assert.AreEqual(1, YamlSerializer.Deserialize<Dictionary<string, int>>("a: 1")!["a"]);
	}

	[TestMethod]
	public void Nulls()
	{
		var configuration = YamlSerializer.Deserialize<Configuration>("Name: ~\nPrimary:\nServers: null\nTags:");

		Assert.IsNotNull(configuration);
		Assert.IsNull(configuration.Name);
		Assert.IsNull(configuration.Primary);
		Assert.IsNull(configuration.Servers);
		Assert.IsNull(configuration.Tags);
		Assert.IsNull(YamlSerializer.Deserialize<Configuration>(""));
		Assert.IsNull(YamlSerializer.Deserialize<Configuration>("~"));
	}

	[TestMethod]
	public void Model()
	{
		var dynamic = YamlSerializer.Deserialize<Dynamic>("Anything: [a, {b: c}]\nMapping: {x: 1}");

		Assert.IsNotNull(dynamic);
		Assert.IsInstanceOfType<SequenceNode>(dynamic.Anything);
		Assert.IsNotNull(dynamic.Mapping);
		Assert.AreEqual(new ScalarNode(Value: "1"), dynamic.Mapping["x"]);
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Dynamic>("Mapping: [1]"));
	}

	[TestMethod]
	public void FromModel()
	{
		var document = Parser.Parse("Host: example.com");
		var type = typeof(Server);

		Assert.AreEqual("example.com", YamlSerializer.Deserialize<Server>(document)?.Host);
		Assert.AreEqual("example.com", YamlSerializer.Deserialize<Server>(document.Root)?.Host);
		Assert.AreEqual("example.com", ((Server?)YamlSerializer.Deserialize(document, type))?.Host);
	}

	[TestMethod]
	public void ErrorPath()
	{
		var yaml = """
			servers:
			  - host: a
			  - host: b
			    port: abc
			""";

		var exception = Assert.ThrowsExactly<YamlSerializationException>(() => YamlSerializer.Deserialize<Configuration>(yaml, new() { PropertyNamingPolicy = YamlNamingPolicy.CamelCase }));
		Assert.AreEqual("$.servers[1].port", exception.Path);
		Assert.AreEqual("Cannot convert the scalar 'abc' to type 'System.Int32'.", exception.Reason);
		Assert.AreEqual("Cannot convert the scalar 'abc' to type 'System.Int32'. (path $.servers[1].port)", exception.Message);
	}

	[TestMethod]
	public void ErrorPathOfDictionary()
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Configuration>("Limits: {a: 1, b: x}"));
		Assert.AreEqual("$.Limits.b", exception.Path);
	}

	[TestMethod]
	public void MismatchedNodes()
	{
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Server>("[a]"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<List<int>>("a: 1"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Dictionary<string, int>>("[1]"));
	}

	[TestMethod]
	public void UnsupportedTypes()
	{
		var ambiguous = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<AmbiguousConstructors>("Name: x"));
		Assert.Contains("multiple public constructors", ambiguous.Reason);
		var hidden = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<NoPublicConstructor>("Name: x"));
		Assert.Contains("public constructor", hidden.Reason);
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<IDisposable>("a: 1"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<int[,]>("[[1]]"));
	}

	[TestMethod]
	public void ArgumentsValidated()
	{
		Assert.ThrowsExactly<ArgumentNullException>(static () => YamlSerializer.Deserialize<Server>((Node)null!));
		Assert.ThrowsExactly<ArgumentNullException>(static () => YamlSerializer.Deserialize(new ScalarNode(Value: "a"), null!));
	}
}
