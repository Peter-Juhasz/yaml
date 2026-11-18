using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace PeterJuhasz.Text.Yaml.SourceGeneration;

// the types the generator looks for, resolved once per context
internal sealed class KnownSymbols
{
	public KnownSymbols(Compilation compilation)
	{
		Compilation = compilation;
		Context = Get("PeterJuhasz.Text.Yaml.Serialization.YamlSerializerContext");
		Serializable = Get("PeterJuhasz.Text.Yaml.Serialization.YamlSerializableAttribute");
		DerivedType = Get("PeterJuhasz.Text.Yaml.Serialization.YamlDerivedTypeAttribute");
		Polymorphic = Get("PeterJuhasz.Text.Yaml.Serialization.YamlPolymorphicAttribute");
		PropertyName = Get("PeterJuhasz.Text.Yaml.Serialization.YamlPropertyNameAttribute");
		Ignore = Get("PeterJuhasz.Text.Yaml.Serialization.YamlIgnoreAttribute");
		Constructor = Get("PeterJuhasz.Text.Yaml.Serialization.YamlConstructorAttribute");
		Node = Get("PeterJuhasz.Text.Yaml.Model.Node");
		EnumMember = Get("System.Runtime.Serialization.EnumMemberAttribute");
		Flags = Get("System.FlagsAttribute");
		SetsRequiredMembers = Get("System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");
		AllowNull = Get("System.Diagnostics.CodeAnalysis.AllowNullAttribute");
		DisallowNull = Get("System.Diagnostics.CodeAnalysis.DisallowNullAttribute");
		Parsable = Get("System.IParsable`1");

		Enumerable = compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);
		Collection = Get("System.Collections.Generic.ICollection`1");
		Dictionary = Get("System.Collections.Generic.IDictionary`2");
		ReadOnlyDictionary = Get("System.Collections.Generic.IReadOnlyDictionary`2");
		ListType = Get("System.Collections.Generic.List`1");
		DictionaryType = Get("System.Collections.Generic.Dictionary`2");

		ListInterfaces = Set(Enumerable, Collection, Get("System.Collections.Generic.IList`1"), Get("System.Collections.Generic.IReadOnlyCollection`1"), Get("System.Collections.Generic.IReadOnlyList`1"));
		SetInterfaces = Set(Get("System.Collections.Generic.ISet`1"), Get("System.Collections.Generic.IReadOnlySet`1"));
		DictionaryInterfaces = Set(Dictionary, ReadOnlyDictionary);
		Stacks = Set(Get("System.Collections.Generic.Stack`1"), Get("System.Collections.Concurrent.ConcurrentStack`1"), Get("System.Collections.Immutable.ImmutableStack`1"), Get("System.Collections.Immutable.IImmutableStack`1"));

