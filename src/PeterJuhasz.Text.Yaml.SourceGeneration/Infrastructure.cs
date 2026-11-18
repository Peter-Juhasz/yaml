using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace System.Runtime.CompilerServices
{
	// records, init accessors and required members need these types, which are not part of netstandard2.0
	internal static class IsExternalInit;

	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
	internal sealed class RequiredMemberAttribute : Attribute;

	[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
	internal sealed class CompilerFeatureRequiredAttribute(string featureName) : Attribute
	{
		public string FeatureName { get; } = featureName;

		public bool IsOptional { get; init; }
	}
}

namespace System.Diagnostics.CodeAnalysis
{
	[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
	internal sealed class SetsRequiredMembersAttribute : Attribute;
}

namespace PeterJuhasz.Text.Yaml.SourceGeneration
{
	// an array compared by its items, so the results of the generator are cached between compilations
	internal readonly struct EquatableArray<T>(T[] items) : IEquatable<EquatableArray<T>>, IEnumerable<T> where T : IEquatable<T>
	{
		private readonly T[]? items = items;

		public static EquatableArray<T> Empty { get; } = new([]);

		public int Length => items?.Length ?? 0;

		public bool Equals(EquatableArray<T> other) => (items ?? []).SequenceEqual(other.items ?? []);

		public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

		public override int GetHashCode()
		{
			var hash = 17;
			foreach (var item in items ?? [])
			{
				hash = (hash * 31) + (item?.GetHashCode() ?? 0);
			}
			return hash;
		}

		public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(items ?? [])).GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	// a diagnostic without references to syntax trees, so it can be cached
	internal sealed record class DiagnosticInfo(string Id, string? FilePath, TextSpan Span, LinePositionSpan LineSpan, EquatableArray<string> Arguments)
	{
		public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location? location, params string[] arguments) => location is { IsInSource: true }
			? new(Id: descriptor.Id, FilePath: location.SourceTree?.FilePath, Span: location.SourceSpan, LineSpan: location.GetLineSpan().Span, Arguments: new(arguments))
			: new(Id: descriptor.Id, FilePath: null, Span: default, LineSpan: default, Arguments: new(arguments));

		public Diagnostic ToDiagnostic() => Diagnostic.Create(
			Descriptors.ById[Id],
			FilePath is null ? Location.None : Location.Create(FilePath, Span, LineSpan),
			[.. Arguments]
		);
	}

	internal sealed record class GenerationResult(string HintName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics);

	internal static class Descriptors
	{
		private const string Category = "PeterJuhasz.Text.Yaml.SourceGeneration";

		public static DiagnosticDescriptor NotPartial { get; } = new(
			id: "YAML1001",
			title: "Serializer contexts must be partial",
			messageFormat: "The serializer context '{0}' and its containing types must be partial, so the metadata can be generated into them",
			category: Category,
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		public static DiagnosticDescriptor NotContext { get; } = new(
			id: "YAML1002",
			title: "Serializable types must be listed on serializer contexts",
			messageFormat: "The type '{0}' must derive from YamlSerializerContext to list types with YamlSerializable",
			category: Category,
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		public static DiagnosticDescriptor Unsupported { get; } = new(
			id: "YAML1003",
			title: "Type is not supported by serialization",
			messageFormat: "The type '{0}' is not supported by serialization, it raises an error when it is used: {1}",
			category: Category,
			defaultSeverity: DiagnosticSeverity.Warning,
			isEnabledByDefault: true);

		public static DiagnosticDescriptor NotGeneratable { get; } = new(
			id: "YAML1004",
			title: "Type or member is not supported by source generation",
			messageFormat: "'{0}' is not supported by source generation: {1}",
			category: Category,
			defaultSeverity: DiagnosticSeverity.Warning,
			isEnabledByDefault: true);

		public static DiagnosticDescriptor InvalidConfiguration { get; } = new(
			id: "YAML1005",
			title: "Serialization configuration is invalid",
			messageFormat: "The serialization of type '{0}' raises an error when it is used: {1}",
			category: Category,
			defaultSeverity: DiagnosticSeverity.Warning,
			isEnabledByDefault: true);

		public static DiagnosticDescriptor DuplicatePropertyName { get; } = new(
			id: "YAML1006",
			title: "Type info property names must be unique",
			messageFormat: "The type info property name '{0}' is used by multiple types in the serializer context '{1}'",
			category: Category,
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		public static DiagnosticDescriptor GenericContext { get; } = new(
			id: "YAML1007",
			title: "Serializer contexts must not be generic",
			messageFormat: "The serializer context '{0}' and its containing types must not be generic",
			category: Category,
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		public static IReadOnlyDictionary<string, DiagnosticDescriptor> ById { get; } = new[] { NotPartial, NotContext, Unsupported, NotGeneratable, InvalidConfiguration, DuplicatePropertyName, GenericContext }.ToDictionary(static d => d.Id);
	}
}
