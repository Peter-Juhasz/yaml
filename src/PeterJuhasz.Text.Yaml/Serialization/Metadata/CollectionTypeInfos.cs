using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Serialization.Converters;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Serialization.Metadata;

internal sealed class ArrayTypeInfo<TElement>(YamlSerializerContext context, YamlTypeInfo<TElement> element) : YamlTypeInfo<TElement[]>(context)
{
	internal override TElement[] ReadCore(Node node)
	{
		// binary data is represented as a base64 encoded scalar
		if (node is ScalarNode scalar && typeof(TElement) == typeof(byte))
		{
			var buffer = new byte[scalar.Value.Length * 3 / 4];
			return Convert.TryFromBase64String(scalar.Value, buffer, out var written) ? (TElement[])(object)buffer[..written] : throw Mismatch(node);
		}

		if (node is not SequenceNode sequence)
		{
			throw Mismatch(node);
		}

		var array = new TElement[sequence.Items.Length];
		for (var i = 0; i < array.Length; i++)
		{
			try
			{
				array[i] = element.Read(sequence.Items[i]);
			}
			catch (YamlSerializationException exception) when (exception.Prepend(i))
			{
				throw;
			}
		}
		return array;
	}

	internal override void WriteCore(IYamlEmitter emitter, TElement[] value, int depth)
	{
		if (value is byte[] bytes)
		{
			emitter.WriteString(Convert.ToBase64String(bytes));
			return;
		}

		WriteSequence(emitter, value, element, depth);
	}
}

// creates the builder and adds the items one by one, the builder may be the collection itself, or a list, which is converted to the collection at the end
internal sealed class CollectionTypeInfo<TCollection, TBuilder, TElement>(YamlSerializerContext context, YamlTypeInfo<TElement> element, Func<TBuilder> create, Action<TBuilder, TElement> add, Func<TBuilder, TCollection> convert, bool isStack) : YamlTypeInfo<TCollection>(context)
	where TCollection : IEnumerable<TElement>
{
	internal override TCollection ReadCore(Node node)
	{
		if (node is not SequenceNode sequence)
		{
			throw Mismatch(node);
		}

		var builder = create();
		for (var i = 0; i < sequence.Items.Length; i++)
		{
			try
			{
				add(builder, element.Read(sequence.Items[i]));
			}
			catch (YamlSerializationException exception) when (exception.Prepend(i))
			{
				throw;
			}
		}
		return convert(builder);
	}

	// stacks enumerate their items from the top, but they are pushed in order when read, so they are written in reverse
	internal override void WriteCore(IYamlEmitter emitter, TCollection value, int depth) => WriteSequence(emitter, isStack ? value.Reverse() : value, element, depth);
}

internal sealed class DictionaryTypeInfo<TDictionary, TBuilder, TKey, TValue>(YamlSerializerContext context, YamlTypeInfo<TKey> key, YamlTypeInfo<TValue> value, Func<TBuilder> create, Action<TBuilder, TKey, TValue> set, Func<TBuilder, TDictionary> convert) : YamlTypeInfo<TDictionary>(context)
	where TDictionary : IEnumerable<KeyValuePair<TKey, TValue>>
{
	internal override TDictionary ReadCore(Node node)
	{
		if (node is not MappingNode mapping)
		{
			throw Mismatch(node);
		}

		// the indexer is used instead of add, so keys converted to the same value do not fail
		var builder = create();
		foreach (var entry in mapping.Entries)
		{
			try
			{
				var itemKey = key.Read(entry.Key);
				if (itemKey is null)
				{
					throw SerializationFacts.NullKey();
				}

				set(builder, itemKey, value.Read(entry.Value));
			}
			catch (YamlSerializationException exception) when (exception.Prepend(SerializationFacts.GetPath(entry.Key)))
			{
				throw;
			}
		}
		return convert(builder);
	}

	internal override void WriteCore(IYamlEmitter emitter, TDictionary dictionary, int depth)
	{
		emitter.WriteStartMapping();
		foreach (var (itemKey, item) in dictionary)
		{
			var path = itemKey?.ToString() ?? "?";
			Members.Write(emitter, key, itemKey, path, depth);
			Members.Write(emitter, value, item, path, depth);
		}
		emitter.WriteEndMapping();
	}
}
