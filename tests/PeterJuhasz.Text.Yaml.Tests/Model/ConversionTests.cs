using PeterJuhasz.Text.Yaml.Model;
using static PeterJuhasz.Text.Yaml.Tests.Yaml;

namespace PeterJuhasz.Text.Yaml.Tests.Model;

[TestClass]
public class ConversionTests
{
	[TestMethod]
	[DataRow("true", true)]
	[DataRow("True", true)]
	[DataRow("TRUE", true)]
	[DataRow("false", false)]
	[DataRow("False", false)]
	[DataRow("FALSE", false)]
	public void TryGetBoolean(string value, bool expected)
	{
		Assert.IsTrue(Plain(value).TryGetBoolean(out var actual));
		Assert.AreEqual(expected, actual);
	}

	[TestMethod]
	[DataRow("tRUE")]
	[DataRow("yes")]
	[DataRow("on")]
	[DataRow("1")]
	[DataRow("")]
	public void TryGetBooleanInvalid(string value)
	{
		Assert.IsFalse(Plain(value).TryGetBoolean(out _));
	}

	[TestMethod]
	[DataRow("0", 0)]
	[DataRow("42", 42)]
	[DataRow("-42", -42)]
	[DataRow("+42", 42)]
	[DataRow("007", 7)]
	[DataRow("0x1F", 31)]
	[DataRow("0xff", 255)]
	[DataRow("0o17", 15)]
	[DataRow("2147483647", int.MaxValue)]
	[DataRow("-2147483648", int.MinValue)]
	public void TryGetInt32(string value, int expected)
	{
		Assert.IsTrue(Plain(value).TryGetInt32(out var actual));
		Assert.AreEqual(expected, actual);
	}

	[TestMethod]
	[DataRow("2147483648")]
	[DataRow("-2147483649")]
	[DataRow("1.0")]
	[DataRow("1e3")]
	[DataRow("1_000")]
	[DataRow(" 1")]
	[DataRow("0x")]
	[DataRow("0o")]
	[DataRow("0o8")]
	[DataRow("0o1x")]
	[DataRow("0x1G")]
	[DataRow("0x 1")]
	[DataRow("42\0")]
	[DataRow("0x1F\0")]
	[DataRow("0o17\0")]
	[DataRow("0X1F")]
	[DataRow("-0x1")]
	[DataRow("+")]
	[DataRow("")]
	[DataRow("abc")]
	public void TryGetInt32Invalid(string value)
	{
		Assert.IsFalse(Plain(value).TryGetInt32(out _));
	}

	[TestMethod]
	[DataRow("9223372036854775807", long.MaxValue)]
	[DataRow("-9223372036854775808", long.MinValue)]
	[DataRow("0x7FFFFFFFFFFFFFFF", long.MaxValue)]
	[DataRow("0o777777777777777777777", long.MaxValue)]
	[DataRow("4294967296", 4294967296L)]
	public void TryGetInt64(string value, long expected)
	{
		Assert.IsTrue(Plain(value).TryGetInt64(out var actual));
		Assert.AreEqual(expected, actual);
	}

	[TestMethod]
	[DataRow("9223372036854775808")]
	[DataRow("0x8000000000000000")]
	[DataRow("0xFFFFFFFFFFFFFFFF")]
	[DataRow("0o1000000000000000000000")]
	public void TryGetInt64Overflow(string value)
	{
		Assert.IsFalse(Plain(value).TryGetInt64(out _));
	}

	[TestMethod]
	[DataRow("1.5", 1.5)]
	[DataRow("-1.5", -1.5)]
	[DataRow("+1.5", 1.5)]
	[DataRow(".5", 0.5)]
	[DataRow("5.", 5.0)]
	[DataRow("1e3", 1000.0)]
	[DataRow("1.5E-2", 0.015)]
	[DataRow("42", 42.0)]
	[DataRow("0x10", 16.0)]
	public void TryGetDouble(string value, double expected)
	{
		Assert.IsTrue(Plain(value).TryGetDouble(out var actual));
		Assert.AreEqual(expected, actual);
	}

	[TestMethod]
	[DataRow(".inf", double.PositiveInfinity)]
	[DataRow("+.Inf", double.PositiveInfinity)]
	[DataRow("-.INF", double.NegativeInfinity)]
	public void TryGetDoubleInfinity(string value, double expected)
	{
		Assert.IsTrue(Plain(value).TryGetDouble(out var actual));
		Assert.AreEqual(expected, actual);
	}

