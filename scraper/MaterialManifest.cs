using Grimoire.Util;

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
	public IEnumerable<Material> Materials
		=> materials.Values;

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
		if(materials.ContainsKey(mat.Name))
			throw new ArgumentException($"Material '{mat.Name}' already exists");
		if(! baseUnits.Contains(mat.Amount.Unit))
			mat = new(mat.Name, Normalize(mat.Amount), mat.Price, mat.Reference);

		materials[mat.Name] = mat;
	}

	/// <summary>
	/// Brings an amount into canonical form, i.e. in terms of base units
	/// </summary>
	public Amount Normalize(Amount amt)
		=> allUnits.TryGetValue(amt.Unit, out var def)
			? amt.Number * def
			: throw new ArgumentException($"No such unit '{amt.Unit}'");

	/// <summary>
	///  Runs a post-processing step on the already defined materials
	/// </summary>
	public void PostProcess(Glob input, Amount inAmount, Glob output, Amount outAmount)
	{
		var buf = new List<Material>();
		inAmount = Normalize(inAmount);
		outAmount = Normalize(outAmount);

		foreach(var material in Materials)
		{
			if(! input.Test(material.Name, out var v))
				continue;

			var name = output.Insert(v);

			if(materials.ContainsKey(name))
				throw new ArgumentException($"Post-processing '{material.Name}' produced already existent material '{name}'");
			if(buf.Any(m => m.Name == name))
				throw new ArgumentException($"Post-processing produced '{name}' twice");
			if(material.Amount.Unit != inAmount.Unit)
				throw new ArgumentException($"Cannot apply rule to '{material.Name}' due to unit mismatch; Expected {inAmount.Unit} but got {material.Amount.Unit}");

			int gcd = Extensions.GCD(inAmount.Number, material.Amount.Number);
			int numPurchases = material.Amount.Number / gcd;
			int numApplications = inAmount.Number / gcd;

			buf.Add(new(name, numApplications * outAmount, numPurchases * material.Price));
		}

		foreach(var m in buf)
			materials[m.Name] = m;
	}

	public bool TryGetUnit(string name, out Amount definition)
		=> allUnits.TryGetValue(name, out definition);
}