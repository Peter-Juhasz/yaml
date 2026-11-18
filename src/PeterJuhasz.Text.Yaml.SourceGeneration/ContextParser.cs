using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace PeterJuhasz.Text.Yaml.SourceGeneration;

// discovers the types reachable from the listed ones, and classifies them by the same rules as the reflection based serializer
internal sealed class ContextParser(KnownSymbols known, INamedTypeSymbol context, List<DiagnosticInfo> diagnostics)
{
	public static SymbolDisplayFormat Format { get; } = SymbolDisplayFormat.FullyQualifiedFormat;

	private readonly Dictionary<string, TypeSpec> specs = new(StringComparer.Ordinal);
	private readonly Compilation compilation = known.Compilation;
	private readonly HashSet<DiagnosticInfo> reported = [];
	private int accessorCount;

	// the types in the order of their discovery, which is the order of the generated members
	public List<TypeSpec> Types { get; } = [];

	public List<string> Accessors { get; } = [];

	// returns null, when the type cannot be referenced by the generated code
	public TypeSpec? GetOrAdd(ITypeSymbol type, Location? location)
	{
		type = Normalize(type);
		if (!IsNameable(type))
		{
			Report(Descriptors.NotGeneratable, location, Describe(type), "the type cannot be referenced by the generated code");
			return null;
		}

		var fullName = type.ToDisplayString(Format);
		if (specs.TryGetValue(fullName, out var existing))
		{
			return existing;
		}

		var spec = new TypeSpec(type, fullName);
		specs.Add(fullName, spec);
		Types.Add(spec);
		Classify(spec, ignorePolymorphism: false, location);
		return spec;
	}

	// the metadata of the type without considering polymorphism
	private TypeSpec GetCore(TypeSpec spec, Location? location)
	{
		if (spec.Kind is not SpecKind.Polymorphic)
		{
			return spec;
		}

		if (spec.Core is null)
		{
			spec.Core = new(spec.Type, spec.FullName) { IsCore = true };
			Classify(spec.Core, ignorePolymorphism: true, location);
		}

		return spec.Core;
	}

	private ITypeSymbol Normalize(ITypeSymbol type) => type switch
	{
		IDynamicTypeSymbol => compilation.GetSpecialType(SpecialType.System_Object),
		INamedTypeSymbol { IsTupleType: true, TupleUnderlyingType: { } underlying } => underlying,
		_ => type,
	};

	// the type can be used as a type argument in the generated code
	private bool IsNameable(ITypeSymbol type) => type switch
	{
		IArrayTypeSymbol array => IsNameable(array.ElementType),
		INamedTypeSymbol named => named is { IsStatic: false, IsRefLikeType: false, SpecialType: not SpecialType.System_Void } && IsReferenceable(named),
		_ => false,
	};

	private bool IsReferenceable(INamedTypeSymbol type) => type is { TypeKind: not Microsoft.CodeAnalysis.TypeKind.Error, IsAnonymousType: false, IsUnboundGenericType: false }
		&& compilation.IsSymbolAccessibleWithin(type, context)
		&& type.TypeArguments.All(IsNameable)
		&& (type.ContainingType is null || IsReferenceable(type.ContainingType));

