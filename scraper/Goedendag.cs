using Grimoire.Latex;
using Grimoire.Util;
using System.Collections.Immutable;
using System.Data;
using System.Text.RegularExpressions;

namespace Grimoire;

public record class Goedendag(Config.Game Conf) : IGame<Goedendag.Spell>
{
	public enum Arcanum
	{
		Charms,
		Conjuration,
		Divine,
		Elementalism,
		General,
		Nature,
		Wytch,
		Ritual
	}

	public enum PowerLevel
	{
		Generalist,
		Petty,
		Lesser,
		Greater,
		Ritual
	}

	public readonly record struct Component(
		string display,
		bool consumed,
		bool used,
		double? price = null,
		string? reference = null
	);

	public readonly record struct Spell(
		string name,
		Arcanum arcanum,
		PowerLevel powerLevel,
		bool combat,
		bool reaction,
		string distance,
		string duration,
		string castingTime,
		Component[] components,
		string brief,
		string effect,
		string critSuccess,
		string critFail,
		string? extra,
		string Source
	) : ISpell;

	/// <summary>
	///  The character that separates variant in \grVariants
	/// </summary>
	private const char VARIANT_SEPARATOR = '/';

	private static readonly Regex amountRegex = new(@"\s*([0-9]+)\s*(\S*)\s*");

	private static readonly ImmutableDictionary<string, int> coinMacros = new Dictionary<string, int>() {
		{ "Cu", 1 },
		{ "Ag", 36 },
		{ "Au", 12*36 }
	}.ToImmutableDictionary();

	public Log Log { get; } = Log.DEFAULT.AddTags(Conf.Shorthand);

	public MaterialManifest Manifest { get; } = initUnits();

	private static Amount parseAmount(string str)
	{
		var m = amountRegex.Match(str);

		if(! m.Success)
			goto bad;

		var num = int.Parse(m.Groups[1].ValueSpan);
		var unit = m.Groups[2].ValueSpan;

		if(unit.Length > 0 && (unit[0] == '[' || unit[^1] == ']'))
		{
			if(unit[0] != '[' || unit[^1] != ']')
				goto bad;

			unit = unit[1..^1];
		}

		return new(num, unit.IsEmpty ? MaterialManifest.DIMENSIONLESS_UNIT : unit.ToString());

	bad:
		throw new FormatException($"Invalid amount: {str}");
	}

	private static Price parsePrice(Chain<Token> code)
	{
		int sum = 0;
		var rest = code;

		while(true)
		{
			var spl = rest.SplitOn(tk => tk is MacroName m && coinMacros.ContainsKey(m.Macro));

			if(! spl.HasValue)
			{
				if(rest.Items().Any(tk => tk is not WhiteSpace))
					goto bad;

				return new(sum);
			}

			if(! int.TryParse(Lexer.Untokenize(spl.Value.left).Trim(), out var n))
				goto bad;

			sum += coinMacros[((MacroName)spl.Value.at).Macro] * n;
			rest = spl.Value.right;
		}

		bad:
		throw new FormatException($"Invalid price '{Lexer.Untokenize(code)}'");
	}

	private static MaterialManifest initUnits()
	{
		var mf = new MaterialManifest();

		// TODO: un-hardcode this
		mf.AddBaseUnit("g");
		mf.AddBaseUnit("drop");
		mf.AddBaseUnit("cm");
		mf.AddBaseUnit("cm^2");

		mf.AddUnit("drops", new(1, "drop")); // alias
		mf.AddUnit("kg", new(1000, "g"));
		mf.AddUnit("ml", new(2, "drop"));
		mf.AddUnit("l", new(1000, "ml"));
		mf.AddUnit("m", new(100, "cm"));

		return mf;
	}


	private static readonly ArgType[] UNIT_SIGNATURE = [new StarArg(), new MandatoryArg()];

