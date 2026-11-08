# YAML

Lightweight YAML parser written in .NET C#, producing an immutable object model.

Supported features:
- Block mappings and sequences, nested into each other
- Flow mappings and sequences (`{a: 1}`, `[a, b]`), including JSON-style input
- Plain, single-quoted and double-quoted scalars (with escape sequences), multi-line folding
- Literal (`|`) and folded (`>`) block scalars with chomping and indentation indicators
- Comments
- Multiple documents (`---`, `...`), directives are skipped

Not supported: anchors, aliases, tags and explicit (`?`) keys. These raise a `YamlException`.

## Benchmarks

Parsing a ~200 line configuration file to an object model ([benchmarks](benchmarks/PeterJuhasz.Text.Yaml.Benchmarks)):

| Method                | Mean      | Allocated |
|---------------------- |----------:|----------:|
| YamlDotNet            | 102.50 us | 175.61 KB |
| PeterJuhasz.Text.Yaml |  18.67 us |   30.2 KB |

## High-level API

To parse a YAML string to a Document Object Model:

```cs
var document = Parser.Parse(yaml);
```

To parse a stream with multiple documents:

```cs
var documents = Parser.ParseStream(yaml);
```

Then you can navigate the document tree:

```cs
if (document.Root is MappingNode mapping && mapping["name"] is ScalarNode name)
{
	Console.WriteLine(name.Value);
}
```

The model consists of:
- `DocumentNode(Root)`
- `ScalarNode(Value, Style)`, where `Style` is `Plain`, `SingleQuoted`, `DoubleQuoted`, `Literal` or `Folded`
- `SequenceNode(Items)`
- `MappingNode(Entries)` of `MappingEntry(Key, Value)`

Nodes have structural equality.

## Scalar values

Scalars keep their raw text in `Value`. To read typed values, use the conversion methods, which follow the YAML 1.2 core schema:

```cs
if (scalar.TryGetInt32(out var count)) { ... }
```

| Method | Accepted values |
|-|-|
| `IsNull` | `null`, `Null`, `NULL`, `~`, empty |
| `TryGetBoolean` | `true`, `True`, `TRUE`, `false`, `False`, `FALSE` |
| `TryGetInt32`, `TryGetInt64` | `42`, `-42`, `+42`, `0x2A`, `0o52` |
| `TryGetDouble` | integers, `1.5`, `.5`, `1e3`, `.inf`, `-.inf`, `.nan` |
| `TryGetDecimal` | integers, `1.5`, `.5`, `1e3` |

Only plain scalars are converted: quoted and block scalars like `'42'` are always strings. Values out of range of the target type return `false`.

## Serialization

To deserialize a document into a .NET object:

```cs
var configuration = YamlSerializer.Deserialize<Configuration>(yaml);
```

Objects are created by:
- the public parameterless constructor, then their settable properties are populated
- otherwise the single public constructor, like the primary constructor of records, or the one marked with `[YamlConstructor]`

Constructor parameters use the key of the property with the same name, so naming policies and `[YamlPropertyName]` apply to them, and the remaining settable properties are populated afterwards. Missing parameters get their default value, or `null` when they are nullable, otherwise they raise an error.

```cs
public sealed record class Server(string Host, int Port = 80);
```

An already parsed `DocumentNode` or any `Node` can be deserialized as well. Options:

```cs
var options = new YamlSerializerOptions
{
	PropertyNamingPolicy = YamlNamingPolicy.SnakeCaseLower, // CamelCase, SnakeCaseLower/Upper, KebabCaseLower/Upper
	PropertyNameCaseInsensitive = true,
	EnumNamingPolicy = YamlNamingPolicy.SnakeCaseLower,
	UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow, // throw for keys, which do not map to any member
};
```

Supported types:
- `string`, `char`, `bool`, all integer types (including `Int128`, `UInt128`, `BigInteger`), `Half`, `float`, `double`, `decimal`
- `DateTime`, `DateTimeOffset` (UTC when no offset is given), `DateOnly`, `TimeOnly`, `TimeSpan` (`1.02:03:04` or ISO 8601 `P1DT2H3M4S`), `Uri`, `Guid`, `Version`, and any other `IParsable<T>`
- enums, from integers (`2`) or member names (`Information`, case insensitive), comma separated for flags (`Read, Write`); `[EnumMember(Value = "in-progress")]` replaces the name of a member
- `Nullable<T>`, arrays, `List<T>`, `HashSet<T>`, `Dictionary<TKey, TValue>`, their interfaces, and any collection with a parameterless constructor
- immutable (`ImmutableArray<T>`, `ImmutableList<T>`, `ImmutableHashSet<T>`, `ImmutableSortedSet<T>`, `ImmutableQueue<T>`, `ImmutableStack<T>`, `ImmutableDictionary<TKey, TValue>`, `ImmutableSortedDictionary<TKey, TValue>` and their interfaces) and frozen (`FrozenSet<T>`, `FrozenDictionary<TKey, TValue>`) collections
- `Queue<T>`, `Stack<T>`, concurrent collections, `ReadOnlyCollection<T>`, `ReadOnlyDictionary<TKey, TValue>`, and any collection with a constructor accepting a list or dictionary; items of stacks are pushed in order, so the last one is on the top
- `byte[]` from a base64 encoded scalar
- `Node` and `object` properties receive the object model as is

Properties can be renamed with `[YamlPropertyName("key")]` and skipped with `[YamlIgnore]`. Unknown keys are ignored unless `UnmappedMemberHandling` is `Disallow`, missing `required` properties raise an error. Booleans and numbers follow the core schema just like the conversion methods above, so `'42'` is not an integer. Errors raise a `YamlSerializationException` with the `Path` of the node, like `$.servers[1].port`.

Deserialization uses reflection, so it is annotated as not trimming and AOT compatible.