	// the order follows the creation of converters by the reflection based serializer
	private void Classify(TypeSpec spec, bool ignorePolymorphism, Location? location)
	{
		var type = spec.Type;
		if (type.SpecialType is SpecialType.System_Object)
		{
			spec.Kind = SpecKind.Untyped;
			return;
		}

		if (IsNode(type))
		{
			spec.Kind = SpecKind.Node;
			return;
		}

		if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
		{
			spec.Kind = SpecKind.Nullable;
			spec.Element = GetOrAdd(nullable.TypeArguments[0], location);
			return;
		}

		if (type.TypeKind is Microsoft.CodeAnalysis.TypeKind.Delegate)
		{
			Unsupported(spec, location, "Delegates are not supported by source generation.", generation: true);
			return;
		}

		if (!ignorePolymorphism && GetAttributes(type, known.DerivedType).Any())
		{
			ClassifyPolymorphic(spec, location);
			return;
		}

		if (type is INamedTypeSymbol scalar && KnownSymbols.Scalars.Contains(GetMetadataName(scalar)))
		{
			spec.Kind = SpecKind.Scalar;
			return;
		}

		if (type.AllInterfaces.Any(i => Equal(i.OriginalDefinition, known.Parsable) && Equal(i.TypeArguments[0], type)))
		{
			spec.Kind = SpecKind.Parsable;
			return;
		}

		if (type.TypeKind is Microsoft.CodeAnalysis.TypeKind.Enum)
		{
			ClassifyEnum(spec);
			return;
		}

		if (type is IArrayTypeSymbol array)
		{
			if (!array.IsSZArray)
			{
				Unsupported(spec, location, "Multi-dimensional arrays are not supported.");
				return;
			}

			spec.Kind = SpecKind.Array;
			spec.Element = GetOrAdd(array.ElementType, location);
			return;
		}

		if (type is not INamedTypeSymbol named)
		{
			Unsupported(spec, location, "The type is not supported by source generation.", generation: true);
			return;
		}

		if (TryClassifyImmutable(spec, named, location))
		{
			return;
		}

		// abstract collection types are created as their default implementations
		if (named is { TypeKind: Microsoft.CodeAnalysis.TypeKind.Interface, IsGenericType: true })
		{
			var definition = named.OriginalDefinition;
			if (known.DictionaryInterfaces.Contains(definition))
			{
				SetDictionary(spec, named.TypeArguments[0], named.TypeArguments[1], location, builder: null, add: null, convert: null);
				return;
			}

			if (known.ListInterfaces.Contains(definition))
			{
				SetCollection(spec, named.TypeArguments[0], location, builder: List(named.TypeArguments[0]), add: null, convert: null);
				return;
			}

			if (known.SetInterfaces.Contains(definition))
			{
				SetCollection(spec, named.TypeArguments[0], location, builder: $"global::System.Collections.Generic.HashSet<{Display(named.TypeArguments[0])}>", add: null, convert: null);
				return;
			}
		}

		if (named.TypeKind is Microsoft.CodeAnalysis.TypeKind.Interface || named.IsAbstract)
		{
			Unsupported(spec, location, "Interfaces and abstract types are not supported.");
			return;
		}

		// collections are populated after they are created
		var hasParameterlessConstructor = named.IsValueType || GetPublicParameterlessConstructor(named) is not null;
		// the members of the interfaces may be implemented explicitly
		if (hasParameterlessConstructor && FindInterface(named, known.Dictionary) is { } dictionary)
		{
			var (key, value) = (Display(dictionary.TypeArguments[0]), Display(dictionary.TypeArguments[1]));
			SetDictionary(spec, dictionary.TypeArguments[0], dictionary.TypeArguments[1], location, builder: spec.FullName, add: $"static (builder, key, value) => ((global::System.Collections.Generic.IDictionary<{key}, {value}>)builder)[key] = value", convert: null);
			return;
		}

		if (hasParameterlessConstructor && FindInterface(named, known.Collection) is { } collection)
		{
			SetCollection(spec, collection.TypeArguments[0], location, builder: spec.FullName, add: $"static (builder, item) => ((global::System.Collections.Generic.ICollection<{Display(collection.TypeArguments[0])}>)builder).Add(item)", convert: null);
			return;
		}

		if (TryClassifyFromConstructor(spec, named, location))
		{
			return;
		}

		ClassifyObject(spec, named, location);
	}

	private bool IsNode(ITypeSymbol type)
	{
		for (var current = type; current is not null; current = current.BaseType)
		{
			if (Equal(current, known.Node))
			{
				return true;
			}
		}
		return false;
	}

	private bool IsStack(INamedTypeSymbol type) => type.IsGenericType && known.Stacks.Contains(type.OriginalDefinition);

	private bool TryClassifyImmutable(TypeSpec spec, INamedTypeSymbol type, Location? location)
	{
		if (!type.IsGenericType)
		{
			return false;
		}

		var definition = type.OriginalDefinition;
		if (known.ImmutableSequences.TryGetValue(definition, out var sequenceFactory))
		{
			var element = Display(type.TypeArguments[0]);
			SetCollection(spec, type.TypeArguments[0], location, builder: List(type.TypeArguments[0]), add: null, convert: $"static builder => {sequenceFactory}<{element}>(builder)");
			return true;
		}

		if (known.ImmutableDictionaries.TryGetValue(definition, out var dictionaryFactory))
		{
			var (key, value) = (Display(type.TypeArguments[0]), Display(type.TypeArguments[1]));
			SetDictionary(spec, type.TypeArguments[0], type.TypeArguments[1], location, builder: null, add: null, convert: $"static builder => {dictionaryFactory}<{key}, {value}>(builder)");
			return true;
		}

		return false;
	}

