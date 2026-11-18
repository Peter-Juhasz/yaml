using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PeterJuhasz.Text.Yaml.Serialization;

namespace PeterJuhasz.Text.Yaml.SourceGeneration.Tests;

[TestClass]
public class GeneratorTests
{
	public TestContext TestContext { get; set; } = null!;

	private static readonly ImmutableArray<MetadataReference> References =
	[
		.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(static path => MetadataReference.CreateFromFile(path)),
		MetadataReference.CreateFromFile(typeof(YamlSerializer).Assembly.Location),
	];

	[TestMethod]
	public void GeneratesCompilableCode()
	{
		var source = """
			using System;
			using System.Collections.Generic;
			using System.Collections.Immutable;
			using System.Runtime.Serialization;
			using PeterJuhasz.Text.Yaml.Serialization;

			namespace Sample
			{
				public enum Level { Debug, [EnumMember(Value = "info")] Information = 2, Negative = -1 }

				[Flags]
				public enum Access : byte { None = 0, Read = 1, Write = 2 }

				public sealed record class Server(string Host, int Port = 80, Level Level = Level.Information, decimal Weight = 1.5m, string Name = "a \"quoted\"\nname", double Ratio = double.NaN);

				public sealed class Settings
				{
					public required string Name { get; init; }

					public int? Timeout { get; set; }

					public ImmutableArray<string> Tags { get; set; }

					public Dictionary<string, List<Server>> Groups { get; set; } = [];

					public Queue<int>? Queue { get; set; }

					public (int Count, string Label) Pair { get; set; }

					public Box<int>? Box { get; set; }

					public int @class { get; set; }

					[YamlIgnore]
					public string? Ignored { get; set; }

					public Access Access { get; set; }

					public object? Anything { get; set; }
				}

				public sealed class Box<T>
				{
					public T? Value { get; set; }
				}

				public struct Point
				{
					public int X { get; init; }

					public required int Y { get; set; }
				}

				public class Base
				{
					public virtual string? Name { get; set; }

					public string? Hidden { get; set; }
				}

				public sealed class Derived : Base
				{
					public override string? Name { get; set; }

					public new int Hidden { get; set; }
				}

				public sealed class WithReference
				{
					[YamlConstructor]
					internal WithReference(in int value) => Value = value;

					public int Value { get; }
				}

				[YamlDerivedType(typeof(Cat), "cat")]
				[YamlDerivedType(typeof(Dog), "dog", "doggo")]
				public abstract class Animal;

				public sealed class Cat : Animal
				{
					public int Lives { get; set; }
				}

				[YamlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
				[YamlDerivedType(typeof(Puppy), 1)]
				public class Dog : Animal
				{
					public bool Good { get; set; }
				}

				public sealed class Puppy : Dog;

				public partial class Outer
				{
					[YamlSerializable(typeof(Settings))]
					[YamlSerializable(typeof(Point?))]
					[YamlSerializable(typeof(Derived))]
					[YamlSerializable(typeof(WithReference))]
					[YamlSerializable(typeof(List<Animal>))]
					[YamlSerializable(typeof(Other.Server), TypeInfoPropertyName = "OtherServer")]
					internal sealed partial class AppContext : YamlSerializerContext;
				}
			}

			namespace Other
			{
				public sealed class Server
				{
					public string? Host { get; set; }
				}
			}
			""";

		var (output, diagnostics) = Run(source);

		Assert.IsEmpty(diagnostics, String.Join("\n", diagnostics));
		var errors = output.GetDiagnostics(TestContext.CancellationToken).Where(static d => d.Severity is DiagnosticSeverity.Error).ToList();
		Assert.IsEmpty(errors, String.Join("\n", errors));

		var context = output.GetTypeByMetadataName("Sample.Outer+AppContext")!;
		foreach (var name in (string[])["Default", "Settings", "Server", "OtherServer", "Level", "Access", "NullablePoint", "Point", "Derived", "WithReference", "ListAnimal", "Animal", "Cat", "Dog", "Puppy", "BoxInt32", "ValueTupleInt32String", "QueueInt32", "ImmutableArrayString", "DictionaryStringListServer", "Object", "GetTypeInfo"])
		{
			Assert.IsNotEmpty(context.GetMembers(name), $"The member '{name}' was not generated.");
		}
	}