		// immutable and frozen collections are created by their factory methods
		AddFactory(ImmutableSequences, "System.Collections.Immutable.ImmutableArray`1", "global::System.Collections.Immutable.ImmutableArray.CreateRange");
		AddFactory(ImmutableSequences, "System.Collections.Immutable.ImmutableList`1", "global::System.Collections.Immutable.ImmutableList.CreateRange");
		AddFactory(ImmutableSequences, "System.Collections.Immutable.IImmutableList`1", "global::System.Collections.Immutable.ImmutableList.CreateRange");
		AddFactory(ImmutableSequences, "System.Collections.Immutable.ImmutableHashSet`1", "global::System.Collections.Immutable.ImmutableHashSet.CreateRange");
		AddFactory(ImmutableSequences, "System.Collections.Immutable.IImmutableSet`1", "global::System.Collections.Immutable.ImmutableHashSet.CreateRange");
		AddFactory(ImmutableSequences, "System.Collections.Immutable.ImmutableSortedSet`1", "global::System.Collections.Immutable.ImmutableSortedSet.CreateRange");
		AddFactory(ImmutableSequences, "System.Collections.Immutable.ImmutableQueue`1", "global::System.Collections.Immutable.ImmutableQueue.CreateRange");
		AddFactory(ImmutableSequences, "System.Collections.Immutable.IImmutableQueue`1", "global::System.Collections.Immutable.ImmutableQueue.CreateRange");
		AddFactory(ImmutableSequences, "System.Collections.Immutable.ImmutableStack`1", "global::System.Collections.Immutable.ImmutableStack.CreateRange");
		AddFactory(ImmutableSequences, "System.Collections.Immutable.IImmutableStack`1", "global::System.Collections.Immutable.ImmutableStack.CreateRange");
		AddFactory(ImmutableSequences, "System.Collections.Frozen.FrozenSet`1", "global::System.Collections.Frozen.FrozenSet.ToFrozenSet");
		AddFactory(ImmutableDictionaries, "System.Collections.Immutable.ImmutableDictionary`2", "global::System.Collections.Immutable.ImmutableDictionary.CreateRange");
		AddFactory(ImmutableDictionaries, "System.Collections.Immutable.IImmutableDictionary`2", "global::System.Collections.Immutable.ImmutableDictionary.CreateRange");
		AddFactory(ImmutableDictionaries, "System.Collections.Immutable.ImmutableSortedDictionary`2", "global::System.Collections.Immutable.ImmutableSortedDictionary.CreateRange");
		AddFactory(ImmutableDictionaries, "System.Collections.Frozen.FrozenDictionary`2", "global::System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary");
	}

	// the scalar types built into the serializer
	public static HashSet<string> Scalars { get; } =
	[
		"System.String", "System.Char", "System.Boolean",
		"System.Byte", "System.SByte", "System.Int16", "System.UInt16", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64", "System.IntPtr", "System.UIntPtr", "System.Int128", "System.UInt128", "System.Numerics.BigInteger",
		"System.Half", "System.Single", "System.Double", "System.Decimal",
		"System.DateTime", "System.DateTimeOffset", "System.DateOnly", "System.TimeOnly", "System.TimeSpan", "System.Uri", "System.Guid", "System.Version",
	];

	public Compilation Compilation { get; }

	public INamedTypeSymbol? Context { get; }

	public INamedTypeSymbol? Serializable { get; }

	public INamedTypeSymbol? DerivedType { get; }

	public INamedTypeSymbol? Polymorphic { get; }

	public INamedTypeSymbol? PropertyName { get; }

	public INamedTypeSymbol? Ignore { get; }

	public INamedTypeSymbol? Constructor { get; }

	public INamedTypeSymbol? Node { get; }

	public INamedTypeSymbol? EnumMember { get; }

	public INamedTypeSymbol? Flags { get; }

	public INamedTypeSymbol? SetsRequiredMembers { get; }

	public INamedTypeSymbol? AllowNull { get; }

	public INamedTypeSymbol? DisallowNull { get; }

	public INamedTypeSymbol? Parsable { get; }

	public INamedTypeSymbol Enumerable { get; }

	public INamedTypeSymbol? Collection { get; }

	public INamedTypeSymbol? Dictionary { get; }

	public INamedTypeSymbol? ReadOnlyDictionary { get; }

	public INamedTypeSymbol? ListType { get; }

	public INamedTypeSymbol? DictionaryType { get; }

	public HashSet<INamedTypeSymbol> ListInterfaces { get; }

	public HashSet<INamedTypeSymbol> SetInterfaces { get; }

	public HashSet<INamedTypeSymbol> DictionaryInterfaces { get; }

	public HashSet<INamedTypeSymbol> Stacks { get; }

	public Dictionary<INamedTypeSymbol, string> ImmutableSequences { get; } = new(SymbolEqualityComparer.Default);

	public Dictionary<INamedTypeSymbol, string> ImmutableDictionaries { get; } = new(SymbolEqualityComparer.Default);

	private INamedTypeSymbol? Get(string metadataName) => Compilation.GetTypeByMetadataName(metadataName);

	private static HashSet<INamedTypeSymbol> Set(params INamedTypeSymbol?[] symbols)
	{
		var set = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
		foreach (var symbol in symbols)
		{
			if (symbol is not null)
			{
				set.Add(symbol);
			}
		}
		return set;
	}

	private void AddFactory(Dictionary<INamedTypeSymbol, string> factories, string metadataName, string method)
	{
		if (Get(metadataName) is { } symbol)
		{
			factories[symbol] = method;
		}
	}
}