	// the builder is created and populated, then converted to the collection, it is the collection itself by default
	private void SetCollection(TypeSpec spec, ITypeSymbol elementType, Location? location, string builder, string? add, string? convert)
	{
		spec.Kind = SpecKind.Collection;
		spec.Element = GetOrAdd(elementType, location);
		spec.Collection = new(
			BuilderType: builder,
			Create: $"static () => new {builder}()",
			Add: add ?? "static (builder, item) => builder.Add(item)",
			Convert: convert ?? "static builder => builder",
			IsStack: spec.Type is INamedTypeSymbol named && IsStack(named)
		);
	}

	// the builder is a dictionary by default
	private void SetDictionary(TypeSpec spec, ITypeSymbol keyType, ITypeSymbol valueType, Location? location, string? builder, string? add, string? convert)
	{
		spec.Kind = SpecKind.Dictionary;
		spec.Key = GetOrAdd(keyType, location);
		spec.Element = GetOrAdd(valueType, location);
		builder ??= $"global::System.Collections.Generic.Dictionary<{Display(keyType)}, {Display(valueType)}>";
		spec.Collection = new(
			BuilderType: builder,
			Create: $"static () => new {builder}()",
			Add: add ?? "static (builder, key, value) => builder[key] = value",
			Convert: convert ?? "static builder => builder",
			IsStack: false
		);
	}

	private static string List(ITypeSymbol elementType) => $"global::System.Collections.Generic.List<{Display(elementType)}>";

	// any other collection, which accepts the filled list or dictionary in its constructor, like queues, stacks and read-only wrappers
	private bool TryClassifyFromConstructor(TypeSpec spec, INamedTypeSymbol type, Location? location)
	{
		if ((FindInterface(type, known.Dictionary) ?? FindInterface(type, known.ReadOnlyDictionary)) is { } dictionary && known.DictionaryType is not null)
		{
			var builder = known.DictionaryType.Construct(dictionary.TypeArguments[0], dictionary.TypeArguments[1]);
			if (FindConstructor(type, builder) is { } parameterType)
			{
				SetDictionary(spec, dictionary.TypeArguments[0], dictionary.TypeArguments[1], location, builder: builder.ToDisplayString(Format), add: null, convert: $"static builder => new {spec.FullName}(({parameterType})builder)");
				return true;
			}
		}

		if (FindInterface(type, known.Enumerable) is { } enumerable && known.ListType is not null)
		{
			var builder = known.ListType.Construct(enumerable.TypeArguments[0]);
			if (FindConstructor(type, builder) is { } parameterType)
			{
				SetCollection(spec, enumerable.TypeArguments[0], location, builder: builder.ToDisplayString(Format), add: null, convert: $"static builder => new {spec.FullName}(({parameterType})builder)");
				return true;
			}
		}

		return false;
	}

	// returns the type of the single parameter of the first public constructor, which accepts the builder
	private string? FindConstructor(INamedTypeSymbol type, INamedTypeSymbol builder)
	{
		foreach (var constructor in type.InstanceConstructors)
		{
			if (constructor.DeclaredAccessibility is Accessibility.Public && constructor.Parameters.Length is 1 && constructor.Parameters[0].RefKind is RefKind.None && IsAssignable(builder, constructor.Parameters[0].Type))
			{
				return Display(constructor.Parameters[0].Type);
			}
		}

		return null;
	}

	private bool IsAssignable(ITypeSymbol source, ITypeSymbol destination)
	{
		// like the assignability of reflection, which includes boxing of structs to their interfaces
		var conversion = compilation.ClassifyConversion(source, destination);
		return conversion.IsIdentity || (conversion.IsImplicit && (conversion.IsReference || conversion.IsBoxing));
	}

	private static INamedTypeSymbol? FindInterface(INamedTypeSymbol type, INamedTypeSymbol? definition)
	{
		if (definition is null)
		{
			return null;
		}

		if (type.IsGenericType && Equal(type.OriginalDefinition, definition))
		{
			return type;
		}

		return type.AllInterfaces.FirstOrDefault(i => Equal(i.OriginalDefinition, definition));
	}

