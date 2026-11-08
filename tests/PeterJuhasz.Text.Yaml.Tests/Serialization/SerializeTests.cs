using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Runtime.Serialization;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Serialization;
using static PeterJuhasz.Text.Yaml.Tests.Yaml;

namespace PeterJuhasz.Text.Yaml.Tests.Serialization;

[TestClass]
public class SerializeTests
{
	public class Base
	{
		public string? Id { get; set; }
	}

	public sealed class Configuration : Base
	{
		public string? Name { get; set; }

		public int Port { get; set; }

		public bool Enabled { get; set; }

		public List<Server> Servers { get; set; } = [];

		public Dictionary<string, int>? Limits { get; set; }

		[YamlPropertyName("custom-key")]
		public string? Renamed { get; set; }

		[YamlIgnore]
		public string? Ignored { get; set; }

		// not read by deserialization, so it is not written either
		public string ReadOnly { get; } = "initial";
	}

	public sealed record class Server(string Host, int Port = 80)
	{
		public string? Description { get; set; }
	}

	public enum Level
	{
		Debug,
		Information = 2,
		Negative = -1,
	}

	[Flags]
	public enum Permissions : byte
	{
		None = 0,
		Read = 1,
		Write = 2,
		ReadWrite = 3,
		Execute = 4,
	}

	public enum Status
	{
		[EnumMember(Value = "in-progress")]
		InProgress,

		MaxValue,
	}

	[YamlDerivedType(typeof(Cat), "cat")]
	[YamlDerivedType(typeof(Dog), "dog", "doggo")]
	public abstract class Animal
	{
		public string? Name { get; set; }
	}

	public sealed class Cat : Animal
	{
		public int Lives { get; set; }
	}

	public sealed class Dog : Animal
	{
		public bool Good { get; set; }
	}

	public sealed class Lion : Animal;

