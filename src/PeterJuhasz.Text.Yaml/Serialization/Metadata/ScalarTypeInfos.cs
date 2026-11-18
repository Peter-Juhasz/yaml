using System.Runtime.CompilerServices;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Serialization.Converters;
using PeterJuhasz.Text.Yaml.Writing;

namespace PeterJuhasz.Text.Yaml.Serialization.Metadata;

internal sealed class ScalarTypeInfo<T>(YamlSerializerContext context, ScalarFormat<T> format) : YamlTypeInfo<T>(context)
{
	internal override T ReadCore(Node node) => node is ScalarNode scalar && format.TryParse(scalar, out var value) ? value : throw Mismatch(node);

	internal override void WriteCore(IYamlEmitter emitter, T value, int depth) => format.Write(emitter, value);
}

internal sealed class EnumTypeInfo<TEnum> : YamlTypeInfo<TEnum> where TEnum : struct, Enum
{
	private static readonly bool IsSigned = Type.GetTypeCode(typeof(TEnum)) is TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64;

	private readonly EnumTable table;

	public EnumTypeInfo(YamlSerializerContext context, ReadOnlySpan<(string Name, string? ExplicitName, TEnum Value)> members, bool isFlags) : base(context)
	{
		var entries = new EnumMemberEntry[members.Length];
		for (var i = 0; i < members.Length; i++)
		{
			var (name, explicitName, value) = members[i];
			entries[i] = new(Name: name, ExplicitName: explicitName, Value: ToUInt64(value));
		}

		table = new(entries, isFlags, IsSigned, context.Options.EnumNamingPolicy);
	}

	internal override TEnum ReadCore(Node node) => node is ScalarNode scalar && table.TryRead(scalar, out var value) ? FromUInt64(value) : throw Mismatch(node);

	internal override void WriteCore(IYamlEmitter emitter, TEnum value, int depth) => table.Write(emitter, ToUInt64(value));

	// the bits of the value are kept, so negative values of signed enums round trip
	private static ulong ToUInt64(TEnum value) => Unsafe.SizeOf<TEnum>() switch
	{
		1 when IsSigned => unchecked((ulong)Unsafe.As<TEnum, sbyte>(ref value)),
		1 => Unsafe.As<TEnum, byte>(ref value),
		2 when IsSigned => unchecked((ulong)Unsafe.As<TEnum, short>(ref value)),
		2 => Unsafe.As<TEnum, ushort>(ref value),
		4 when IsSigned => unchecked((ulong)Unsafe.As<TEnum, int>(ref value)),
		4 => Unsafe.As<TEnum, uint>(ref value),
		_ => Unsafe.As<TEnum, ulong>(ref value),
	};

	// the value is truncated to the size of the enum, like by the conversion of the enum type
	private static TEnum FromUInt64(ulong value)
	{
		switch (Unsafe.SizeOf<TEnum>())
		{
			case 1:
				var b = unchecked((byte)value);
				return Unsafe.As<byte, TEnum>(ref b);

			case 2:
				var s = unchecked((ushort)value);
				return Unsafe.As<ushort, TEnum>(ref s);

			case 4:
				var i = unchecked((uint)value);
				return Unsafe.As<uint, TEnum>(ref i);

			default:
				return Unsafe.As<ulong, TEnum>(ref value);
		}
	}
}

internal sealed class NullableTypeInfo<T>(YamlSerializerContext context, YamlTypeInfo<T> underlying) : YamlTypeInfo<T?>(context) where T : struct
{
	internal override T? ReadCore(Node node) => underlying.ReadCore(node);

	internal override void WriteCore(IYamlEmitter emitter, T? value, int depth) => underlying.WriteCore(emitter, value!.Value, depth);
}

// parts of the model are returned as is, so parts of a document can be processed manually
internal sealed class NodeTypeInfo<TNode>(YamlSerializerContext context) : YamlTypeInfo<TNode>(context) where TNode : Node
{
	internal override TNode Read(Node node) => ReadCore(node);

	internal override TNode ReadCore(Node node) => node as TNode ?? throw Mismatch(node);

	internal override void WriteCore(IYamlEmitter emitter, TNode value, int depth) => emitter.WriteNode(value);
}

// values of object members are read as the model, and written as their runtime type, which must be known by the context
internal sealed class UntypedTypeInfo(YamlSerializerContext context) : YamlTypeInfo<object>(context)
{
	internal override object Read(Node node) => node is ScalarNode { IsNull: true } ? null! : node;

	internal override object ReadCore(Node node) => node;

	internal override void WriteCore(IYamlEmitter emitter, object value, int depth)
	{
		var runtimeType = value.GetType();

		// a plain object has no members
		if (runtimeType == typeof(object))
		{
			emitter.WriteStartMapping();
			emitter.WriteEndMapping();
			return;
		}

		var typeInfo = Context.GetTypeInfo(runtimeType) ?? throw new YamlSerializationException($"The type '{runtimeType}' is not known by the serializer context '{Context.GetType()}', list it with {nameof(YamlSerializableAttribute)}.");
		typeInfo.WriteAsObject(emitter, value, depth);
	}
}

internal interface IUnsupportedTypeInfo;

internal sealed class UnsupportedTypeInfo<T>(YamlSerializerContext context, string reason) : YamlTypeInfo<T>(context), IUnsupportedTypeInfo
{
	internal override T ReadCore(Node node) => throw new YamlSerializationException(SerializationFacts.UnsupportedRead(Type, reason));

	internal override void WriteCore(IYamlEmitter emitter, T value, int depth) => throw new YamlSerializationException(SerializationFacts.UnsupportedWrite(Type, reason));
}