	/// <summary>
	///  Extracts material definitions from a code line
	/// </summary>
	private IEnumerable<Material> extractMaterial(Compiler comp, Chain<Token> line, string[]? ctx)
	{
		var cols = line.SplitBy(tk => tk is AlignTab, true).ToArray();

		// collect names
		var _names = line.extractInvocations("grMaterial").ToList();
		// normal unit references
		var unit = line.extractSingleInvocation("grUnit", UNIT_SIGNATURE);
		var otherUnit = line.extractSingleInvocation("grOtherUnit", UNIT_SIGNATURE);
		// variant reference
		var variant = line.extractSingleInvocation("grVariants");

		if(_names.Count > 0 && unit is null && !variant.HasValue)
			throw new FormatException(@"Incomplete material definition");
		if(unit is null && otherUnit is not null)
			throw new FormatException(@"Using \grOtherUnit without \grUnit");
		if(unit is not null && variant.HasValue)
			throw new FormatException(@"Mixing \grUnit and \grVariants");

		var names = (_names.Count == 0 ? [cols[0]] : _names).Select(comp.ToString);

		if(unit is not null)
		{
			var price = parsePrice(cols[2]);
			var amt = parseAmount(comp.ToString(unit[1]));

			if(otherUnit is not null)
			{
				var otherAmt = parseAmount(comp.ToString(otherUnit[1]));

				if(Manifest.TryGetUnit(otherAmt.Unit, out var trUnit) && trUnit.Unit != MaterialManifest.DIMENSIONLESS_UNIT)
					throw new FormatException($"Transient unit '{otherAmt.Unit}' is not dimensionless");

				amt *= otherAmt.Number;
				// TODO: preserve transient unit information in some way
			}

			return names.Select(n => new Material(n, amt, price));
		}
		else if(variant.HasValue)
		{
			if(ctx is null)
				throw new FormatException(@"\grVariants without preceding \grDeclareVariants");

			var pieces = variant.Value.SplitBy(tk => tk is Character c && c.Char == VARIANT_SEPARATOR).ToList();

			if(pieces.Count != ctx.Length)
				throw new FormatException(@"Arity mismatch between \grVariants and preceding \grDeclareVariants");

			return names.SelectMany(name => ctx.Zip(pieces)
					.Select(xy => new Material($"{xy.First} {name}", Amount.ONE, parsePrice(xy.Second)))
				);
		}
		else
			return [];
	}

	private static void extractVariantDecl(Compiler comp, Chain<Token> line, ref string[]? ctx)
	{
		var inv = line.extractSingleInvocation("grDeclareVariants");

		if(! inv.HasValue)
			return;

		ctx = inv.Value
			.SplitBy(tk => tk is Character c && c.Char == VARIANT_SEPARATOR)
			.Select(comp.ToString)
			.ToArray();
	}

	private string processCastTime(string spell, string ct, ref bool reaction)
	{
		var pieces = ct.Split("\\action");
		var log = Log.AddTags(spell);
		var _reaction = false;

		for(int i = 1; i < pieces.Length; ++i)
		{
			var cursor = pieces[i].AsSpan();

			if(cursor.Length > 0 && cursor[0] == '*')
			{
				_reaction = true;
				cursor = cursor.Slice(1);
			}

			string acp;

			if(cursor.Length > 0 && cursor[0] == '[')
			{
				int cl = cursor.IndexOf(']');

				if(cl < 0)
					throw new FormatException("Unterminated [] in casting time");

				acp = cursor.Slice(1, cl - 1).ToString();
				cursor = cursor.Slice(cl + 1);
			}
			else
			{
				log.Warn("Casting time uses plain \\action");
				acp = "1";
			}

			pieces[i] = acp + " AcP" + cursor.ToString();
		}

		if(_reaction)
		{
			if(reaction)
				log.Warn("Mixing legacy (R) and new \\action*");

			reaction = true;
		}

		return string.Join("", pieces);
	}

	private readonly static Pattern COMPONENT_SEPARATOR = Pattern.Of(
		Pattern.Of("", ",and", "#"),
		Pattern.Of("", ", and", "#"), // whitespace should be squeezed so this matches properly
		Pattern.Of(","),
		Pattern.Of("#", "and", "#")
	);

	/// <summary>
	///  Separates components by both ',' and 'and', trimmed and without empty entries.
	/// </summary>
	private static IEnumerable<Chain<Token>> separateComponents(Chain<Token> code)
	{
		var rest = code.Trim();

		while(true)
		{
			var _spl = rest.SplitOn(COMPONENT_SEPARATOR, true);

			if(! _spl.Unpack(out var spl))
				break;

			var cur = spl.left.TrimEnd();

			if(cur.IsNotEmpty)
				yield return cur;

			rest = spl.right.TrimStart();
		}

		if(rest.IsNotEmpty)
			yield return rest;
	}

	private const string CONSUMED_MACRO = "Consumed";
	private const string USED_MACRO = "Used";
	private const string LEGACY_CONSUMED_MACRO = "con";
	private const string LEGACY_USED_MACRO = "use";

