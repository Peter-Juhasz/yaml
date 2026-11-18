using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace PeterJuhasz.Text.Yaml.SourceGeneration;

internal enum SpecKind
{
	Untyped,
	Node,
	Nullable,
	Scalar,
	Parsable,
	Enum,
	Array,
	Collection,
	Dictionary,
	Object,
	Polymorphic,
	Unsupported,
}

// the metadata of a type, which is emitted as a property of the context, the core of a polymorphic type is emitted as a private one
internal sealed class TypeSpec(ITypeSymbol type, string fullName)
{
	public ITypeSymbol Type { get; } = type;

	// the fully qualified name, used in the generated code
	public string FullName { get; } = fullName;

	public SpecKind Kind { get; set; }

	public string? ExplicitPropertyName { get; set; }

	public string PropertyName { get; set; } = "";

	public bool IsCore { get; set; }

	// the expression, which returns the metadata in the generated code
	public string Accessor => IsCore ? $"this.__Core_{PropertyName}" : $"this.{PropertyName}";

	// the underlying type of nullables, the element of arrays and collections, the value of dictionaries
	public TypeSpec? Element { get; set; }

	public TypeSpec? Key { get; set; }

	public CollectionSpec? Collection { get; set; }

	public List<EnumMemberSpec>? EnumMembers { get; set; }

	public bool IsFlags { get; set; }

	public ObjectSpec? Object { get; set; }

	public PolymorphicSpec? Polymorphic { get; set; }

	// the metadata of polymorphic types without considering polymorphism, used as the fallback and as derived types
	public TypeSpec? Core { get; set; }

	public string? UnsupportedReason { get; set; }
}

// the builder is created and populated, then converted to the collection
internal sealed record class CollectionSpec(string BuilderType, string Create, string Add, string Convert, bool IsStack);

internal sealed record class EnumMemberSpec(string Name, string? ExplicitName, string Value);

internal sealed class ObjectSpec
{
	public List<PropertySpec> Properties { get; } = [];

	// null, when the object is created by the parameterless constructor
	public List<ParameterSpec>? Parameters { get; set; }

	public string Create { get; set; } = "";

	public bool SetsRequiredMembers { get; set; }
}

internal sealed class PropertySpec
{
	public required string Name { get; init; }

	public required string? ExplicitName { get; init; }

	public required TypeSpec Type { get; init; }

	public required string? Getter { get; init; }

	public required string? Setter { get; init; }

	public required bool IsRequired { get; init; }

	public required int WriteOrder { get; init; }
}

internal sealed class ParameterSpec
{
	public required string Name { get; init; }

	public required string? ExplicitName { get; init; }

	public required string? BoundProperty { get; init; }

	public required TypeSpec Type { get; init; }

	public required bool IsOptional { get; init; }

	public required string DefaultValue { get; init; }
}

internal sealed class PolymorphicSpec
{
	public required string DiscriminatorName { get; init; }

	public required bool CaseInsensitive { get; init; }

	public required int UnknownDerivedTypeHandling { get; init; }

	public List<DerivedTypeSpec> DerivedTypes { get; } = [];

	public TypeSpec? Fallback { get; set; }
}

internal sealed record class DerivedTypeSpec(TypeSpec Type, string Discriminator, IReadOnlyList<string> Aliases);