	[YamlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
	[YamlDerivedType(typeof(HttpJob), 1)]
	public class Job
	{
		public string? Name { get; set; }
	}

	public sealed class HttpJob : Job
	{
		public Uri? Url { get; set; }
	}

	public sealed class Holder
	{
		public object? Value { get; set; }

		public Node? Raw { get; set; }
	}

	public sealed class Cycle
	{
		public Cycle? Next { get; set; }
	}

	[TestMethod]
	public void Objects()
	{
		var configuration = new Configuration
		{
			Id = "main",
			Name = "app",
			Port = 8080,
			Enabled = true,
			Servers = [new(Host: "a.example.com") { Description = "primary" }],
			Limits = new() { ["cpu"] = 2 },
			Renamed = "x",
			Ignored = "skipped",
		};

		var expected = Root("""
			Id: main
			Name: app
			Port: 8080
			Enabled: true
			Servers:
			- Host: a.example.com
			  Port: 80
			  Description: primary
			Limits:
			  cpu: 2
			custom-key: x
			""");
		Assert.AreEqual(expected, YamlSerializer.SerializeToNode(configuration));
	}

	[TestMethod]
	public void RoundTrip()
	{
		var configuration = new Configuration { Name = "null", Port = 1, Servers = [new(Host: "h", Port: 2), new(Host: "true")], Limits = new() { ["a"] = -1 } };
		var node = YamlSerializer.SerializeToNode(configuration);
		var result = YamlSerializer.Deserialize<Configuration>(node, new() { UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow })!;

		Assert.AreEqual("null", result.Name);
		Assert.AreEqual(1, result.Port);
		Assert.AreSequenceEqual(configuration.Servers, result.Servers);
		Assert.AreSequenceEqual(configuration.Limits, result.Limits);
	}

	[TestMethod]
	public void NamingPolicy()
	{
		var options = new YamlSerializerOptions { PropertyNamingPolicy = YamlNamingPolicy.SnakeCaseLower };
		Assert.AreEqual(Map(("host", Plain("h")), ("port", Plain("1")), ("description", Plain("null"))), YamlSerializer.SerializeToNode(new Server(Host: "h", Port: 1), options));
	}

	[TestMethod]
	public void Null()
	{
		Assert.AreEqual(Plain("null"), YamlSerializer.SerializeToNode<string?>(null));
		Assert.AreEqual(Plain("null"), YamlSerializer.SerializeToNode<int?>(null));
	}

	[TestMethod]
	[DataRow("text", ScalarStyle.Plain)]
	[DataRow("http://example.com/a#b", ScalarStyle.Plain)]
	[DataRow("-foo", ScalarStyle.Plain)]
	[DataRow("a:b", ScalarStyle.Plain)]
	[DataRow("", ScalarStyle.DoubleQuoted)]
	[DataRow("null", ScalarStyle.DoubleQuoted)]
	[DataRow("~", ScalarStyle.DoubleQuoted)]
	[DataRow("true", ScalarStyle.DoubleQuoted)]
	[DataRow("FALSE", ScalarStyle.DoubleQuoted)]
	[DataRow("42", ScalarStyle.DoubleQuoted)]
	[DataRow("0x2A", ScalarStyle.DoubleQuoted)]
	[DataRow("1.5", ScalarStyle.DoubleQuoted)]
	[DataRow(".inf", ScalarStyle.DoubleQuoted)]
	[DataRow(" leading", ScalarStyle.DoubleQuoted)]
	[DataRow("trailing ", ScalarStyle.DoubleQuoted)]
	[DataRow("key: value", ScalarStyle.DoubleQuoted)]
	[DataRow("text #comment", ScalarStyle.DoubleQuoted)]
	[DataRow("ends:", ScalarStyle.DoubleQuoted)]
	[DataRow("- item", ScalarStyle.DoubleQuoted)]
	[DataRow("-", ScalarStyle.DoubleQuoted)]
	[DataRow("#hash", ScalarStyle.DoubleQuoted)]
	[DataRow("[a]", ScalarStyle.DoubleQuoted)]
	[DataRow("{a}", ScalarStyle.DoubleQuoted)]
	[DataRow("*alias", ScalarStyle.DoubleQuoted)]
	[DataRow("&anchor", ScalarStyle.DoubleQuoted)]
	[DataRow("!tag", ScalarStyle.DoubleQuoted)]
	[DataRow("|", ScalarStyle.DoubleQuoted)]
	[DataRow("'quoted'", ScalarStyle.DoubleQuoted)]
	[DataRow("---", ScalarStyle.DoubleQuoted)]
	[DataRow("line\nbreak", ScalarStyle.Literal)]
	[DataRow("tab\there", ScalarStyle.DoubleQuoted)]
	public void Strings(string value, ScalarStyle style)
	{
		Assert.AreEqual(new ScalarNode(Value: value, Style: style), YamlSerializer.SerializeToNode(value));
		Assert.AreEqual(value, YamlSerializer.Deserialize<string>(YamlSerializer.SerializeToNode(value)));
	}

	[TestMethod]
	public void Scalars()
	{
		Assert.AreEqual(Plain("true"), YamlSerializer.SerializeToNode(true));
		Assert.AreEqual(Plain("-42"), YamlSerializer.SerializeToNode(-42));
		Assert.AreEqual(Plain("18446744073709551615"), YamlSerializer.SerializeToNode(UInt64.MaxValue));
		Assert.AreEqual(Plain("170141183460469231731687303715884105727"), YamlSerializer.SerializeToNode(Int128.MaxValue));
		Assert.AreEqual(Plain("123456789012345678901234567890"), YamlSerializer.SerializeToNode(BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture)));
		Assert.AreEqual(Plain("1.5"), YamlSerializer.SerializeToNode(1.5));
		Assert.AreEqual(Plain("0.1"), YamlSerializer.SerializeToNode(0.1f));
		Assert.AreEqual(Plain("1.50"), YamlSerializer.SerializeToNode(1.50m));
		Assert.AreEqual(Plain(".inf"), YamlSerializer.SerializeToNode(System.Double.PositiveInfinity));
		Assert.AreEqual(Plain("-.inf"), YamlSerializer.SerializeToNode(System.Single.NegativeInfinity));
		Assert.AreEqual(Plain(".nan"), YamlSerializer.SerializeToNode(System.Double.NaN));
		Assert.AreEqual(Double("1"), YamlSerializer.SerializeToNode('1'));
		Assert.AreEqual(Plain("1.02:03:04"), YamlSerializer.SerializeToNode(new TimeSpan(1, 2, 3, 4)));
		Assert.AreEqual(Plain("2024-01-02"), YamlSerializer.SerializeToNode(new DateOnly(2024, 1, 2)));
		Assert.AreEqual(Plain("2024-01-02T03:04:05.0000000Z"), YamlSerializer.SerializeToNode(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
		Assert.AreEqual(Plain("https://example.com/"), YamlSerializer.SerializeToNode(new Uri("https://example.com/")));
		Assert.AreEqual(Plain("1.2.3"), YamlSerializer.SerializeToNode(new Version(1, 2, 3)));

		// the text of versions with two components would be read as a number by the core schema
		Assert.AreEqual(Double("1.2"), YamlSerializer.SerializeToNode(new Version(1, 2)));
	}

	[TestMethod]
	public void ScalarsRoundTrip()
	{
		AssertRoundTrip(Byte.MaxValue);
		AssertRoundTrip(SByte.MinValue);
		AssertRoundTrip(Int64.MinValue);
		AssertRoundTrip(UInt64.MaxValue);
		AssertRoundTrip(Int128.MinValue);
		AssertRoundTrip(UInt128.MaxValue);
		AssertRoundTrip(BigInteger.Pow(10, 40));
		AssertRoundTrip((Half)1.5);
		AssertRoundTrip(System.Single.MaxValue);
		AssertRoundTrip(System.Double.Epsilon);
		AssertRoundTrip(1e20);
		AssertRoundTrip(Decimal.MaxValue);
		AssertRoundTrip(System.Double.NaN);
		AssertRoundTrip(new DateTime(2024, 1, 2, 3, 4, 5, 6, DateTimeKind.Local));
		AssertRoundTrip(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)));
		AssertRoundTrip(new TimeOnly(3, 4, 5, 6));
		AssertRoundTrip(-new TimeSpan(1, 2, 3, 4));
		AssertRoundTrip(Guid.NewGuid());
		AssertRoundTrip('\0');
		AssertRoundTrip("😀 \u2028 \"quotes\" \\ end");
	}