	private void ClassifyEnum(TypeSpec spec)
	{
		spec.Kind = SpecKind.Enum;
		spec.IsFlags = GetAttributes(spec.Type, known.Flags).Any();
		spec.EnumMembers = [];
		foreach (var field in spec.Type.GetMembers().OfType<IFieldSymbol>())
		{
			if (field is not { IsConst: true, IsStatic: true, DeclaredAccessibility: Accessibility.Public })
			{
				continue;
			}

			var explicitName = GetAttributes(field, known.EnumMember).Select(static a => GetNamedArgument(a, "Value") as string).FirstOrDefault();
			spec.EnumMembers.Add(new(Name: field.Name, ExplicitName: explicitName, Value: $"{spec.FullName}.{Identifier(field.Name)}"));
		}
	}

	private void ClassifyPolymorphic(TypeSpec spec, Location? location)
	{
		var type = spec.Type;
		var polymorphic = GetAttributes(type, known.Polymorphic).FirstOrDefault();
		var discriminatorName = polymorphic is null ? null : GetNamedArgument(polymorphic, "TypeDiscriminatorPropertyName") as string;
		var caseInsensitive = polymorphic is not null && GetNamedArgument(polymorphic, "TypeDiscriminatorCaseInsensitive") is true;
		var unknownDerivedTypeHandling = polymorphic is not null && GetNamedArgument(polymorphic, "UnknownDerivedTypeHandling") is int handling ? handling : 0;

		spec.Kind = SpecKind.Polymorphic;
		spec.Polymorphic = new()
		{
			DiscriminatorName = discriminatorName ?? "$type",
			CaseInsensitive = caseInsensitive,
			UnknownDerivedTypeHandling = unknownDerivedTypeHandling,
		};

		// the configuration is validated when the type is used, just like by the reflection based serializer, the errors are reported early as warnings
		var comparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		var discriminators = new HashSet<string>(comparer);
		var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
		var problems = new List<string>();
		foreach (var attribute in GetAttributes(type, known.DerivedType))
		{
			var arguments = attribute.ConstructorArguments;
			if (arguments.Length < 2 || arguments[0].Value is not ITypeSymbol derivedType || ToDiscriminator(arguments[1]) is not { } discriminator)
			{
				continue;
			}

			var attributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? location;
			var aliases = arguments.Length > 2 && !arguments[2].IsNull ? arguments[2].Values.Select(ToDiscriminator).Where(static a => a is not null).Select(static a => a!).ToArray() : [];
			if (GetOrAdd(derivedType, attributeLocation) is not { } derivedSpec)
			{
				continue;
			}

			if (!IsAssignable(derivedType, type))
			{
				problems.Add($"the type '{Describe(derivedType)}' is not derived from it");
			}

			if (!seen.Add(derivedType))
			{
				problems.Add($"the derived type '{Describe(derivedType)}' is listed multiple times");
			}

			var core = GetCore(derivedSpec, attributeLocation);
			if (core.Kind is not (SpecKind.Object or SpecKind.Unsupported))
			{
				problems.Add($"the derived type '{Describe(derivedType)}' must be an object, which is deserialized from a mapping");
			}
			else if (core.Object is { } derivedObject && GetExplicitNames(derivedObject).Contains(spec.Polymorphic.DiscriminatorName))
			{
				problems.Add($"a member of the derived type '{Describe(derivedType)}' has the same name as the type discriminator '{spec.Polymorphic.DiscriminatorName}'");
			}

			foreach (var value in new[] { discriminator }.Concat(aliases))
			{
				if (!discriminators.Add(value))
				{
					problems.Add($"the type discriminator '{value}' is used multiple times");
				}
			}

			spec.Polymorphic.DerivedTypes.Add(new(Type: core, Discriminator: discriminator, Aliases: aliases));
		}

		// mappings without a discriminator are deserialized as the base type, when it can be created
		if (type is { IsAbstract: false, TypeKind: not Microsoft.CodeAnalysis.TypeKind.Interface })
		{
			spec.Polymorphic.Fallback = GetCore(spec, location);
		}
		else if (unknownDerivedTypeHandling is not 0)
		{
			problems.Add("it cannot fall back to itself for unknown derived types, because it is abstract");
		}

		foreach (var problem in problems)
		{
			Report(Descriptors.InvalidConfiguration, location, Describe(type), problem);
		}
	}

