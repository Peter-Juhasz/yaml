using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using PeterJuhasz.Text.Yaml.Serialization;

namespace PeterJuhasz.Text.Yaml.Tests.Serialization;

[TestClass]
public class CollectionTests
{
	public sealed record class Settings(ImmutableArray<string> Tags, FrozenDictionary<string, int> Limits)
	{
		public ImmutableList<int>? Ports { get; init; }

		public IImmutableSet<string>? Roles { get; init; }
	}

	[TestMethod]
	public void ImmutableArray()
	{
		Assert.AreSequenceEqual([1, 2, 3], YamlSerializer.Deserialize<ImmutableArray<int>>("[1, 2, 3]"));
		Assert.IsTrue(YamlSerializer.Deserialize<ImmutableArray<int>>("[]").IsEmpty);
		Assert.IsNull(YamlSerializer.Deserialize<ImmutableArray<int>?>("~"));
	}

	[TestMethod]
	public void ImmutableSequences()
	{
		Assert.AreSequenceEqual([1, 2], YamlSerializer.Deserialize<ImmutableList<int>>("[1, 2]")!);
		Assert.AreSequenceEqual([1, 2], YamlSerializer.Deserialize<IImmutableList<int>>("[1, 2]")!);
		Assert.HasCount(2, YamlSerializer.Deserialize<ImmutableHashSet<int>>("[1, 2, 2]")!);
		Assert.HasCount(2, YamlSerializer.Deserialize<IImmutableSet<int>>("[1, 2, 2]")!);
		Assert.AreSequenceEqual([1, 2, 3], YamlSerializer.Deserialize<ImmutableSortedSet<int>>("[3, 1, 2]")!);
		Assert.AreSequenceEqual([1, 2], YamlSerializer.Deserialize<ImmutableQueue<int>>("[1, 2]")!);
		Assert.AreSequenceEqual([1, 2], YamlSerializer.Deserialize<IImmutableQueue<int>>("[1, 2]")!);

		// the items are pushed in order, so the last one is on the top
		Assert.AreEqual(2, YamlSerializer.Deserialize<ImmutableStack<int>>("[1, 2]")!.Peek());
		Assert.AreEqual(2, YamlSerializer.Deserialize<IImmutableStack<int>>("[1, 2]")!.Peek());
	}

	[TestMethod]
	public void ImmutableDictionaries()
	{
		Assert.AreEqual(1, YamlSerializer.Deserialize<ImmutableDictionary<string, int>>("a: 1")!["a"]);
		Assert.AreEqual(1, YamlSerializer.Deserialize<IImmutableDictionary<string, int>>("a: 1")!["a"]);
		Assert.AreSequenceEqual(["a", "b"], YamlSerializer.Deserialize<ImmutableSortedDictionary<string, int>>("b: 2\na: 1")!.Keys);
	}

	[TestMethod]
	public void Frozen()
	{
		var set = YamlSerializer.Deserialize<FrozenSet<string>>("[a, b, a]");
		Assert.IsNotNull(set);
		Assert.HasCount(2, set);
		Assert.Contains("b", set);

		var dictionary = YamlSerializer.Deserialize<FrozenDictionary<EnumTests.Level, int>>("debug: 1\n2: 2");
		Assert.IsNotNull(dictionary);
		Assert.AreEqual(1, dictionary[EnumTests.Level.Debug]);
		Assert.AreEqual(2, dictionary[EnumTests.Level.Information]);
	}

	[TestMethod]
	public void QueueAndStack()
	{
		var queue = YamlSerializer.Deserialize<Queue<int>>("[1, 2, 3]");
		Assert.IsNotNull(queue);
		Assert.AreEqual(1, queue.Dequeue());

		var stack = YamlSerializer.Deserialize<Stack<int>>("[1, 2, 3]");
		Assert.IsNotNull(stack);
		Assert.AreEqual(3, stack.Pop());
	}

	[TestMethod]
	public void Concurrent()
	{
		Assert.AreSequenceEqual([1, 2], YamlSerializer.Deserialize<ConcurrentQueue<int>>("[1, 2]")!);
		Assert.IsTrue(YamlSerializer.Deserialize<ConcurrentStack<int>>("[1, 2]")!.TryPeek(out var top));
		Assert.AreEqual(2, top);
		Assert.HasCount(2, YamlSerializer.Deserialize<ConcurrentBag<int>>("[1, 2]")!);
		Assert.AreEqual(1, YamlSerializer.Deserialize<ConcurrentDictionary<string, int>>("a: 1")!["a"]);
	}

	[TestMethod]
	public void ReadOnlyWrappers()
	{
		Assert.AreSequenceEqual([1, 2], YamlSerializer.Deserialize<ReadOnlyCollection<int>>("[1, 2]")!);
		Assert.AreEqual(1, YamlSerializer.Deserialize<ReadOnlyDictionary<string, int>>("a: 1")!["a"]);
	}

	[TestMethod]
	public void Members()
	{
		var yaml = """
			Tags: [a, b]
			Limits: {requests: 100}
			Ports: [80, 443]
			Roles: [admin]
			""";

		var settings = YamlSerializer.Deserialize<Settings>(yaml);

		Assert.IsNotNull(settings);
		Assert.AreSequenceEqual(["a", "b"], settings.Tags);
		Assert.AreEqual(100, settings.Limits["requests"]);
		Assert.AreSequenceEqual([80, 443], settings.Ports!);
		Assert.Contains("admin", settings.Roles!);
	}

	[TestMethod]
	public void ErrorPath()
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Settings>("Tags: [a, [b]]\nLimits: {}"));
		Assert.AreEqual("$.Tags[1]", exception.Path);

		var dictionary = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Settings>("Tags: []\nLimits: {a: x}"));
		Assert.AreEqual("$.Limits.a", dictionary.Path);
	}

	[TestMethod]
	public void MismatchedNodes()
	{
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<ImmutableArray<int>>("a: 1"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<FrozenDictionary<string, int>>("[1]"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Queue<int>>("1"));
	}
}