	[TestMethod]
	[DataRow(".nan")]
	[DataRow(".NaN")]
	[DataRow(".NAN")]
	public void TryGetDoubleNaN(string value)
	{
		Assert.IsTrue(Plain(value).TryGetDouble(out var actual));
		Assert.IsTrue(double.IsNaN(actual));
	}

	[TestMethod]
	[DataRow("Infinity")]
	[DataRow("-Infinity")]
	[DataRow("infinity")]
	[DataRow("NaN")]
	[DataRow("nan")]
	[DataRow("-.")]
	[DataRow("1..2")]
	[DataRow("1e+")]
	[DataRow("1.5\0")]
	[DataRow("5.\0")]
	[DataRow("-.nan")]
	[DataRow(".")]
	[DataRow("1,5")]
	[DataRow("1e")]
	[DataRow("e3")]
	[DataRow("1.5f")]
	[DataRow("")]
	public void TryGetDoubleInvalid(string value)
	{
		Assert.IsFalse(Plain(value).TryGetDouble(out _));
	}

	[TestMethod]
	[DataRow("1.10", "1.10")]
	[DataRow("-0.001", "-0.001")]
	[DataRow("79228162514264337593543950335", "79228162514264337593543950335")]
	[DataRow("42", "42")]
	[DataRow("1e2", "100")]
	public void TryGetDecimal(string value, string expected)
	{
		Assert.IsTrue(Plain(value).TryGetDecimal(out var actual));
		Assert.AreEqual(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), actual);
	}

	[TestMethod]
	[DataRow(".inf")]
	[DataRow(".nan")]
	[DataRow("79228162514264337593543950336")]
	[DataRow("abc")]
	public void TryGetDecimalInvalid(string value)
	{
		Assert.IsFalse(Plain(value).TryGetDecimal(out _));
	}

	[TestMethod]
	[DataRow(ScalarStyle.SingleQuoted)]
	[DataRow(ScalarStyle.DoubleQuoted)]
	[DataRow(ScalarStyle.Literal)]
	[DataRow(ScalarStyle.Folded)]
	public void NonPlainScalarsAreStrings(ScalarStyle style)
	{
		Assert.IsFalse(new ScalarNode(Value: "true", Style: style).TryGetBoolean(out _));
		Assert.IsFalse(new ScalarNode(Value: "1", Style: style).TryGetInt32(out _));
		Assert.IsFalse(new ScalarNode(Value: "1", Style: style).TryGetInt64(out _));
		Assert.IsFalse(new ScalarNode(Value: "1.5", Style: style).TryGetDouble(out _));
		Assert.IsFalse(new ScalarNode(Value: "1.5", Style: style).TryGetDecimal(out _));
	}

	[TestMethod]
	public void TryGetCoreSchemaTypes()
	{
		Assert.IsTrue(Plain("TRUE").TryGet<bool>(out var boolean));
		Assert.IsTrue(boolean);
		Assert.IsTrue(Plain("0x1F").TryGet<int>(out var int32));
		Assert.AreEqual(31, int32);
		Assert.IsTrue(Plain("0o17").TryGet<long>(out var int64));
		Assert.AreEqual(15L, int64);
		Assert.IsTrue(Plain(".inf").TryGet<double>(out var number));
		Assert.AreEqual(double.PositiveInfinity, number);
		Assert.IsTrue(Plain("1.10").TryGet<decimal>(out var money));
		Assert.AreEqual(1.10m, money);
	}

	[TestMethod]
	public void TryGetCoreSchemaTypesInvalid()
	{
		Assert.IsFalse(Plain("yes").TryGet<bool>(out _));
		Assert.IsFalse(Plain("2147483648").TryGet<int>(out _));
		Assert.IsFalse(Plain("Infinity").TryGet<double>(out _));
		Assert.IsFalse(new ScalarNode(Value: "1", Style: ScalarStyle.DoubleQuoted).TryGet<int>(out _));
	}

	[TestMethod]
	public void TryGetOtherTypes()
	{
		Assert.IsTrue(Plain("2024-02-29").TryGet<DateOnly>(out var date));
		Assert.AreEqual(new DateOnly(year: 2024, month: 2, day: 29), date);
		Assert.IsTrue(new ScalarNode(Value: "6f9619ff-8b86-d011-b42d-00c04fc964ff", Style: ScalarStyle.SingleQuoted).TryGet<Guid>(out var guid));
		Assert.AreEqual(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"), guid);
		Assert.IsTrue(Plain("1.5").TryGet<float>(out var single));
		Assert.AreEqual(1.5f, single);
		Assert.IsFalse(Plain("abc").TryGet<Guid>(out _));
	}
}