	private static string? ToDiscriminator(TypedConstant constant) => constant.Value switch
	{
		int integer => integer.ToString(CultureInfo.InvariantCulture),
		string text => text,
		_ => null,
	};

	// returns no constructor, when the instance is created by the parameterless one and then populated
	private (IMethodSymbol? Constructor, string? Error) SelectConstructor(INamedTypeSymbol type)
	{
		// the implicit parameterless constructors of structs do not exist at runtime
		var constructors = type.InstanceConstructors.Where(c => !(type.IsValueType && c.IsImplicitlyDeclared && c.Parameters.IsEmpty)).ToList();
		var marked = constructors.Where(c => GetAttributes(c, known.Constructor).Any()).ToList();
		if (marked.Count is 1)
		{
			return (marked[0], null);
		}

		if (marked.Count > 1)
		{
			return (null, "Only one constructor can be marked with YamlConstructorAttribute.");
		}

		var publicConstructors = constructors.Where(static c => c.DeclaredAccessibility is Accessibility.Public).ToList();
		if ((type.IsValueType && publicConstructors.Count is 0) || publicConstructors.Any(static c => c.Parameters.IsEmpty))
		{
			return (null, null);
		}

		return publicConstructors.Count switch
		{
			1 => (publicConstructors[0], null),
			0 => (null, "The type must have a public constructor."),
			_ => (null, "The type has multiple public constructors, mark the one to use with YamlConstructorAttribute."),
		};
	}

	private static IMethodSymbol? GetPublicParameterlessConstructor(INamedTypeSymbol type) => type.InstanceConstructors.FirstOrDefault(c => c.Parameters.IsEmpty && c.DeclaredAccessibility is Accessibility.Public && !(type.IsValueType && c.IsImplicitlyDeclared));

	private sealed record class DiscoveredProperty(IPropertySymbol Property, int Depth, int Order);

	private void ClassifyObject(TypeSpec spec, INamedTypeSymbol type, Location? location)
	{
		var (constructor, error) = SelectConstructor(type);
		if (error is not null)
		{
			Unsupported(spec, location, error);
			return;
		}

		// the public properties, from the most derived type first, hidden and overridden properties of base types are skipped, like by reflection
		var discovered = new List<DiscoveredProperty>();
		var names = new HashSet<string>(StringComparer.Ordinal);
		var overridden = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);
		var depth = 0;
		for (var current = type.BaseType; current is not null; current = current.BaseType)
		{
			depth++;
		}

		for (var current = type; current is not null; current = current.BaseType, depth--)
		{
			var order = 0;
			foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
			{
				order++;
				if (overridden.Contains(property.OriginalDefinition))
				{
					continue;
				}

				for (var baseProperty = property.OverriddenProperty; baseProperty is not null; baseProperty = baseProperty.OverriddenProperty)
				{
					overridden.Add(baseProperty.OriginalDefinition);
				}

				if (property is not { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public, ExplicitInterfaceImplementations.IsEmpty: true } || HasInheritedAttribute(property, known.Ignore) || !names.Add(property.Name))
				{
					continue;
				}

				discovered.Add(new(Property: property, Depth: depth, Order: order));
			}
		}