	[TestMethod]
	public void Enums()
	{
		Assert.AreEqual(Plain("Information"), YamlSerializer.SerializeToNode(Level.Information));
		Assert.AreEqual(Plain("Negative"), YamlSerializer.SerializeToNode(Level.Negative));
		Assert.AreEqual(Plain("5"), YamlSerializer.SerializeToNode((Level)5));
		Assert.AreEqual(Plain("-5"), YamlSerializer.SerializeToNode((Level)(-5)));
		Assert.AreEqual(Plain("None"), YamlSerializer.SerializeToNode(Permissions.None));
		Assert.AreEqual(Plain("ReadWrite"), YamlSerializer.SerializeToNode(Permissions.Read | Permissions.Write));
		Assert.AreEqual(Plain("Read, Execute"), YamlSerializer.SerializeToNode(Permissions.Read | Permissions.Execute));
		Assert.AreEqual(Plain("ReadWrite, Execute"), YamlSerializer.SerializeToNode(Permissions.Read | Permissions.Write | Permissions.Execute));
		Assert.AreEqual(Plain("12"), YamlSerializer.SerializeToNode((Permissions)12));
		Assert.AreEqual(Plain("in-progress"), YamlSerializer.SerializeToNode(Status.InProgress));
		Assert.AreEqual(Plain("max_value"), YamlSerializer.SerializeToNode(Status.MaxValue, new() { EnumNamingPolicy = YamlNamingPolicy.SnakeCaseLower }));

		AssertRoundTrip(Permissions.Read | Permissions.Execute);
		AssertRoundTrip((Permissions)12);
		AssertRoundTrip((Level)(-5));
		AssertRoundTrip(Status.InProgress);
	}

