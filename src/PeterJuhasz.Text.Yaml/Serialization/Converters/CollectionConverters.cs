using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using PeterJuhasz.Text.Yaml.Model;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal sealed class ArrayConverter(Type type) : YamlConverter(type)
{
	private readonly Type elementType = type.GetElementType()!;

	public override object? Read(Node node, YamlSerializerOptions options)
	{
		// binary data is represented as a base64 encoded scalar
		if (node is ScalarNode scalar && elementType == typeof(byte))
		{
			var buffer = new byte[scalar.Value.Length * 3 / 4];
			return Convert.TryFromBase64String(scalar.Value, buffer, out var written) ? buffer[..written] : throw Mismatch(node);
		}

		if (node is not SequenceNode sequence)
		{
			throw Mismatch(node);
		}

		var array = Array.CreateInstance(elementType, sequence.Items.Length);
		for (var i = 0; i < sequence.Items.Length; i++)
		{
			try
			{
				array.SetValue(ReadValue(sequence.Items[i], elementType, options), i);
			}
			catch (YamlSerializationException exception) when (exception.Prepend(i))
			{
				throw;
			}
		}
		return array;
	}

	public override Node Write(object value, YamlSerializerOptions options, int depth) => value switch
	{
		byte[] bytes => ScalarConverter.Text(Convert.ToBase64String(bytes)),
		_ => WriteSequence((Array)value, elementType, options, depth),
	};
}

// creates the collection and adds the items one by one, the created type may be an implementation of the declared one,
// or a builder, which is converted to the declared type at the end
[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal sealed class CollectionConverter : YamlConverter
{
	// stacks enumerate their items from the top, but they are pushed in order when read, so they are written in reverse
	private static readonly FrozenSet<Type> Stacks = [typeof(Stack<>), typeof(ConcurrentStack<>), typeof(ImmutableStack<>), typeof(IImmutableStack<>)];

	private readonly Type implementationType;
	private readonly Type elementType;
	private readonly MethodInfo add;
	private readonly Func<object, object>? convert;
	private readonly bool isStack;

	public CollectionConverter(Type type, Type implementationType, Func<object, object>? convert = null) : base(type)
	{
		this.convert = convert;
		var collection = FindInterface(implementationType, typeof(ICollection<>))!;
		this.implementationType = implementationType;
		elementType = collection.GetGenericArguments()[0];
		add = collection.GetMethod(nameof(ICollection<>.Add))!;
		isStack = type.IsGenericType && Stacks.Contains(type.GetGenericTypeDefinition());
	}

	public override Node Write(object value, YamlSerializerOptions options, int depth)
	{
		var items = (IEnumerable)value;
		return WriteSequence(isStack ? items.Cast<object?>().Reverse() : items, elementType, options, depth);
	}

	public override object? Read(Node node, YamlSerializerOptions options)
	{
		if (node is not SequenceNode sequence)
		{
			throw Mismatch(node);
		}

		var collection = Activator.CreateInstance(implementationType)!;
		for (var i = 0; i < sequence.Items.Length; i++)
		{
			try
			{
				var item = ReadValue(sequence.Items[i], elementType, options);

				// the non-generic interface is faster than reflection, and is implemented by most lists
				if (collection is IList { IsReadOnly: false, IsFixedSize: false } list)
				{
					list.Add(item);
				}
				else
				{
					add.Invoke(collection, [item]);
				}
			}
			catch (YamlSerializationException exception) when (exception.Prepend(i))
			{
				throw;
			}
		}
		return convert is null ? collection : convert(collection);
	}
}

[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal sealed class DictionaryConverter : YamlConverter
{
	private readonly Type implementationType;
	private readonly Type keyType;
	private readonly Type valueType;
	private readonly MethodInfo setItem;
	private readonly Func<object, object>? convert;
	private readonly Func<object, IEnumerable<(object Key, object? Value)>> pairs;

	public DictionaryConverter(Type type, Type implementationType, Func<object, object>? convert = null) : base(type)
	{
		this.convert = convert;
		var dictionary = FindInterface(implementationType, typeof(IDictionary<,>))!;
		this.implementationType = implementationType;
		var arguments = dictionary.GetGenericArguments();
		keyType = arguments[0];
		valueType = arguments[1];

		// the indexer is used instead of add, so keys converted to the same value do not fail
		setItem = dictionary.GetProperty("Item")!.SetMethod!;

		// the declared type may not implement the generic dictionary interface, but all of them enumerate their pairs
		pairs = typeof(DictionaryConverter).GetMethod(nameof(Pairs), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(arguments).CreateDelegate<Func<object, IEnumerable<(object Key, object? Value)>>>();
	}

	public override Node Write(object value, YamlSerializerOptions options, int depth)
	{
		var entries = ImmutableArray.CreateBuilder<MappingEntry>();
		foreach (var (key, item) in pairs(value))
		{
			var path = key.ToString() ?? "?";
			entries.Add(new(Key: WriteMember(key, keyType, path, options, depth), Value: WriteMember(item, valueType, path, options, depth)));
		}
		return new MappingNode(Entries: entries.DrainToImmutable());
	}

	private static IEnumerable<(object Key, object? Value)> Pairs<TKey, TValue>(object dictionary) => ((IEnumerable<KeyValuePair<TKey, TValue>>)dictionary).Select(static p => ((object)p.Key!, (object?)p.Value));

	public override object? Read(Node node, YamlSerializerOptions options)
	{
		if (node is not MappingNode mapping)
		{
			throw Mismatch(node);
		}

		var dictionary = Activator.CreateInstance(implementationType)!;
		foreach (var entry in mapping.Entries)
		{
			try
			{
				var key = ReadValue(entry.Key, keyType, options) ?? throw new YamlSerializationException("Keys of dictionaries cannot be null.");
				var value = ReadValue(entry.Value, valueType, options);
				if (dictionary is IDictionary nonGeneric)
				{
					nonGeneric[key] = value;
				}
				else
				{
					setItem.Invoke(dictionary, [key, value]);
				}
			}
			catch (YamlSerializationException exception) when (exception.Prepend(entry.Key is ScalarNode scalar ? scalar.Value : "?"))
			{
				throw;
			}
		}
		return convert is null ? dictionary : convert(dictionary);
	}
}