		// parameters bind to the property of the same name, ignoring case
		var parameters = constructor?.Parameters ?? [];
		var bound = new HashSet<string>(StringComparer.Ordinal);
		var bindings = new List<DiscoveredProperty?>();
		foreach (var parameter in parameters)
		{
			var property = discovered.FirstOrDefault(p => String.Equals(p.Property.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
			bindings.Add(property);
			if (property is not null)
			{
				bound.Add(property.Property.Name);
			}
		}

		var objectSpec = new ObjectSpec();
		var generationErrors = new List<string>();

		// members are written in the order of their declaration, starting with the ones of the base types
		var writeOrders = discovered.OrderBy(static p => p.Depth).ThenBy(static p => p.Order).Select(static (p, i) => (p, i)).ToDictionary(static x => x.p, static x => x.i);
		foreach (var property in discovered)
		{
			var symbol = property.Property;
			var canSet = symbol.SetMethod is { DeclaredAccessibility: Accessibility.Public };

			// properties, which are neither set nor bound to a parameter, are not used
			if (!canSet && !bound.Contains(symbol.Name))
			{
				continue;
			}

			var propertyLocation = symbol.Locations.FirstOrDefault(static l => l.IsInSource) ?? location;
			if (symbol.RefKind is not RefKind.None)
			{
				Report(Descriptors.NotGeneratable, propertyLocation, symbol.ToDisplayString(), "properties returning references are skipped");
				continue;
			}

			if (GetOrAdd(symbol.Type, propertyLocation) is not { } propertyType)
			{
				continue;
			}

			var target = Equal(symbol.ContainingType, type) ? "obj" : $"(({Display(symbol.ContainingType)})obj)";
			var getter = symbol.GetMethod is { DeclaredAccessibility: Accessibility.Public } ? $"static obj => {target}.{Identifier(symbol.Name)}" : null;
			// properties bound to parameters are set by the constructor
			string? setter = null;
			if (canSet && !bound.Contains(symbol.Name))
			{
				if (symbol.SetMethod!.IsInitOnly)
				{
					if (IsGeneric(symbol.ContainingType))
					{
						generationErrors.Add($"the init only property '{symbol.Name}' of a generic type cannot be set");
						continue;
					}

					var name = AddAccessor($"[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = {Literal(symbol.SetMethod.MetadataName)})] private static extern void {{0}}({(type.IsValueType ? "ref " : "")}{Display(symbol.ContainingType)} obj, {propertyType.FullName} value);");
					setter = $"static (ref {spec.FullName} obj, {propertyType.FullName} value) => {name}({(type.IsValueType ? "ref " : "")}obj, value)";
				}
				else
				{
					setter = $"static (ref {spec.FullName} obj, {propertyType.FullName} value) => {target}.{Identifier(symbol.Name)} = value";
				}
			}

			objectSpec.Properties.Add(new()
			{
				Name = symbol.Name,
				ExplicitName = GetPropertyName(symbol),
				Type = propertyType,
				Getter = getter,
				Setter = setter,
				IsRequired = symbol.IsRequired,
				WriteOrder = writeOrders[property],
			});
		}

		var hasRequiredMembers = HasRequiredMembers(type);
		if (constructor is null)
		{
			var parameterless = GetPublicParameterlessConstructor(type);
			objectSpec.SetsRequiredMembers = parameterless is not null && GetAttributes(parameterless, known.SetsRequiredMembers).Any();
			if (!hasRequiredMembers || objectSpec.SetsRequiredMembers)
			{
				objectSpec.Create = $"static () => new {spec.FullName}()";
			}
			else if (type.IsValueType && parameterless is null)
			{
				objectSpec.Create = $"static () => default({spec.FullName})";
			}
			else if (IsGeneric(type))
			{
				generationErrors.Add("a generic type with required members cannot be created");
			}
			else
			{
				// required members are populated by deserialization, so the constructor is called without setting them
				var name = AddAccessor($"[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Constructor)] private static extern {spec.FullName} {{0}}();");
				objectSpec.Create = $"static () => {name}()";
			}
		}
		else
		{
			objectSpec.SetsRequiredMembers = GetAttributes(constructor, known.SetsRequiredMembers).Any();
			objectSpec.Parameters = [];
			for (var i = 0; i < parameters.Length; i++)
			{
				var parameter = parameters[i];
				var parameterLocation = parameter.Locations.FirstOrDefault(static l => l.IsInSource) ?? location;
				if (GetOrAdd(parameter.Type, parameterLocation) is not { } parameterType)
				{
					generationErrors.Add($"the type of the parameter '{parameter.Name}' cannot be referenced");
					continue;
				}

				var (isOptional, defaultValue) = GetDefault(parameter);
				objectSpec.Parameters.Add(new()
				{
					Name = String.IsNullOrEmpty(parameter.Name) ? $"arg{i}" : parameter.Name,
					ExplicitName = GetPropertyName(parameter),
					BoundProperty = bindings[i]?.Property.Name,
					Type = parameterType,
					IsOptional = isOptional,
					DefaultValue = defaultValue,
				});
			}

			// non public constructors, and the ones not setting the required members, which are populated by deserialization, are called by an accessor
			var needsAccessor = constructor.DeclaredAccessibility is not Accessibility.Public || (hasRequiredMembers && !objectSpec.SetsRequiredMembers);
			if (generationErrors.Count is 0 && needsAccessor && IsGeneric(type))
			{
				generationErrors.Add("the constructor of a generic type cannot be called, when it is not public or does not set the required members");
			}

			// tuples cannot be created by their constructors in code, the ones with more than seven elements nest the rest
			if (generationErrors.Count is 0 && type.IsTupleType && parameters.Length > 7)
			{
				generationErrors.Add("tuples with more than seven elements are not supported");
			}

			if (generationErrors.Count is 0)
			{
				var target = type.IsTupleType ? "global::System.ValueTuple.Create" : $"new {spec.FullName}";
				if (needsAccessor)
				{
					var signature = String.Join(", ", objectSpec.Parameters.Select((p, i) => $"{GetModifier(parameters[i].RefKind)}{p.Type.FullName} a{i}"));
					target = AddAccessor($"[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Constructor)] private static extern {spec.FullName} {{0}}({signature});");
				}

				var arguments = objectSpec.Parameters.Select(static (p, i) => $"({p.Type.FullName})args[{i}]").ToList();
				if (parameters.Any(static p => p.RefKind is not RefKind.None))
				{
					// arguments passed by reference are stored in locals first
					var locals = String.Join(" ", arguments.Select(static (a, i) => $"var a{i} = {a};"));
					var passed = String.Join(", ", parameters.Select(static (p, i) => $"{GetModifier(p.RefKind)}a{i}".Replace("ref readonly ", "in ")));
					objectSpec.Create = $"static args => {{ {locals} return {target}({passed}); }}";
				}
				else
				{
					objectSpec.Create = $"static args => {target}({String.Join(", ", arguments)})";
				}
			}
		}

		if (generationErrors.Count > 0)
		{
			Unsupported(spec, location, $"The type is not supported by source generation, {String.Join(", ", generationErrors)}.", generation: true);
			return;
		}

		// conflicting explicit names raise an error when the type is used, conflicts by naming policies are only known at runtime
		var explicitNames = new HashSet<string>(StringComparer.Ordinal);
		foreach (var name in GetExplicitNames(objectSpec))
		{
			if (!explicitNames.Add(name))
			{
				Report(Descriptors.InvalidConfiguration, location, Describe(type), $"multiple members have the same name '{name}'");
			}
		}

		spec.Kind = SpecKind.Object;
		spec.Object = objectSpec;
	}

	// the explicit keys of the members, which are read by deserialization
	private static List<string> GetExplicitNames(ObjectSpec spec)
	{
		var names = new List<string>();
		var bound = new HashSet<string>(StringComparer.Ordinal);
		foreach (var parameter in spec.Parameters ?? [])
		{
			var property = parameter.BoundProperty is null ? null : spec.Properties.FirstOrDefault(p => p.Name == parameter.BoundProperty);
			if (property is not null)
			{
				bound.Add(property.Name);
			}

			if ((parameter.ExplicitName ?? property?.ExplicitName) is { } name)
			{
				names.Add(name);
			}
		}

		foreach (var property in spec.Properties)
		{
			if (property.Setter is not null && !bound.Contains(property.Name) && property.ExplicitName is { } name)
			{
				names.Add(name);
			}
		}

		return names;
	}

	// a missing parameter is set to its default value, or to null when it is nullable, otherwise it is required
	private (bool IsOptional, string DefaultValue) GetDefault(IParameterSymbol parameter)
	{
		var type = parameter.Type;
		if (parameter.HasExplicitDefaultValue)
		{
			return (true, FormatDefault(type, parameter.ExplicitDefaultValue));
		}

		if (type.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T)
		{
			return (true, "default");
		}

		if (type.IsValueType)
		{
			return (false, "default");
		}

		if (GetAttributes(parameter, known.AllowNull).Any())
		{
			return (true, "default");
		}

		if (GetAttributes(parameter, known.DisallowNull).Any())
		{
			return (false, "default");
		}

		return (parameter.NullableAnnotation is not NullableAnnotation.NotAnnotated, "default");
	}

	private static string FormatDefault(ITypeSymbol type, object? value)
	{
		if (value is null)
		{
			return "default";
		}

		var target = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;
		var literal = value switch
		{
			double.NaN => "double.NaN",
			double d when double.IsPositiveInfinity(d) => "double.PositiveInfinity",
			double d when double.IsNegativeInfinity(d) => "double.NegativeInfinity",
			double d => d.ToString("R", CultureInfo.InvariantCulture) + "D",
			float.NaN => "float.NaN",
			float f when float.IsPositiveInfinity(f) => "float.PositiveInfinity",
			float f when float.IsNegativeInfinity(f) => "float.NegativeInfinity",
			float f => f.ToString("R", CultureInfo.InvariantCulture) + "F",
			decimal m => m.ToString(CultureInfo.InvariantCulture) + "M",
			DateTime date => $"new global::System.DateTime({date.Ticks.ToString(CultureInfo.InvariantCulture)}L)",
			_ => SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false),
		};

		// enums are stored as their underlying values, and numbers are converted to the type of the parameter
		return $"({Display(target)})({literal})";
	}