	[TestMethod]
	public void Collections()
	{
		var expected = Seq(Plain("1"), Plain("2"), Plain("3"));
		Assert.AreEqual(expected, YamlSerializer.SerializeToNode<int[]>([1, 2, 3]));
		Assert.AreEqual(expected, YamlSerializer.SerializeToNode(new List<int> { 1, 2, 3 }));
		Assert.AreEqual(expected, YamlSerializer.SerializeToNode<IEnumerable<int>>([1, 2, 3]));
		Assert.AreEqual(expected, YamlSerializer.SerializeToNode<IReadOnlyList<int>>([1, 2, 3]));
		Assert.AreEqual(expected, YamlSerializer.SerializeToNode(ImmutableArray.Create(1, 2, 3)));
		Assert.AreEqual(expected, YamlSerializer.SerializeToNode(ImmutableList.Create(1, 2, 3)));
		Assert.AreEqual(expected, YamlSerializer.SerializeToNode(new Queue<int>([1, 2, 3])));
		Assert.AreEqual(Seq(), YamlSerializer.SerializeToNode(Array.Empty<int>()));
		Assert.AreEqual(Seq(Plain("null"), Plain("1")), YamlSerializer.SerializeToNode<int?[]>([null, 1]));
	}

	[TestMethod]
	public void Stacks()
	{
		var expected = Seq(Plain("1"), Plain("2"), Plain("3"));
		Assert.AreEqual(expected, YamlSerializer.SerializeToNode(new Stack<int>([1, 2, 3])));
		Assert.AreEqual(expected, YamlSerializer.SerializeToNode(new ConcurrentStack<int>([1, 2, 3])));
		Assert.AreEqual(expected, YamlSerializer.SerializeToNode(ImmutableStack.Create(1, 2, 3)));

		var stack = YamlSerializer.Deserialize<Stack<int>>(YamlSerializer.SerializeToNode(new Stack<int>([1, 2, 3])))!;
		Assert.AreEqual(3, stack.Peek());
	}

