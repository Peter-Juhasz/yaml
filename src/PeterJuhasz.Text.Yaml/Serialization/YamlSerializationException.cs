using System.Globalization;
using System.Text;

namespace PeterJuhasz.Text.Yaml.Serialization;

public sealed class YamlSerializationException : Exception
{
	// the segments are collected from the innermost node outwards, while the exception propagates
	private readonly List<object> segments = [];

	internal YamlSerializationException(string reason) : base(reason)
	{
		Reason = reason;
	}

	public string Reason { get; }

	// the location of the node in the document, like '$.servers[0].port'
	public string Path
	{
		get
		{
			var builder = new StringBuilder("$");
			for (var i = segments.Count - 1; i >= 0; i--)
			{
				_ = segments[i] switch
				{
					int index => builder.Append(CultureInfo.InvariantCulture, $"[{index}]"),
					var key => builder.Append('.').Append(key),
				};
			}
			return builder.ToString();
		}
	}

	public override string Message => $"{Reason} (path {Path})";

	// used in exception filters, so the path is built without catching and rethrowing
	internal bool Prepend(string key)
	{
		segments.Add(key);
		return false;
	}

	internal bool Prepend(int index)
	{
		segments.Add(index);
		return false;
	}
}