	private static string GetModifier(RefKind kind) => kind switch
	{
		RefKind.Ref => "ref ",
		RefKind.Out => "out ",
		RefKind.In => "in ",
		RefKind.RefReadOnlyParameter => "ref readonly ",
		_ => "",
	};

	private static bool HasRequiredMembers(INamedTypeSymbol type)
	{
		for (var current = type; current is not null; current = current.BaseType)
		{
			if (current.GetMembers().Any(static m => m is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsGeneric(INamedTypeSymbol type)
	{
		for (var current = type; current is not null; current = current.ContainingType)
		{
			if (current.IsGenericType)
			{
				return true;
			}
		}
		return false;
	}

	private string AddAccessor(string declaration)
	{
		var name = $"__Accessor{accessorCount++}";
		Accessors.Add(declaration.Replace("{0}", name));
		return name;
	}

	private string? GetPropertyName(ISymbol symbol) => symbol switch
	{
		IPropertySymbol property => GetInheritedAttribute(property, known.PropertyName),
		_ => GetAttributes(symbol, known.PropertyName).FirstOrDefault(),
	} is { ConstructorArguments.Length: 1 } attribute ? attribute.ConstructorArguments[0].Value as string : null;

	// attributes of overridden properties apply to the overriding ones, like by reflection
	private static AttributeData? GetInheritedAttribute(IPropertySymbol property, INamedTypeSymbol? attribute)
	{
		for (var current = property; current is not null; current = current.OverriddenProperty)
		{
			if (GetAttributes(current, attribute).FirstOrDefault() is { } data)
			{
				return data;
			}
		}
		return null;
	}

	private static bool HasInheritedAttribute(IPropertySymbol property, INamedTypeSymbol? attribute) => GetInheritedAttribute(property, attribute) is not null;

	private static IEnumerable<AttributeData> GetAttributes(ISymbol symbol, INamedTypeSymbol? attribute) => attribute is null ? [] : symbol.GetAttributes().Where(a => Equal(a.AttributeClass, attribute));

	private static object? GetNamedArgument(AttributeData attribute, string name) => attribute.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value;

	private void Unsupported(TypeSpec spec, Location? location, string reason, bool generation = false)
	{
		spec.Kind = SpecKind.Unsupported;
		spec.UnsupportedReason = reason;
		Report(generation ? Descriptors.NotGeneratable : Descriptors.Unsupported, location, Describe(spec.Type), reason);
	}

	// a type referenced by multiple members is reported once
	private void Report(DiagnosticDescriptor descriptor, Location? location, params string[] arguments)
	{
		var diagnostic = DiagnosticInfo.Create(descriptor, location ?? context.Locations.FirstOrDefault(), arguments);
		if (reported.Add(diagnostic))
		{
			diagnostics.Add(diagnostic);
		}
	}

	public static bool Equal(ISymbol? left, ISymbol? right) => SymbolEqualityComparer.Default.Equals(left, right);

	public static string Display(ITypeSymbol type) => type.ToDisplayString(Format);

	// the name of the type in the messages of diagnostics
	public static string Describe(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

	// reserved keywords are escaped, contextual ones can be used as member names as they are
	public static string Identifier(string name) => SyntaxFacts.GetKeywordKind(name) is SyntaxKind.None ? name : "@" + name;

	public static string Literal(string? value) => value is null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);

	private static string GetMetadataName(INamedTypeSymbol type) => type.ContainingType is null && type.ContainingNamespace is { IsGlobalNamespace: false } ns ? $"{ns.ToDisplayString()}.{type.MetadataName}" : type.MetadataName;
}