	[TestMethod]
	public void Dictionaries()
	{
		var expected = Map(("a", Plain("1")), ("b", Plain("2")));
		Assert.AreEqual(expected, YamlSerializer.SerializeToNode(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }));
		Assert.AreEqual(expected, YamlSerializer.SerializeToNode<IReadOnlyDictionary<string, int>>(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }));
		Assert.AreEqual(expected, YamlSerializer.SerializeToNode(ImmutableSortedDictionary.CreateRange([KeyValuePair.Create("b", 2), KeyValuePair.Create("a", 1)])));
		Assert.AreEqual(Map((Plain("1"), Plain("one")), (Double("true"), Plain("yes"))), YamlSerializer.SerializeToNode(new Dictionary<object, string> { [1] = "one", ["true"] = "yes" }));

		var frozen = new Dictionary<int, Level> { [1] = Level.Debug, [2] = Level.Information }.ToFrozenDictionary();
		var result = YamlSerializer.Deserialize<FrozenDictionary<int, Level>>(YamlSerializer.SerializeToNode(frozen))!;
		Assert.AreSequenceEqual(frozen.OrderBy(static p => p.Key), result.OrderBy(static p => p.Key));
	}

	[TestMethod]
	public void Bytes()
	{
		Assert.AreEqual(Plain("AQID"), YamlSerializer.SerializeToNode<byte[]>([1, 2, 3]));
		Assert.AreEqual(Double(""), YamlSerializer.SerializeToNode(Array.Empty<byte>()));

		// the base64 text of these bytes would be read as an integer
		var bytes = Convert.FromBase64String("1234");
		Assert.AreEqual(Double("1234"), YamlSerializer.SerializeToNode(bytes));
		Assert.AreSequenceEqual(bytes, YamlSerializer.Deserialize<byte[]>(YamlSerializer.SerializeToNode(bytes)));
	}

	[TestMethod]
	public void Polymorphism()
	{
		Animal[] animals = [new Cat { Name = "Tom", Lives = 9 }, new Dog { Name = "Rex", Good = true }];
		var expected = Root("""
			- $type: cat
			  Name: Tom
			  Lives: 9
			- $type: dog
			  Name: Rex
			  Good: true
			""");
		var node = YamlSerializer.SerializeToNode(animals);
		Assert.AreEqual(expected, node);

		var result = YamlSerializer.Deserialize<Animal[]>(node, new() { UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow })!;
		Assert.AreEqual(9, ((Cat)result[0]).Lives);
		Assert.IsTrue(((Dog)result[1]).Good);
	}

	[TestMethod]
	public void PolymorphismBaseAndIntegerDiscriminator()
	{
		Assert.AreEqual(Map(("Name", Plain("a"))), YamlSerializer.SerializeToNode(new Job { Name = "a" }));
		Assert.AreEqual(Map(("kind", Plain("1")), ("Name", Plain("b")), ("Url", Plain("http://x/"))), YamlSerializer.SerializeToNode<Job>(new HttpJob { Name = "b", Url = new("http://x/") }));
	}

	[TestMethod]
	public void PolymorphismUnknownDerivedType()
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(() => YamlSerializer.SerializeToNode<Animal[]>([new Lion()]));
		Assert.AreEqual("$[0]", exception.Path);
	}

	[TestMethod]
	public void DerivedTypeAsDeclaredObject()
	{
		// the runtime type is used for object members, without a discriminator, just like for any other type
		Assert.AreEqual(Map(("Value", Map(("Name", Plain("Tom")), ("Lives", Plain("0")))), ("Raw", Plain("null"))), YamlSerializer.SerializeToNode(new Holder { Value = new Cat { Name = "Tom" } }));
	}

	[TestMethod]
	public void Nodes()
	{
		var raw = Map(("a", Seq(Single("b"))));
		Assert.AreEqual(Map(("Value", raw), ("Raw", raw)), YamlSerializer.SerializeToNode(new Holder { Value = raw, Raw = raw }));
	}

	[TestMethod]
	public void AnonymousType()
	{
		Assert.AreEqual(Map(("Name", Plain("x")), ("Count", Plain("2"))), YamlSerializer.SerializeToNode(new { Name = "x", Count = 2 }));
	}

	[TestMethod]
	public void Cycles()
	{
		var cycle = new Cycle();
		cycle.Next = cycle;
		var exception = Assert.ThrowsExactly<YamlSerializationException>(() => YamlSerializer.SerializeToNode(cycle));
		Assert.StartsWith("$.Next.Next.Next", exception.Path);
	}

	[TestMethod]
	public void ErrorPath()
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(() => YamlSerializer.SerializeToNode(new Holder { Value = new Dictionary<string, object> { ["key"] = new List<object> { 1, new int[1, 1] } } }));
		Assert.AreEqual("$.Value.key[1]", exception.Path);
	}

	[TestMethod]
	public void TypeMismatch()
	{
		Assert.ThrowsExactly<ArgumentException>(() => YamlSerializer.SerializeToNode("text", typeof(int)));
	}

	private static void AssertRoundTrip<T>(T value)
	{
		var node = YamlSerializer.SerializeToNode(value);
		Assert.AreEqual(value, YamlSerializer.Deserialize<T>(node), $"Serialized as {node}");
	}
}
