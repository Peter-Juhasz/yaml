using System.Globalization;

namespace PeterJuhasz.Text.Yaml.Serialization;

// a derived type, which is deserialized when the type discriminator of the mapping matches the main discriminator or one of its aliases
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class YamlDerivedTypeAttribute(Type derivedType, string typeDiscriminator, params string[] aliases) : Attribute
{
	// integer discriminators are matched by their invariant text, like '1'
	public YamlDerivedTypeAttribute(Type derivedType, int typeDiscriminator, params int[] aliases) : this(derivedType, ToString(typeDiscriminator), [.. aliases.Select(ToString)])
	{
	}

	public Type DerivedType { get; } = derivedType;

	public string TypeDiscriminator { get; } = typeDiscriminator;

	// alternative discriminators, which are accepted when reading
	public IReadOnlyList<string> Aliases { get; } = aliases;

	private static string ToString(int value) => value.ToString(CultureInfo.InvariantCulture);
}
