using System.Numerics;
using PeterJuhasz.Text.Yaml.Serialization;

namespace PeterJuhasz.Text.Yaml.Tests.Serialization;

[TestClass]
public class ScalarTests
{
	[TestMethod]
	public void Strings()
	{
		Assert.AreEqual("hello", YamlSerializer.Deserialize<string>("hello"));
		Assert.AreEqual("42", YamlSerializer.Deserialize<string>("42"));
		Assert.AreEqual("null", YamlSerializer.Deserialize<string>("'null'"));
		Assert.IsNull(YamlSerializer.Deserialize<string>("~"));
		Assert.AreEqual("a\nb\n", YamlSerializer.Deserialize<string>("|\n  a\n  b\n"));
	}

	[TestMethod]
	public void Booleans()
	{
		Assert.IsTrue(YamlSerializer.Deserialize<bool>("true"));
		Assert.IsFalse(YamlSerializer.Deserialize<bool>("FALSE"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<bool>("yes"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<bool>("'true'"));
	}

	[TestMethod]
	public void Chars()
	{
		Assert.AreEqual('x', YamlSerializer.Deserialize<char>("x"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<char>("xy"));
	}

	[TestMethod]
	public void Integers()
	{
		Assert.AreEqual((byte)255, YamlSerializer.Deserialize<byte>("0xff"));
		Assert.AreEqual((sbyte)-128, YamlSerializer.Deserialize<sbyte>("-128"));
		Assert.AreEqual((short)-1, YamlSerializer.Deserialize<short>("-1"));
		Assert.AreEqual((ushort)8, YamlSerializer.Deserialize<ushort>("0o10"));
		Assert.AreEqual(42, YamlSerializer.Deserialize<int>("42"));
		Assert.AreEqual(4294967295u, YamlSerializer.Deserialize<uint>("4294967295"));
		Assert.AreEqual(long.MinValue, YamlSerializer.Deserialize<long>("-9223372036854775808"));
		Assert.AreEqual(ulong.MaxValue, YamlSerializer.Deserialize<ulong>("18446744073709551615"));
		Assert.AreEqual(ulong.MaxValue, YamlSerializer.Deserialize<ulong>("0xFFFFFFFFFFFFFFFF"));
		Assert.AreEqual((nint)7, YamlSerializer.Deserialize<nint>("7"));
		Assert.AreEqual((nuint)7, YamlSerializer.Deserialize<nuint>("7"));
		Assert.AreEqual(Int128.MaxValue, YamlSerializer.Deserialize<Int128>("170141183460469231731687303715884105727"));
		Assert.AreEqual(UInt128.MaxValue, YamlSerializer.Deserialize<UInt128>("0xffffffffffffffffffffffffffffffff"));
		Assert.AreEqual(BigInteger.Parse("123456789012345678901234567890", provider: null), YamlSerializer.Deserialize<BigInteger>("123456789012345678901234567890"));
	}

	[TestMethod]
	[DataRow("256")]
	[DataRow("-1")]
	[DataRow("1.5")]
	[DataRow("'1'")]
	[DataRow("abc")]
	[DataRow("1_000")]
	public void IntegersInvalid(string yaml)
	{
		Assert.ThrowsExactly<YamlSerializationException>(() => YamlSerializer.Deserialize<byte>(yaml));
	}

	[TestMethod]
	public void IntegersOutOfRange()
	{
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<int>("2147483648"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<ulong>("-1"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<long>("9223372036854775808"));
	}

	[TestMethod]
	public void FloatingPoint()
	{
		Assert.AreEqual(1.5, YamlSerializer.Deserialize<double>("1.5"));
		Assert.AreEqual(double.NegativeInfinity, YamlSerializer.Deserialize<double>("-.inf"));
		Assert.IsTrue(double.IsNaN(YamlSerializer.Deserialize<double>(".nan")));
		Assert.AreEqual(0.1f, YamlSerializer.Deserialize<float>("0.1"));
		Assert.AreEqual(float.PositiveInfinity, YamlSerializer.Deserialize<float>(".inf"));
		Assert.AreEqual((Half)2.5, YamlSerializer.Deserialize<Half>("2.5"));
		Assert.AreEqual(1.10m, YamlSerializer.Deserialize<decimal>("1.10"));
		Assert.AreEqual(3m, YamlSerializer.Deserialize<decimal>("3"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<float>("1e300"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<double>("'1.5'"));
	}

	[TestMethod]
	public void DateTimes()
	{
		var utc = YamlSerializer.Deserialize<DateTime>("2024-02-29T10:30:00Z");
		Assert.AreEqual(new DateTime(year: 2024, month: 2, day: 29, hour: 10, minute: 30, second: 0, DateTimeKind.Utc), utc);
		Assert.AreEqual(DateTimeKind.Utc, utc.Kind);

		var unspecified = YamlSerializer.Deserialize<DateTime>("2024-02-29 10:30:00");
		Assert.AreEqual(DateTimeKind.Unspecified, unspecified.Kind);
		Assert.AreEqual(new DateTime(year: 2024, month: 2, day: 29, hour: 10, minute: 30, second: 0), unspecified);

		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<DateTime>("2024-02-30"));
	}

	[TestMethod]
	public void DateTimeOffsets()
	{
		Assert.AreEqual(new DateTimeOffset(year: 2024, month: 2, day: 29, hour: 10, minute: 30, second: 0, offset: TimeSpan.FromHours(2)), YamlSerializer.Deserialize<DateTimeOffset>("2024-02-29T10:30:00+02:00"));
		Assert.AreEqual(new DateTimeOffset(year: 2024, month: 2, day: 29, hour: 10, minute: 30, second: 0, offset: TimeSpan.Zero), YamlSerializer.Deserialize<DateTimeOffset>("2024-02-29T10:30:00"));
	}

	[TestMethod]
	public void DateOnlyAndTimeOnly()
	{
		Assert.AreEqual(new DateOnly(year: 2024, month: 2, day: 29), YamlSerializer.Deserialize<DateOnly>("2024-02-29"));
		Assert.AreEqual(new TimeOnly(hour: 13, minute: 45, second: 30), YamlSerializer.Deserialize<TimeOnly>("13:45:30"));
		Assert.AreEqual(new TimeOnly(hour: 8, minute: 5), YamlSerializer.Deserialize<TimeOnly>("'08:05'"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<DateOnly>("29/02/2024 extra"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<TimeOnly>("25:00"));
	}

	[TestMethod]
	[DataRow("01:30:00", 0, 1, 30, 0)]
	[DataRow("1.02:03:04", 1, 2, 3, 4)]
	[DataRow("PT1H30M", 0, 1, 30, 0)]
	[DataRow("P1DT2H3M4S", 1, 2, 3, 4)]
	public void TimeSpans(string yaml, int days, int hours, int minutes, int seconds)
	{
		Assert.AreEqual(new TimeSpan(days, hours, minutes, seconds), YamlSerializer.Deserialize<TimeSpan>(yaml));
	}

	[TestMethod]
	public void TimeSpansInvalid()
	{
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<TimeSpan>("PX"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<TimeSpan>("abc"));
	}

	[TestMethod]
	public void Uris()
	{
		Assert.AreEqual(new Uri("https://example.com/path?q=1"), YamlSerializer.Deserialize<Uri>("https://example.com/path?q=1"));
		var relative = YamlSerializer.Deserialize<Uri>("images/logo.png");
		Assert.IsNotNull(relative);
		Assert.IsFalse(relative.IsAbsoluteUri);
		Assert.IsNull(YamlSerializer.Deserialize<Uri>("~"));
	}

	[TestMethod]
	public void OtherParsableTypes()
	{
		Assert.AreEqual(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"), YamlSerializer.Deserialize<Guid>("6f9619ff-8b86-d011-b42d-00c04fc964ff"));
		Assert.AreEqual(new Version(major: 1, minor: 2, build: 3), YamlSerializer.Deserialize<Version>("1.2.3"));
		Assert.AreEqual(System.Net.IPAddress.Loopback, YamlSerializer.Deserialize<System.Net.IPAddress>("127.0.0.1"));
	}

	[TestMethod]
	public void Nullable()
	{
		Assert.AreEqual(42, YamlSerializer.Deserialize<int?>("42"));
		Assert.IsNull(YamlSerializer.Deserialize<int?>("null"));
		Assert.IsNull(YamlSerializer.Deserialize<int?>(""));
		Assert.IsNull(YamlSerializer.Deserialize<DateOnly?>("~"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<int>("~"));
	}

	[TestMethod]
	public void Binary()
	{
		Assert.AreSequenceEqual("hello"u8.ToArray(), YamlSerializer.Deserialize<byte[]>("aGVsbG8="));
		Assert.AreSequenceEqual("hello"u8.ToArray(), YamlSerializer.Deserialize<byte[]>("|\n  aGVs\n  bG8=\n"));
		Assert.AreSequenceEqual("\x01\x02"u8.ToArray(), YamlSerializer.Deserialize<byte[]>("[1, 2]"));
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<byte[]>("not base64!"));
	}

	[TestMethod]
	public void ScalarExpected()
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<int>("[1]"));
		Assert.AreEqual("Cannot convert a sequence to type 'System.Int32'.", exception.Reason);
	}
}
