using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

// collections, which cannot be populated one by one, are created from a filled list or dictionary
[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal static class CollectionFactory
{
	private static readonly FrozenDictionary<Type, (Type Factory, string Method)> Sequences = new Dictionary<Type, (Type Factory, string Method)>
	{
		[typeof(ImmutableArray<>)] = (typeof(ImmutableArray), nameof(ImmutableArray.CreateRange)),
		[typeof(ImmutableList<>)] = (typeof(ImmutableList), nameof(ImmutableList.CreateRange)),
		[typeof(IImmutableList<>)] = (typeof(ImmutableList), nameof(ImmutableList.CreateRange)),
		[typeof(ImmutableHashSet<>)] = (typeof(ImmutableHashSet), nameof(ImmutableHashSet.CreateRange)),
		[typeof(IImmutableSet<>)] = (typeof(ImmutableHashSet), nameof(ImmutableHashSet.CreateRange)),
		[typeof(ImmutableSortedSet<>)] = (typeof(ImmutableSortedSet), nameof(ImmutableSortedSet.CreateRange)),
		[typeof(ImmutableQueue<>)] = (typeof(ImmutableQueue), nameof(ImmutableQueue.CreateRange)),
		[typeof(IImmutableQueue<>)] = (typeof(ImmutableQueue), nameof(ImmutableQueue.CreateRange)),
		[typeof(ImmutableStack<>)] = (typeof(ImmutableStack), nameof(ImmutableStack.CreateRange)),
		[typeof(IImmutableStack<>)] = (typeof(ImmutableStack), nameof(ImmutableStack.CreateRange)),
		[typeof(FrozenSet<>)] = (typeof(FrozenSet), nameof(FrozenSet.ToFrozenSet)),
	}.ToFrozenDictionary();

	private static readonly FrozenDictionary<Type, (Type Factory, string Method)> Dictionaries = new Dictionary<Type, (Type Factory, string Method)>
	{
		[typeof(ImmutableDictionary<,>)] = (typeof(ImmutableDictionary), nameof(ImmutableDictionary.CreateRange)),
		[typeof(IImmutableDictionary<,>)] = (typeof(ImmutableDictionary), nameof(ImmutableDictionary.CreateRange)),
		[typeof(ImmutableSortedDictionary<,>)] = (typeof(ImmutableSortedDictionary), nameof(ImmutableSortedDictionary.CreateRange)),
		[typeof(FrozenDictionary<,>)] = (typeof(FrozenDictionary), nameof(FrozenDictionary.ToFrozenDictionary)),
	}.ToFrozenDictionary();

	// immutable and frozen collections are created by their factory methods
	public static YamlConverter? TryCreateImmutable(Type type)
	{
		if (!type.IsGenericType)
		{
			return null;
		}

		var definition = type.GetGenericTypeDefinition();
		var arguments = type.GetGenericArguments();
		if (Sequences.TryGetValue(definition, out var sequence))
		{
			var builder = typeof(List<>).MakeGenericType(arguments);
			return FindFactoryMethod(sequence.Factory, sequence.Method, arguments, typeof(IEnumerable<>).MakeGenericType(arguments)) is { } convert
				? new CollectionConverter(type, builder, convert)
				: new UnsupportedConverter(type, $"The factory method '{sequence.Factory}.{sequence.Method}' was not found.");
		}

		if (Dictionaries.TryGetValue(definition, out var dictionary))
		{
			var builder = typeof(Dictionary<,>).MakeGenericType(arguments);
			var source = typeof(IEnumerable<>).MakeGenericType(typeof(KeyValuePair<,>).MakeGenericType(arguments));
			return FindFactoryMethod(dictionary.Factory, dictionary.Method, arguments, source) is { } convert
				? new DictionaryConverter(type, builder, convert)
				: new UnsupportedConverter(type, $"The factory method '{dictionary.Factory}.{dictionary.Method}' was not found.");
		}

		return null;
	}

	// any other collection, which accepts the filled list or dictionary in its constructor, like queues, stacks and read-only wrappers
	public static YamlConverter? TryCreateFromConstructor(Type type)
	{
		if (type.IsAbstract || type.IsInterface)
		{
			return null;
		}

		if ((YamlConverter.FindInterface(type, typeof(IDictionary<,>)) ?? YamlConverter.FindInterface(type, typeof(IReadOnlyDictionary<,>))) is { } dictionary)
		{
			var builder = typeof(Dictionary<,>).MakeGenericType(dictionary.GetGenericArguments());
			if (FindConstructor(type, builder) is { } convert)
			{
				return new DictionaryConverter(type, builder, convert);
			}
		}

		if (YamlConverter.FindInterface(type, typeof(IEnumerable<>)) is { } enumerable)
		{
			var builder = typeof(List<>).MakeGenericType(enumerable.GetGenericArguments());
			if (FindConstructor(type, builder) is { } convert)
			{
				return new CollectionConverter(type, builder, convert);
			}
		}

		return null;
	}

	private static Func<object, object>? FindFactoryMethod(Type factory, string name, Type[] typeArguments, Type sourceType)
	{
		foreach (var candidate in factory.GetMethods(BindingFlags.Public | BindingFlags.Static))
		{
			if (candidate.Name != name || !candidate.IsGenericMethodDefinition || candidate.GetGenericArguments().Length != typeArguments.Length)
			{
				continue;
			}

			MethodInfo method;
			try
			{
				method = candidate.MakeGenericMethod(typeArguments);
			}
			catch (ArgumentException)
			{
				// the type arguments violate the constraints of an unrelated overload
				continue;
			}

			// the source is the first parameter, the rest are optional, like the comparers
			if (method.GetParameters() is not [var source, .. var rest] || source.ParameterType != sourceType || !rest.All(static p => p.HasDefaultValue))
			{
				continue;
			}

			var defaults = rest.Select(static p => p.DefaultValue).ToArray();
			return builder => method.Invoke(null, BindingFlags.DoNotWrapExceptions, binder: null, [builder, .. defaults], culture: null)!;
		}

		return null;
	}

	private static Func<object, object>? FindConstructor(Type type, Type builder)
	{
		foreach (var constructor in type.GetConstructors())
		{
			if (constructor.GetParameters() is [var parameter] && parameter.ParameterType.IsAssignableFrom(builder))
			{
				return source => constructor.Invoke(BindingFlags.DoNotWrapExceptions, binder: null, [source], culture: null);
			}
		}

		return null;
	}
}
