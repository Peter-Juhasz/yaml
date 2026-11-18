using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PeterJuhasz.Text.Yaml.SourceGeneration;

// generates the metadata of the types listed by [YamlSerializable] on partial serializer contexts
[Generator(LanguageNames.CSharp)]
public sealed class YamlSourceGenerator : IIncrementalGenerator
{
	private const string SerializableAttribute = "PeterJuhasz.Text.Yaml.Serialization.YamlSerializableAttribute";

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var results = context.SyntaxProvider.ForAttributeWithMetadataName(
			SerializableAttribute,
			predicate: static (node, _) => node is ClassDeclarationSyntax,
			transform: static (context, cancellationToken) => Generate(context, cancellationToken)
		);

		// a context declared in multiple partial declarations with attributes is found once for each of them
		context.RegisterSourceOutput(results.Collect(), static (context, results) =>
		{
			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (var result in results)
			{
				if (!seen.Add(result.HintName))
				{
					continue;
				}

				foreach (var diagnostic in result.Diagnostics)
				{
					context.ReportDiagnostic(diagnostic.ToDiagnostic());
				}

				if (result.Source is not null)
				{
					context.AddSource(result.HintName, result.Source);
				}
			}
		});
	}

	private static GenerationResult Generate(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
	{
		var symbol = (INamedTypeSymbol)context.TargetSymbol;
		var hintName = GetHintName(symbol);
		var location = symbol.Locations.FirstOrDefault();
		var known = new KnownSymbols(context.SemanticModel.Compilation);
		var diagnostics = new List<DiagnosticInfo>();
		if (known.Context is null || known.Serializable is null)
		{
			return new(HintName: hintName, Source: null, Diagnostics: EquatableArray<DiagnosticInfo>.Empty);
		}

		if (!DerivesFrom(symbol, known.Context))
		{
			diagnostics.Add(DiagnosticInfo.Create(Descriptors.NotContext, location, symbol.ToDisplayString()));
			return new(HintName: hintName, Source: null, Diagnostics: new([.. diagnostics]));
		}

		for (var current = symbol; current is not null; current = current.ContainingType)
		{
			if (current.IsGenericType)
			{
				diagnostics.Add(DiagnosticInfo.Create(Descriptors.GenericContext, location, symbol.ToDisplayString()));
				return new(HintName: hintName, Source: null, Diagnostics: new([.. diagnostics]));
			}

			if (!IsPartial(current, cancellationToken))
			{
				diagnostics.Add(DiagnosticInfo.Create(Descriptors.NotPartial, location, symbol.ToDisplayString()));
				return new(HintName: hintName, Source: null, Diagnostics: new([.. diagnostics]));
			}
		}

		var parser = new ContextParser(known, symbol, diagnostics);
		foreach (var attribute in symbol.GetAttributes())
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!ContextParser.Equal(attribute.AttributeClass, known.Serializable) || attribute.ConstructorArguments.Length is not 1 || attribute.ConstructorArguments[0].Value is not ITypeSymbol type)
			{
				continue;
			}

			var attributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ?? location;
			if (parser.GetOrAdd(type, attributeLocation) is { } spec && attribute.NamedArguments.FirstOrDefault(static a => a.Key == "TypeInfoPropertyName").Value.Value is string name)
			{
				spec.ExplicitPropertyName = name;
			}
		}

		Naming.Assign(symbol, parser.Types, diagnostics);

		// constructors and the default instance are generated, unless they are declared
		var hasConstructors = symbol.InstanceConstructors.Any(static c => !c.IsImplicitlyDeclared);
		var hasParameterlessConstructor = symbol.InstanceConstructors.Any(static c => !c.IsImplicitlyDeclared && c.Parameters.IsEmpty);
		var generateDefault = !symbol.IsAbstract && (!hasConstructors || hasParameterlessConstructor) && symbol.GetMembers("Default").IsEmpty;
		var source = Emitter.Emit(symbol, parser, generateConstructors: !hasConstructors, generateDefault);
		return new(HintName: hintName, Source: source, Diagnostics: new([.. diagnostics]));
	}

	private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
	{
		for (var current = type.BaseType; current is not null; current = current.BaseType)
		{
			if (ContextParser.Equal(current, baseType))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsPartial(INamedTypeSymbol type, CancellationToken cancellationToken) => type.DeclaringSyntaxReferences.All(r => r.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

	private static string GetHintName(INamedTypeSymbol symbol)
	{
		var builder = new StringBuilder();
		foreach (var c in symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""))
		{
			builder.Append(Char.IsLetterOrDigit(c) || c is '.' or '_' ? c : '_');
		}
		return builder.Append(".Yaml.g.cs").ToString();
	}
}
