using System.Collections;
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
}

// creates the collection and adds the items one by one, the created type may be an implementation of the declared one
[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal sealed class CollectionConverter : YamlConverter
{
	private readonly Type implementationType;
	private readonly Type elementType;
	private readonly MethodInfo add;

	public CollectionConverter(Type type, Type implementationType) : base(type)
	{
		var collection = FindInterface(implementationType, typeof(ICollection<>))!;
		this.implementationType = implementationType;
		elementType = collection.GetGenericArguments()[0];
		add = collection.GetMethod(nameof(ICollection<>.Add))!;
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
				if (collection is IList list)
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
		return collection;
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

	public DictionaryConverter(Type type, Type implementationType) : base(type)
	{
		var dictionary = FindInterface(implementationType, typeof(IDictionary<,>))!;
		this.implementationType = implementationType;
		var arguments = dictionary.GetGenericArguments();
		keyType = arguments[0];
		valueType = arguments[1];

		// the indexer is used instead of add, so keys converted to the same value do not fail
		setItem = dictionary.GetProperty("Item")!.SetMethod!;
	}

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
		return dictionary;
	}
}
