using Grimoire.Util;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Grimoire;

/// <summary>
/// A scalar with an associated unit
/// </summary>
public readonly record struct Amount(int Number, string Unit)
{
	public static Amount operator *(int k, Amount x)
		=> new(k * x.Number, x.Unit);
	public static Amount operator *(Amount x, int k)
		=> k * x;

	public bool TryAdd(Amount other, out Amount sum)
	{
		if(Unit == other.Unit)
		{
			sum = new(Number + other.Number, Unit);
			return true;
		}
		else
		{
			sum = default;
			return false;
		}
	}

	public void WriteJson(Utf8JsonWriter to)
	{
		to.WriteStartObject();

		to.WriteNumber(nameof(Number).ToLower(), Number);
		to.WriteString(nameof(Unit).ToLower(), Unit);

		to.WriteEndObject();
	}

	public static readonly Amount ONE = new(1, MaterialManifest.DIMENSIONLESS_UNIT);
}

/// <summary>
/// A price in copper pieces
/// </summary>
public readonly record struct Price(int CopperPieces)
{
	public static Price operator *(int k, Price x)
		=> new(k * x.CopperPieces);
	public static Price operator *(Price x, int k)
		=> k * x;

	public static Price operator +(Price x, Price y)
		=> new(x.CopperPieces + y.CopperPieces);

	public static bool operator >(Price l, Price r)
		=> l.CopperPieces > r.CopperPieces;
	public static bool operator <(Price l, Price r)
		=> l.CopperPieces < r.CopperPieces;
	public static bool operator >=(Price l, Price r)
		=> l.CopperPieces >= r.CopperPieces;
	public static bool operator <=(Price l, Price r)
		=> l.CopperPieces <= r.CopperPieces;
}

/// <summary>
/// A material definition giving a purchase price.
/// We don't resolve fractional price (i.e. price per single unit) on scraper-side.
/// </summary>
public readonly record struct Material(string Name, Amount Amount, Price Price, string? Reference = null);

/// <summary>
///  Contains a complete description of the materials used in a game's spells
/// </summary>
public sealed class MaterialManifest
{
	/// <summary>
	/// The unit name for simple scalars
	/// </summary>
	public const string DIMENSIONLESS_UNIT = "1";
	/// <summary>
	/// The units which cannot be subdivided further
	/// </summary>
	private readonly HashSet<string> baseUnits = [];
	/// <summary>
	/// Every unit, including base units
	/// </summary>
	private readonly Dictionary<string, Amount> allUnits = [];
	private readonly Dictionary<string, Material> materials = [];

	/// <summary>
	/// Every material processed so far
	/// </summary>
	public IReadOnlyCollection<Material> Materials
		=> materials.Values;

	public IReadOnlyCollection<string> Units
		=> allUnits.Keys;

	public MaterialManifest()
	{
		AddBaseUnit(DIMENSIONLESS_UNIT);
	}

	/// <summary>
	///  Inserts a new base unit
	/// </summary>
	public void AddBaseUnit(string name)
	{
		if(allUnits.ContainsKey(name))
			throw new ArgumentException($"Unit '{name}' already exists");

		baseUnits.Add(name);
		allUnits.Add(name, new(1, name));
	}

	/// <summary>
	///  Inserts a new unit defined in terms of another
	/// </summary>
	public void AddUnit(string name, Amount definition)
	{
		if(allUnits.ContainsKey(name))
			throw new ArgumentException($"Unit '{name}' already exists");

		allUnits.Add(name, Normalize(definition));
	}

	/// <summary>
	///  Inserts a new material definition
	/// </summary>
	public void AddMaterial(string name, Amount amount, Price price, string? reference = null)
		=> AddMaterial(new(name, Normalize(amount), price, reference));


	public void AddMaterial(Material mat)
	{
		var key = mat.Name.ToLower();
		if(materials.ContainsKey(key))
			throw new ArgumentException($"Material '{mat.Name}' already exists");
		if(! baseUnits.Contains(mat.Amount.Unit))
			mat = new(mat.Name, Normalize(mat.Amount), mat.Price, mat.Reference);

		materials[key] = mat;
	}