	/// <summary>
	/// CG[1] = number, CG[2] = unit name (sans brackets), CG[3] = discard, CG[4] = material name
	/// </summary>
	private static readonly Regex UNIT_REGEX = new(@"^\s*([0-9]+)\s*\[([^\]]+)\]\s+(of\s)?(.+)$", RegexOptions.IgnoreCase);
	/// <summary>
	/// CG[1] = discard, CG[2] = material name
	/// </summary>
	private static readonly Regex ARTICLE_REGEX = new(@"^\s*(an?|1|one)\s(.+)$", RegexOptions.IgnoreCase);
	/// <summary>
	/// CG[1] = number, CG[2] = material name
	/// </summary>
	private static readonly Regex NUMBERED_REGEX = new(@"^\s*([0-9]+)\s(.+)$", RegexOptions.IgnoreCase);
	/// <summary>
	/// CG[1] = material name, CG[2] = discard, CG[3] = number, CG[4] = unit name
	/// </summary>
	private static readonly Regex SUFFIX_UNIT_REGEX = new("^(.+)" + @"(\(\s*|\W)" + "([0-9]+)" + @"\s+\[([^\]]+)\]" + @"\s*\)?$", RegexOptions.IgnoreCase);

	private static (Amount amount, string of) extractMaterial(string component)
	{
		{
			var bracketedUnit = UNIT_REGEX.Match(component);

			if(bracketedUnit.Success)
			{
				int n = int.Parse(bracketedUnit.Groups[1].ValueSpan);
				string unit = bracketedUnit.Groups[2].Value;
				string of = bracketedUnit.Groups[4].Value.Trim();

				return (new(n, unit), of);
			}
		}

		{
			var articledUnit = ARTICLE_REGEX.Match(component);

			if(articledUnit.Success)
				return (Amount.ONE, articledUnit.Groups[1].Value.Trim());
		}

		{
			var numbered = NUMBERED_REGEX.Match(component);

			if(numbered.Success)
			{
				int n = int.Parse(numbered.Groups[1].ValueSpan);
				string of = numbered.Groups[2].Value.Trim();

				return (new(n, MaterialManifest.DIMENSIONLESS_UNIT), of);
			}
		}

		{
			var suffixed = SUFFIX_UNIT_REGEX.Match(component);

			if(suffixed.Success)
			{
				string of = suffixed.Groups[1].Value.Trim();
				int n = int.Parse(suffixed.Groups[3].ValueSpan);
				string unit = suffixed.Groups[4].Value;

				return (new(n, unit), of);
			}
		}


		return (Amount.ONE, component.Trim());
	}

	private Component[] extractComponents(Compiler compiler, Chain<Token> code)
		=> separateComponents(
				code.Length > 2 && code[0] is OpenBrace && code[^1] is CloseBrace
					? code.Slice(1, code.Length - 2) : code
		).Select(piece => {
			bool delta = true;
			bool consumed = false;
			bool used = false;

			// strip off consumes and used markers
			while(delta)
			{
				delta = false;

				if(piece.Length > 0 && piece[^1] is Character p && p.Char == '.')
					piece = piece.Slice(0, piece.Length - 1).TrimEnd();

				if(piece.Length > 0 && piece[^1] is MacroName c && (c.Macro == CONSUMED_MACRO || c.Macro == LEGACY_CONSUMED_MACRO))
				{
					delta = true;

					if(consumed)
						Log.Warn("Duplicate \\Consumed");

					consumed = true;
				}

				if(piece.Length > 0 && piece[^1] is MacroName u && (u.Macro == USED_MACRO || u.Macro == LEGACY_USED_MACRO))
				{
					delta = true;

					if(used)
						Log.Warn("Duplicate \\Used");

					used = true;
				}

				if(delta)
					piece = piece.Slice(0, piece.Length - 1).TrimEnd();
			}

			var display = compiler.ToHTML(piece);
			var txt = compiler.ToString(piece);
			var (amount, matName) = extractMaterial(txt);

			var (price, reference) = Manifest.ResolveComponent(Log.At(piece), amount, matName);

			return new Component(display, consumed, used, price, reference);
		}).ToArray();

