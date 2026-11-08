using PeterJuhasz.Text.Yaml.Serialization;

namespace PeterJuhasz.Text.Yaml.Tests.Serialization;

[TestClass]
public class PolymorphismTests
{
	private static readonly YamlSerializerOptions Disallow = new() { UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow };

	[YamlDerivedType(typeof(Cat), "cat")]
	[YamlDerivedType(typeof(Dog), "dog")]
	public abstract class Animal
	{
		public string? Name { get; set; }
	}

	public sealed class Cat : Animal
	{
		public int Lives { get; set; }
	}

	public sealed class Dog : Animal
	{
		public bool Good { get; set; }
	}

	[YamlDerivedType(typeof(Circle), "circle")]
	[YamlDerivedType(typeof(Square), "square")]
	public interface IShape;

	public sealed record class Circle(double Radius) : IShape;

	public sealed record class Square(double Side) : IShape;

	[YamlPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBaseType)]
	[YamlDerivedType(typeof(HttpJob), 1)]
	public class Job
	{
		public string? Name { get; set; }
	}

	public sealed class HttpJob : Job
	{
		public Uri? Url { get; set; }
	}

	public sealed class Zoo
	{
		public List<Animal> Animals { get; set; } = [];
	}

	[YamlDerivedType(typeof(Clashing), "clashing")]
	public abstract class WithClash;

	public sealed class Clashing : WithClash
	{
		[YamlPropertyName("$type")]
		public string? Type { get; set; }
	}

	[YamlDerivedType(typeof(Cat), "cat")]
	public abstract class Unrelated;

	[YamlDerivedType(typeof(Cat), "a")]
	[YamlDerivedType(typeof(Dog), "a")]
	public abstract class DuplicateDiscriminator : Animal;

	[YamlPolymorphic(UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBaseType)]
	[YamlDerivedType(typeof(Cat), "cat")]
	public abstract class AbstractFallback : Animal;

	[TestMethod]
	public void AbstractBase()
	{
		var cat = YamlSerializer.Deserialize<Animal>("$type: cat\nName: Tom\nLives: 9");
		var dog = YamlSerializer.Deserialize<Animal>("Name: Rex\nGood: true\n$type: dog");

		var typedCat = Assert.IsInstanceOfType<Cat>(cat);
		Assert.AreEqual("Tom", typedCat.Name);
		Assert.AreEqual(9, typedCat.Lives);
		var typedDog = Assert.IsInstanceOfType<Dog>(dog);
		Assert.IsTrue(typedDog.Good);
	}

	[TestMethod]
	public void InterfaceWithRecords()
	{
		var shapes = YamlSerializer.Deserialize<List<IShape>>("- {$type: circle, Radius: 1}\n- {$type: square, Side: 2}");

		Assert.AreSequenceEqual(new IShape[] { new Circle(Radius: 1), new Square(Side: 2) }, shapes);
	}

	[TestMethod]
	public void Null()
	{
		Assert.IsNull(YamlSerializer.Deserialize<Animal>("~"));
	}

	[TestMethod]
	public void DiscriminatorAllowedWhenUnmappedKeysDisallowed()
	{
		Assert.IsInstanceOfType<Cat>(YamlSerializer.Deserialize<Animal>("$type: cat\nLives: 1", Disallow));
		Assert.IsInstanceOfType<Circle>(YamlSerializer.Deserialize<IShape>("$type: circle\nRadius: 1", Disallow));
	}

	[TestMethod]
	public void DiscriminatorUnmappedForDerivedTypeItself()
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Cat>("$type: cat\nLives: 1", Disallow));

		Assert.AreEqual("$.$type", exception.Path);
	}

	[TestMethod]
	public void UnknownDiscriminator()
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Zoo>("Animals:\n  - $type: bird"));

		Assert.AreEqual($"The type discriminator 'bird' does not match any derived type of the polymorphic type '{typeof(Animal)}'.", exception.Reason);
		Assert.AreEqual("$.Animals[0].$type", exception.Path);
	}

	[TestMethod]
	public void DiscriminatorsAreCaseSensitive()
	{
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Animal>("$type: Cat"));
	}

	[TestMethod]
	public void MissingDiscriminatorOfAbstractType()
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Animal>("Name: Tom"));

		Assert.AreEqual($"The type discriminator '$type' of the polymorphic type '{typeof(Animal)}' is missing.", exception.Reason);
		Assert.AreEqual("$", exception.Path);
	}

	[TestMethod]
	[DataRow("$type: [cat]")]
	[DataRow("$type: ~")]
	public void InvalidDiscriminator(string yaml)
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(() => YamlSerializer.Deserialize<Animal>(yaml));

		Assert.AreEqual("$.$type", exception.Path);
	}

	[TestMethod]
	public void NotMapping()
	{
		Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Animal>("[cat]"));
	}

	[TestMethod]
	public void ErrorsOfDerivedType()
	{
		var exception = Assert.ThrowsExactly<YamlSerializationException>(static () => YamlSerializer.Deserialize<Animal>("$type: cat\nLives: many"));

		Assert.AreEqual("$.Lives", exception.Path);
	}

	[TestMethod]
	public void CustomPropertyNameAndIntegerDiscriminator()
	{
		var job = YamlSerializer.Deserialize<Job>("kind: 1\nUrl: https://example.com", Disallow);

		var http = Assert.IsInstanceOfType<HttpJob>(job);
		Assert.AreEqual(new Uri("https://example.com"), http.Url);
	}

	[TestMethod]
	public void FallBackToConcreteBaseType()
	{
		Assert.AreEqual(typeof(Job), YamlSerializer.Deserialize<Job>("Name: a", Disallow)?.GetType());
		Assert.AreEqual(typeof(Job), YamlSerializer.Deserialize<Job>("kind: 2\nName: a", Disallow)?.GetType());
	}

	[TestMethod]
	public void CaseInsensitiveDiscriminatorName()
	{
		var options = new YamlSerializerOptions { PropertyNameCaseInsensitive = true };

		Assert.IsInstanceOfType<HttpJob>(YamlSerializer.Deserialize<Job>("Kind: 1", options));
	}

	[TestMethod]
	public void InvalidConfiguration()
	{
		Assert.ThrowsExactly<InvalidOperationException>(static () => YamlSerializer.Deserialize<WithClash>("$type: clashing"));
		Assert.ThrowsExactly<InvalidOperationException>(static () => YamlSerializer.Deserialize<Unrelated>("$type: cat"));
		Assert.ThrowsExactly<InvalidOperationException>(static () => YamlSerializer.Deserialize<DuplicateDiscriminator>("$type: a"));
		Assert.ThrowsExactly<InvalidOperationException>(static () => YamlSerializer.Deserialize<AbstractFallback>("$type: cat"));
	}
}