	[TestMethod]
	public void GeneratedCodeRuns()
	{
		var source = """
			using PeterJuhasz.Text.Yaml.Serialization;

			public sealed class Model
			{
				public required string Name { get; init; }

				public Point Point { get; set; }

				public WithReference? Reference { get; set; }

				public Box<int>? Box { get; set; }
			}

			public struct Point
			{
				public int X { get; init; }

				public required int Y { get; set; }
			}

			public sealed class WithReference
			{
				[YamlConstructor]
				internal WithReference(in int value, ref string name)
				{
					Value = value;
					Name = name;
				}

				public int Value { get; }

				public string Name { get; }
			}

			public sealed class Box<T>
			{
				public T? Value { get; set; }
			}

			[YamlSerializable(typeof(Model))]
			public sealed partial class AppContext : YamlSerializerContext;
			""";

		var (output, diagnostics) = Run(source);
		Assert.IsEmpty(diagnostics, String.Join("\n", diagnostics));

		using var stream = new MemoryStream();
		var emitted = output.Emit(stream, cancellationToken: TestContext.CancellationToken);
		Assert.IsTrue(emitted.Success, String.Join("\n", emitted.Diagnostics));
		stream.Position = 0;

		// the generated assembly shares the serializer with the tests
		var loadContext = new System.Runtime.Loader.AssemblyLoadContext(nameof(GeneratedCodeRuns), isCollectible: true);
		try
		{
			var assembly = loadContext.LoadFromStream(stream);
			var context = (YamlSerializerContext)Activator.CreateInstance(assembly.GetType("AppContext")!)!;
			var modelType = assembly.GetType("Model")!;

			var yaml = """
				Name: a
				Point:
				  X: 1
				  Y: 2
				Reference:
				  Value: 3
				  Name: b
				Box:
				  Value: 4

				""".ReplaceLineEndings("\n");
			var model = YamlSerializer.Deserialize(yaml, modelType, context);
			Assert.AreEqual(yaml, YamlSerializer.Serialize(model, modelType, context));

			var missing = Assert.ThrowsExactly<YamlSerializationException>(() => YamlSerializer.Deserialize("Name: a\nPoint: {X: 1}", modelType, context));
			Assert.AreEqual("The required property 'Y' is missing.", missing.Reason);
			Assert.AreEqual("$.Point", missing.Path);
		}
		finally
		{
			loadContext.Unload();
		}
	}

	[TestMethod]
	public void NotPartial()
	{
		var diagnostic = Assert.ContainsSingle(Run("""
			[PeterJuhasz.Text.Yaml.Serialization.YamlSerializable(typeof(int))]
			internal sealed class AppContext : PeterJuhasz.Text.Yaml.Serialization.YamlSerializerContext
			{
				public AppContext() : base(null) { }
				public override PeterJuhasz.Text.Yaml.Serialization.Metadata.YamlTypeInfo? GetTypeInfo(System.Type type) => null;
			}
			""").Diagnostics);

		Assert.AreEqual("YAML1001", diagnostic.Id);
	}

	[TestMethod]
	public void NotContext()
	{
		var diagnostic = Assert.ContainsSingle(Run("""
			[PeterJuhasz.Text.Yaml.Serialization.YamlSerializable(typeof(int))]
			internal sealed partial class AppContext;
			""").Diagnostics);

		Assert.AreEqual("YAML1002", diagnostic.Id);
	}

	[TestMethod]
	public void GenericContext()
	{
		var diagnostic = Assert.ContainsSingle(Run("""
			[PeterJuhasz.Text.Yaml.Serialization.YamlSerializable(typeof(int))]
			internal abstract partial class AppContext<T>() : PeterJuhasz.Text.Yaml.Serialization.YamlSerializerContext(null);
			""").Diagnostics);

		Assert.AreEqual("YAML1007", diagnostic.Id);
	}