	Spell IGame<Spell>.ExtractLatexSpell(Compiler comp, Config.Book book, Chain<Token> body)
	{
		if(body.parseArguments(ArgType.SimpleSignature(3)) is not [ var _name, var _tag, var _prop ])
			throw new FormatException("Bad spell format, missing arguments to \\spell");

		bool combat = false;
		bool reaction = false;
		string name;

		{
			var nameWords = comp.ToString(_name).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			int wLen = nameWords.Length;
			recheck_combat:
			if(!combat && nameWords[wLen - 1] == "\\C")
			{
				combat = true;
				--wLen;
			}
			if(!reaction && nameWords[wLen - 1] == "\\R")
			{
				reaction = true;
				--wLen;
				goto recheck_combat;
			}

			name = string.Join(' ', nameWords.Take(wLen));
		}

		var tag = comp.ToString(_tag);
		var arcanum = tag.Split(':') switch {
			["arc", _] => throw new NotASpellException(),
			["rit", _] => Arcanum.Ritual,
			[_, "general",  _] => Arcanum.General,
			[_, "nature",  _] => Arcanum.Nature,
			[_, "ele",  _] => Arcanum.Elementalism,
			[_, "charm",  _] => Arcanum.Charms,
			[_, "divine",  _] => Arcanum.Divine,
			[_, "div",  _] => Arcanum.Divine,
			[_, "conj", _] => Arcanum.Conjuration,
			[_, "wytch", _] => Arcanum.Wytch,
			_ => throw new FormatException($"Unexpected label format {tag.Show()}")
		};

		HashSet<string> htmlKeys = [ "brief", "components", "crit", "effect", "fail", "duration" ];
		// these are used for table headers, so we avoid inserting formatting
		HashSet<string> plainKeys = [ "casting-time", "distance", "power-level" ];

		var prop = _prop.SplitBy(tk => tk is Character c && c.Char == ',', true)
			.Select(v => v
				.SplitOn(tk => tk is Character c && c.Char == '=')
				?? throw new FormatException("properties are not assignment list"))
			.ToDictionary(x => Lexer.Untokenize(x.left).Trim(), x => x.right.Trim());

		Func<string, Chain<Token>> getProp = p => prop.Remove(p, out var v) ? v : throw new FormatException($"Missing property '{p}'");

		// FIXME: skip compiling here
		var ct = processCastTime(name, comp.ToSafeString(getProp("casting-time")), ref reaction);

		string? extra = null;
		{
			var e = comp.ToHTML(body);

			if(! string.IsNullOrWhiteSpace(e))
				extra = e.Trim();
		}

		Spell spell = new(name, arcanum,
			Enum.Parse<PowerLevel>(comp.ToSafeString(getProp("power-level"))),
			combat,
			reaction,
			comp.ToSafeString(getProp("distance")),
			comp.ToHTML(getProp("duration")),
			ct,
			extractComponents(comp, getProp("components")),
			comp.ToHTML(getProp("brief")),
			comp.ToHTML(getProp("effect")),
			comp.ToHTML(getProp("crit")),
			comp.ToHTML(getProp("fail")),
			extra,
			book.Shorthand
		);

		if(prop.Count > 0) // TODO: give this its own log prefix
			comp.Log.Warn("Ignoring unknown properties: " + prop.Keys.ToArray().Show());

		return spell;
	}

	ISource<Spell> IGame<Spell>.Instantiate(Config.Source src)
	{
		return src switch
		{
			Config.CopySource s => new Copy<Spell>(this, s),
			Config.LatexSource l => new LatexFiles<Spell>(this, l),
			Config.OverleafSource o => new Overleaf<Spell>(this, o),
			Config.DndWikiSource => throw new ArgumentException("The DnDWiki doesn't have any Goedendag spells"),
			_ => throw new ArgumentException($"Invalid source for Goedendag: {src}")
		};
	}

	void IGame<Spell>.ExtractMaterials(Compiler comp, Chain<Token> code)
	{
		foreach(var table in code.extractEnvironments(env => comp.TryGetEnvironment(env.Env, out var k) && k == Compiler.KnownEnvironment.Tabular))
		{
			string[]? context = null;
			var inner = table;
			_ = inner.popArg(); // remove colspec

			foreach(var row in inner.SplitBy(tk => tk is BackBack, true))
			{
				try
				{
					foreach(var mat in extractMaterial(comp, row, context))
						Manifest.AddMaterial(mat);
				}
				catch(Exception ex)
				{
					Log.Warn($"Failed to parse material at {row[0].Pos}: {ex.Message}");
				}

				try
				{
					extractVariantDecl(comp, row, ref context);
				}
				catch(Exception ex)
				{
					Log.Warn($"Failed to parse \\grDeclareVariants at {row[0].Pos}: {ex.Message}");
				}


				foreach(var rule in row.extractInvocations("grPost", ArgType.SimpleSignature(4)))
				{
					try
					{
						if(rule is null)
							throw new FormatException("Missing arguments");

						if(! Glob.TryParse(Lexer.Untokenize(rule[0]), out var input))
							throw new FormatException("Invalid input pattern: " + rule[0]);

						var inAmt = parseAmount(comp.ToString(rule[1]));

						if(! Glob.TryParse(Lexer.Untokenize(rule[2]), out var output))
							throw new FormatException("Invalid output pattern: " + rule[2]);

						var outAmt = parseAmount(comp.ToString(rule[3]));

						Manifest.PostProcess(input, inAmt, output, outAmt);
					}
					catch(Exception ex)
					{
						Log.Warn($"Invalid \\grPost at {row[0].Pos}: {ex.Message}");
					}
				}
			}
		}
	}
}