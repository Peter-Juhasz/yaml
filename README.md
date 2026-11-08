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
