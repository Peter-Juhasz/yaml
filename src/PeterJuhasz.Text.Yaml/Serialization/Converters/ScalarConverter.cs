using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal sealed class ScalarConverter(Type type, IScalarFormat format) : YamlConverter(type)
{
	public static ScalarConverter? TryCreate(Type type)
	{
		if (ScalarFormats.Known.TryGetValue(type, out var known))
		{
			return new(type, known);
		}

		// any other type, which can be parsed from a string
		if (type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IParsable<>) && i.GetGenericArguments()[0] == type))
		{
			var method = typeof(ScalarFormats).GetMethod(nameof(ScalarFormats.Parsable), BindingFlags.Public | BindingFlags.Static)!.MakeGenericMethod(type);
			return new(type, (IScalarFormat)method.Invoke(obj: null, parameters: null)!);
		}

		return null;
	}

	public override object? Read(Node node, YamlSerializerOptions options) => node is ScalarNode scalar && format.TryParse(scalar, out var value) ? value : throw Mismatch(node);

	public override void Write(IYamlEmitter emitter, object value, YamlSerializerOptions options, int depth) => format.Write(emitter, value);
}
