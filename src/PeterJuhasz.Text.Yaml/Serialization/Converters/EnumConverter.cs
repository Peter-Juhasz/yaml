using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Serialization.Converters;

// the members of the enum are read by reflection, the conversions are done by the table
[RequiresUnreferencedCode(YamlSerializer.UnreferencedCodeMessage)]
[RequiresDynamicCode(YamlSerializer.DynamicCodeMessage)]
internal sealed class EnumConverter : YamlConverter
{
	private readonly EnumTable table;

	public EnumConverter(Type type, YamlNamingPolicy? namingPolicy) : base(type)
	{
		var members = type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(static f => new EnumMemberEntry(Name: f.Name, ExplicitName: f.GetCustomAttribute<EnumMemberAttribute>()?.Value, Value: ToUInt64(f.GetValue(null)!))).ToArray();
		var isSigned = Type.GetTypeCode(type) is TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64;
		table = new(members, isFlags: type.IsDefined(typeof(FlagsAttribute), inherit: false), isSigned, namingPolicy);
	}

	public override void Write(IYamlEmitter emitter, object value, YamlSerializerOptions options, int depth) => table.Write(emitter, ToUInt64(value));

	public override object? Read(Node node, YamlSerializerOptions options) => node is ScalarNode scalar && table.TryRead(scalar, out var value) ? Enum.ToObject(Type, value) : throw Mismatch(node);

	// the bits of the value are kept, so negative values of signed enums round trip
	private static ulong ToUInt64(object value) => Type.GetTypeCode(value.GetType()) switch
	{
		TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture)),
		_ => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
	};
}
