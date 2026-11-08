using System.Text;

namespace PeterJuhasz.Text.Yaml.Serialization;

// converts the names of the members of .NET types to the keys used in yaml documents
public abstract class YamlNamingPolicy
{
	public static YamlNamingPolicy CamelCase { get; } = new CamelCaseNamingPolicy();

	public static YamlNamingPolicy SnakeCaseLower { get; } = new SeparatorNamingPolicy(Separator: '_', Uppercase: false);

	public static YamlNamingPolicy SnakeCaseUpper { get; } = new SeparatorNamingPolicy(Separator: '_', Uppercase: true);

	public static YamlNamingPolicy KebabCaseLower { get; } = new SeparatorNamingPolicy(Separator: '-', Uppercase: false);

	public static YamlNamingPolicy KebabCaseUpper { get; } = new SeparatorNamingPolicy(Separator: '-', Uppercase: true);

	public abstract string ConvertName(string name);

	private sealed class CamelCaseNamingPolicy : YamlNamingPolicy
	{
		public override string ConvertName(string name)
		{
			ArgumentNullException.ThrowIfNull(name);
			if (name is not [var first, ..] || !Char.IsUpper(first))
			{
				return name;
			}

			return String.Create(name.Length, name, static (chars, name) =>
			{
				name.CopyTo(chars);
				for (var i = 0; i < chars.Length; i++)
				{
					if (i == 1 && !Char.IsUpper(chars[i]))
					{
						break;
					}

					// the last uppercase letter of a leading acronym starts the next word, like in 'URLValue'
					if (i > 0 && i + 1 < chars.Length && !Char.IsUpper(chars[i + 1]))
					{
						if (chars[i + 1] is ' ')
						{
							chars[i] = Char.ToLowerInvariant(chars[i]);
						}
						break;
					}

					chars[i] = Char.ToLowerInvariant(chars[i]);
				}
			});
		}
	}

	private sealed class SeparatorNamingPolicy(char Separator, bool Uppercase) : YamlNamingPolicy
	{
		private enum Category
		{
			None,
			Uppercase,
			LowercaseOrDigit,
		}

		public override string ConvertName(string name)
		{
			ArgumentNullException.ThrowIfNull(name);

			var builder = new StringBuilder(name.Length + 4);
			var previous = Category.None;
			var boundary = false;
			for (var i = 0; i < name.Length; i++)
			{
				var current = name[i];

				// any other character separates words, and is not written
				if (!Char.IsLetterOrDigit(current))
				{
					boundary = builder.Length > 0;
					previous = Category.None;
					continue;
				}

				var isUpper = Char.IsUpper(current);
				var startsWord = (boundary, isUpper, previous) switch
				{
					(true, _, _) => true,
					(_, true, Category.LowercaseOrDigit) => true,

					// the last uppercase letter of an acronym starts the next word, like in 'HTMLParser'
					(_, true, Category.Uppercase) => i + 1 < name.Length && Char.IsLower(name[i + 1]),
					_ => false,
				};
				if (startsWord)
				{
					builder.Append(Separator);
				}

				builder.Append(Uppercase ? Char.ToUpperInvariant(current) : Char.ToLowerInvariant(current));
				boundary = false;
				previous = isUpper ? Category.Uppercase : Category.LowercaseOrDigit;
			}

			return builder.ToString();
		}
	}
}
