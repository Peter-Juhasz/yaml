using System.Buffers;
using System.Runtime.ExceptionServices;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Serialization;
using PeterJuhasz.Text.Yaml.Serialization.Metadata;
using PeterJuhasz.Text.Yaml.Writing;
using Reflection = PeterJuhasz.Text.Yaml.Serialization.YamlSerializer;

namespace PeterJuhasz.Text.Yaml.Tests.Serialization;

// hides the serializer for the tests of this namespace, and runs each call by both the reflection based serializer and the generated metadata,
// the results and the errors must be the same, then the result of reflection is returned, so the assertions of the tests apply to both
internal static class YamlSerializer
{
	public static string Serialize<T>(T value, YamlSerializerOptions? options = null) => Parity.Compare(
		typeof(T),
		options,
		() => Reflection.Serialize(value, options),
		context => Reflection.Serialize(value, Parity.GetTypeInfo<T>(context)),
		static text => text
	);

	public static string Serialize(object? value, Type type, YamlSerializerOptions? options = null) => Parity.Compare(
		type,
		options,
		() => Reflection.Serialize(value, type, options),
		context => Reflection.Serialize(value, type, context),
		static text => text
	);

	// the writer is used by reflection only, the generated metadata is compared by the nodes it writes
	public static void Serialize<T, TWriter>(YamlWriter<TWriter> writer, T value, YamlSerializerOptions? options = null) where TWriter : IBufferWriter<char>
	{
		_ = SerializeToNode(value, options);
		Reflection.Serialize(writer, value, options);
	}

	public static void Serialize<TWriter>(YamlWriter<TWriter> writer, object? value, Type type, YamlSerializerOptions? options = null) where TWriter : IBufferWriter<char>
	{
		_ = SerializeToNode(value, type, options);
		Reflection.Serialize(writer, value, type, options);
	}

	public static Node SerializeToNode<T>(T value, YamlSerializerOptions? options = null) => Parity.Compare(
		typeof(T),
		options,
		() => Reflection.SerializeToNode(value, options),
		context => Reflection.SerializeToNode(value, Parity.GetTypeInfo<T>(context)),
		Parity.DescribeNode
	);

	public static Node SerializeToNode(object? value, Type type, YamlSerializerOptions? options = null) => Parity.Compare(
		type,
		options,
		() => Reflection.SerializeToNode(value, type, options),
		context => Reflection.SerializeToNode(value, type, context),
		Parity.DescribeNode
	);

	public static T? Deserialize<T>(string yaml, YamlSerializerOptions? options = null) => Parity.Compare(
		typeof(T),
		options,
		() => Reflection.Deserialize<T>(yaml, options),
		context => Reflection.Deserialize(yaml, Parity.GetTypeInfo<T>(context)),
		result => Parity.DescribeValue(result, typeof(T), options)
	);

	public static T? Deserialize<T>(Node node, YamlSerializerOptions? options = null) => Parity.Compare(
		typeof(T),
		options,
		() => Reflection.Deserialize<T>(node, options),
		context => Reflection.Deserialize(node, Parity.GetTypeInfo<T>(context)),
		result => Parity.DescribeValue(result, typeof(T), options)
	);

	public static object? Deserialize(string yaml, Type type, YamlSerializerOptions? options = null) => Parity.Compare(
		type,
		options,
		() => Reflection.Deserialize(yaml, type, options),
		context => Reflection.Deserialize(yaml, type, context),
		result => Parity.DescribeValue(result, type, options)
	);

	public static object? Deserialize(Node node, Type type, YamlSerializerOptions? options = null) => Parity.Compare(
		type,
		options,
		() => Reflection.Deserialize(node, type, options),
		context => Reflection.Deserialize(node, type, context),
		result => Parity.DescribeValue(result, type, options)
	);
}

internal static class Parity
{
	public static YamlTypeInfo<T> GetTypeInfo<T>(TestYamlContext context) => (YamlTypeInfo<T>)context.GetTypeInfo(typeof(T))!;

	// the type is null, when the call is expected to fail on the argument
	public static TResult Compare<TResult>(Type? type, YamlSerializerOptions? options, Func<TResult> reflection, Func<TestYamlContext, TResult> generated, Func<TResult, string> describe)
	{
		var expected = Capture(reflection);

		// anonymous types cannot be listed on contexts
		if (type is null || !type.Name.Contains("AnonymousType", StringComparison.Ordinal))
		{
			var context = new TestYamlContext(options);
			if (type is not null && context.GetTypeInfo(type) is null)
			{
				Assert.Fail($"The type '{type}' is not listed by {nameof(TestYamlContext)}.");
			}

			var actual = Capture(() => generated(context));
			AssertSame(expected, actual, describe);
		}

		if (expected.Exception is { } exception)
		{
			ExceptionDispatchInfo.Throw(exception);
		}

		return expected.Value!;
	}

	// the values are compared by their runtime type and the text written by reflection
	public static string DescribeValue(object? value, Type type, YamlSerializerOptions? options)
	{
		if (value is null)
		{
			return "null";
		}

		try
		{
			return $"{value.GetType()}: {Reflection.Serialize(value, type, options)}";
		}
		catch (Exception exception)
		{
			return $"{value.GetType()}: {exception.GetType()}: {exception.Message}";
		}
	}

	public static string DescribeNode(Node node) => Reflection.Serialize(node);

	private static (TResult? Value, Exception? Exception) Capture<TResult>(Func<TResult> action)
	{
		try
		{
			return (action(), null);
		}
		catch (Exception exception) when (exception is not AssertFailedException)
		{
			return (default, exception);
		}
	}

	private static void AssertSame<TResult>((TResult? Value, Exception? Exception) expected, (TResult? Value, Exception? Exception) actual, Func<TResult, string> describe)
	{
		switch (expected.Exception, actual.Exception)
		{
			case (null, null):
				Assert.AreEqual(describe(expected.Value!), describe(actual.Value!), "The generated metadata returned a different result than reflection.");
				break;

			case ({ } reflection, { } generated):
				Assert.AreEqual(reflection.GetType(), generated.GetType(), $"The generated metadata raised a different error than reflection: {generated}");
				Assert.AreEqual(reflection.Message, generated.Message, "The generated metadata raised a different error than reflection.");
				break;

			case (null, { } generated):
				Assert.Fail($"The generated metadata raised an error, but reflection did not: {generated}");
				break;

			case ({ } reflection, null):
				Assert.Fail($"Reflection raised an error, but the generated metadata did not: {reflection.GetType()}: {reflection.Message}");
				break;
		}
	}
}