	public bool TryNormalize(Amount amount, [MaybeNullWhen(false)] out Amount normalized)
	{
		if(allUnits.TryGetValue(amount.Unit, out var definition))
		{
			normalized = definition * amount.Number;
			return true;
		}
		else
		{
			normalized = default;
			return false;
		}
	}

	public bool TryGetMaterial(string name, [MaybeNullWhen(false)] out Material material)
		=> materials.TryGetValue(name.ToLower(), out material);

	/// <summary>
	/// Brings an amount into canonical form, i.e. in terms of base units
	/// </summary>
	public Amount Normalize(Amount amt)
		=> TryNormalize(amt, out var norm) ? norm : throw new ArgumentException($"No such unit '{amt.Unit}'");

	/// <summary>
	///  Runs a post-processing step on the already defined materials
	/// </summary>
	private void postProcess(Func<Material, Material?> processor)
	{
		var buf = new List<(string key, Material entry)>();

		foreach(var material in Materials)
		{
			if(! processor(material).Unpack(out var result))
				continue;

			var key = result.Name.ToLower();

			if(materials.ContainsKey(key))
				throw new ArgumentException($"Post-processing '{material.Name}' produced already existent material '{result.Name}'");
			if(buf.Any(m => m.key == key))
				throw new ArgumentException($"Post-processing produced '{result.Name}' twice");

			buf.Add((key, result));
		}

		foreach(var (key, entry) in buf)
			materials[key] = entry;
	}

	/// <summary>
	///  Post-processes by performing a """crafting""" step
	/// </summary>
	public void PostProcess(Glob input, Amount inAmount, Glob output, Amount outAmount)
	{
		inAmount = Normalize(inAmount);
		outAmount = Normalize(outAmount);

		postProcess(material =>
		{
			if(! input.Test(material.Name, out var v))
				return null;

			var name = output.Insert(v);

			if(material.Amount.Unit != inAmount.Unit)
				throw new ArgumentException($"Cannot apply rule to '{material.Name}' due to unit mismatch; Expected {inAmount.Unit} but got {material.Amount.Unit}");

			int gcd = Extensions.GCD(inAmount.Number, material.Amount.Number);
			int numPurchases = material.Amount.Number / gcd;
			int numApplications = inAmount.Number / gcd;

			return new Material(name, numApplications * outAmount, numPurchases * material.Price, material.Reference);
		});
	}

	/// <summary>
	///  Post-processes by aliasing materials
	/// </summary>
	public void Alias(Glob input, Glob output)
	{
		postProcess(material =>
		{
			if(! input.Test(material.Name, out var v))
				return null;

			return material with { Name = output.Insert(v) };
		});
	}

	public bool TryGetUnit(string name, [MaybeNullWhen(false)] out Amount definition)
		=> allUnits.TryGetValue(name, out definition);

	public (double? price, string? reference) ResolveComponent(Log log, Amount amount, string of)
	{
		if(! TryNormalize(amount, out var normAmount))
		{
			log.Warn($"Reference to unknown unit '{amount.Unit}'");
			return (null, null);
		}

		if(! TryGetMaterial(of, out var material))
		{
			// attempt to de-pluralize
			if(amount.Unit == DIMENSIONLESS_UNIT && of.EndsWith('s') && TryGetMaterial(of[..^1], out material))
				goto recovered;

			if(amount != Amount.ONE) // don't warn about unitless items (i.e. quest components)
				log.Warn($"Reference to unknown material '{of}'");

			return (null, null);
		}

		recovered:
		if(material.Amount.Unit != normAmount.Unit)
		{
			log.Warn($"Unit mismatch for material '{material.Name}' specified in [{material.Amount.Unit}], but spell requires [{amount.Unit}] (i.e. [{normAmount.Unit}])");
			return (null, material.Reference);
		}

		return (
			(material.Price.CopperPieces * normAmount.Number) / (double)material.Amount.Number,
			material.Reference
		);
	}
}