	[TestMethod]
	public void UnsupportedType()
	{
		var (output, diagnostics) = Run("""
			public sealed class Ambiguous
			{
				public Ambiguous(int a) { }
				public Ambiguous(string b) { }
			}

			[PeterJuhasz.Text.Yaml.Serialization.YamlSerializable(typeof(Ambiguous))]
			internal sealed partial class AppContext : PeterJuhasz.Text.Yaml.Serialization.YamlSerializerContext;
			""");

		var diagnostic = Assert.ContainsSingle(diagnostics);
		Assert.AreEqual("YAML1003", diagnostic.Id);
		Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
		Assert.Contains("multiple public constructors", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
		Assert.AreEqual(1, diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1 - 6);
		Assert.IsEmpty(output.GetDiagnostics(TestContext.CancellationToken).Where(static d => d.Severity is DiagnosticSeverity.Error));
	}

	[TestMethod]
	public void InaccessibleType()
	{
		var diagnostic = Assert.ContainsSingle(Run("""
			public sealed class Holder
			{
				private sealed class Hidden;

				public static System.Type Type => typeof(Hidden);
			}

			public sealed class Model
			{
				public System.Span<int> Span => default;
			}

			[PeterJuhasz.Text.Yaml.Serialization.YamlSerializable(typeof(System.Action))]
			internal sealed partial class AppContext : PeterJuhasz.Text.Yaml.Serialization.YamlSerializerContext;
			""").Diagnostics);

		Assert.AreEqual("YAML1004", diagnostic.Id);
		Assert.Contains("Delegates", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
	}

	[TestMethod]
	public void InitOnlyPropertyOfGenericType()
	{
		var (output, diagnostics) = Run("""
			public sealed class Box<T>
			{
				public T? Value { get; init; }
			}

			[PeterJuhasz.Text.Yaml.Serialization.YamlSerializable(typeof(Box<int>))]
			internal sealed partial class AppContext : PeterJuhasz.Text.Yaml.Serialization.YamlSerializerContext;
			""");

		var diagnostic = Assert.ContainsSingle(diagnostics);
		Assert.AreEqual("YAML1004", diagnostic.Id);
		Assert.Contains("init only property 'Value'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
		Assert.IsEmpty(output.GetDiagnostics(TestContext.CancellationToken).Where(static d => d.Severity is DiagnosticSeverity.Error));
	}

	[TestMethod]
	public void InvalidPolymorphism()
	{
		var diagnostics = Run("""
			using PeterJuhasz.Text.Yaml.Serialization;

			[YamlDerivedType(typeof(Cat), "a")]
			[YamlDerivedType(typeof(Dog), "a")]
			[YamlDerivedType(typeof(string), "b")]
			public abstract class Animal;

			public sealed class Cat : Animal;

			public sealed class Dog : Animal
			{
				[YamlPropertyName("$type")]
				public string? Type { get; set; }
			}

			[YamlSerializable(typeof(Animal))]
			internal sealed partial class AppContext : YamlSerializerContext;
			""").Diagnostics;

		Assert.IsTrue(diagnostics.All(static d => d.Id is "YAML1005"), String.Join("\n", diagnostics));
		var messages = diagnostics.Select(static d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)).ToList();
		Assert.HasCount(4, messages);
		Assert.Contains(static m => m.Contains("'a' is used multiple times", StringComparison.Ordinal), messages);
		Assert.Contains(static m => m.Contains("same name as the type discriminator", StringComparison.Ordinal), messages);
		Assert.Contains(static m => m.Contains("'string' is not derived", StringComparison.Ordinal), messages);
		Assert.Contains(static m => m.Contains("must be an object", StringComparison.Ordinal), messages);
	}

	[TestMethod]
	public void DuplicateExplicitNames()
	{
		var diagnostic = Assert.ContainsSingle(Run("""
			using PeterJuhasz.Text.Yaml.Serialization;

			public sealed class Model
			{
				[YamlPropertyName("key")]
				public string? A { get; set; }

				[YamlPropertyName("key")]
				public string? B { get; set; }
			}

			[YamlSerializable(typeof(Model))]
			internal sealed partial class AppContext : YamlSerializerContext;
			""").Diagnostics);

		Assert.AreEqual("YAML1005", diagnostic.Id);
	}

	[TestMethod]
	public void DuplicateTypeInfoPropertyName()
	{
		var diagnostic = Assert.ContainsSingle(Run("""
			using PeterJuhasz.Text.Yaml.Serialization;

			[YamlSerializable(typeof(int), TypeInfoPropertyName = "Same")]
			[YamlSerializable(typeof(string), TypeInfoPropertyName = "Same")]
			internal sealed partial class AppContext : YamlSerializerContext;
			""").Diagnostics);

		Assert.AreEqual("YAML1006", diagnostic.Id);
	}

	[TestMethod]
	public void CollidingNamesAreQualified()
	{
		var (output, diagnostics) = Run("""
			using PeterJuhasz.Text.Yaml.Serialization;

			namespace A { public sealed class Server { public string? Host { get; set; } } }

			namespace B
			{
				public static class Nested
				{
					public sealed class Server { public int Port { get; set; } }
				}
			}

			[YamlSerializable(typeof(A.Server))]
			[YamlSerializable(typeof(B.Nested.Server))]
			[YamlSerializable(typeof(System.Collections.Generic.List<A.Server>))]
			internal sealed partial class AppContext : YamlSerializerContext;
			""");

		Assert.IsEmpty(diagnostics);
		var context = output.GetTypeByMetadataName("AppContext")!;
		// only the colliding types are qualified, the first one by its containing type, then by its namespace
		Assert.IsNotEmpty(context.GetMembers("Server"));
		Assert.IsNotEmpty(context.GetMembers("NestedServer"));
		Assert.IsNotEmpty(context.GetMembers("ListServer"));
	}

	[TestMethod]
	public void DeclaredConstructorsAreKept()
	{
		var (output, diagnostics) = Run("""
			using PeterJuhasz.Text.Yaml.Serialization;

			[YamlSerializable(typeof(int))]
			internal sealed partial class AppContext : YamlSerializerContext
			{
				public AppContext(YamlSerializerOptions options) : base(options) { }
			}
			""");

		Assert.IsEmpty(diagnostics);
		var context = output.GetTypeByMetadataName("AppContext")!;
		Assert.ContainsSingle(context.InstanceConstructors);
		Assert.IsEmpty(context.GetMembers("Default"));
		Assert.IsEmpty(output.GetDiagnostics(TestContext.CancellationToken).Where(static d => d.Severity is DiagnosticSeverity.Error));
	}

	[TestMethod]
	public void Cached()
	{
		var compilation = CreateCompilation("""
			[PeterJuhasz.Text.Yaml.Serialization.YamlSerializable(typeof(int))]
			internal sealed partial class AppContext : PeterJuhasz.Text.Yaml.Serialization.YamlSerializerContext;
			""");
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new YamlSourceGenerator().AsSourceGenerator()], driverOptions: new(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
		driver = driver.RunGenerators(compilation, TestContext.CancellationToken);

		// an unrelated change does not regenerate the source
		driver = driver.RunGenerators(compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("internal sealed class Unrelated;", cancellationToken: TestContext.CancellationToken)), TestContext.CancellationToken);
		var outputs = driver.GetRunResult().Results[0].TrackedOutputSteps.SelectMany(static s => s.Value).SelectMany(static s => s.Outputs).ToList();
		Assert.IsNotEmpty(outputs);
		Assert.IsTrue(outputs.All(static o => o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged), String.Join(", ", outputs.Select(static o => o.Reason)));
	}

	private (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) Run(string source)
	{
		var driver = CSharpGeneratorDriver.Create(new YamlSourceGenerator());
		driver.RunGeneratorsAndUpdateCompilation(CreateCompilation(source), out var output, out var diagnostics, TestContext.CancellationToken);
		return (output, diagnostics);
	}

	private CSharpCompilation CreateCompilation(string source) => CSharpCompilation.Create(
		"Sample",
		[CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), cancellationToken: TestContext.CancellationToken)],
		References,
		new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
	);
}
