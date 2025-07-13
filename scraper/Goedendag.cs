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

	public readonly record struct Spell(
		string name,
		Arcanum arcanum,
		PowerLevel powerLevel,
		bool combat,
		bool reaction,
		string distance,
		string duration,
		string castingTime,
		string components,
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

	private static Amount parseAmount(string str)
	{
		var m = amountRegex.Match(str);

		if(! m.Success)
			goto bad;

		var num = int.Parse(m.Groups[1].ValueSpan);
		var unit = m.Groups[2].ValueSpan;

		if(unit[0] == '[' || unit[^1] == ']')
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

			sum += coinMacros[((MacroName)spl.Value.at).Macro] + n;
			rest = spl.Value.right;
		}

		bad:
		throw new FormatException($"Invalid price '{Lexer.Untokenize(code)}'");
	}

	/// <summary>
	///  Extracts material definitions from a code line
	/// </summary>
	private static IEnumerable<Material> extractMaterial(MaterialManifest mf, Compiler comp, Chain<Token> line, string[]? ctx)
	{
		var cols = line.SplitBy(tk => tk is AlignTab, true).ToArray();

		// collect names
		var _names = line.extractInvocations("grMaterial").ToList();
		// normal unit references
		var unit = line.extractSingleInvocation("grUnit");
		var otherUnit = line.extractSingleInvocation("grOtherUnit");
		// variant reference
		var variant = line.extractSingleInvocation("grVariants");

		if(_names.Count > 0 && !unit.HasValue && !variant.HasValue)
			throw new FormatException(@"Incomplete \grMaterial definition");
		if(!unit.HasValue && otherUnit.HasValue)
			throw new FormatException(@"Using \grOtherUnit without \grUnit");
		if(unit.HasValue && variant.HasValue)
			throw new FormatException(@"Mixing \grUnit and \grVariants");

		var names = (_names.Count == 0 ? [cols[0]] : _names).Select(comp.ToString);

		if(unit.HasValue)
		{
			var price = parsePrice(cols[2]);
			var amt = parseAmount(comp.ToString(unit.Value));

			if(otherUnit.HasValue)
			{
				var otherAmt = parseAmount(comp.ToString(otherUnit.Value));

				if(mf.TryGetUnit(otherAmt.Unit, out var trUnit) && trUnit.Unit != MaterialManifest.DIMENSIONLESS_UNIT)
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

			var pieces = cols[2].SplitBy(tk => tk is Character c && c.Char == VARIANT_SEPARATOR).ToList();

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

	MaterialManifest IGame<Spell>.InitUnits()
	{
		var mf = new MaterialManifest();

		// TODO: un-hardcode this
		mf.AddBaseUnit("g");
		mf.AddBaseUnit("drop");
		mf.AddBaseUnit("cm");

		mf.AddUnit("drops", new(1, "drop")); // alias
		mf.AddUnit("kg", new(1000, "g"));
		mf.AddUnit("ml", new(2, "drop"));
		mf.AddUnit("l", new(1000, "ml"));
		mf.AddUnit("m", new(100, "cm"));

		return mf;
	}

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
			.Select(x => (key: Lexer.Untokenize(x.left).Trim(), val: x.right))
			.ToDictionary(
				x => x.key,
				x => (htmlKeys.Contains(x.key)
					? comp.ToHTML(x.val)
					: comp.ToSafeString(x.val)
				).Trim()
			);

		{
			var missing = htmlKeys.Concat(plainKeys).Where(k => !prop.ContainsKey(k));

			if(missing.Any())
				throw new FormatException("Missing properties: " + missing.Show());

			var unknown = prop.Keys.Where(pk => !htmlKeys.Contains(pk) && !plainKeys.Contains(pk));

			if(unknown.Any()) // TODO: give this its own log prefix
				comp.Log.Warn("Ignoring unknown properties: " + unknown.Show());
		}

		string? extra = null;

		{
			var e = comp.ToHTML(body);

			if(! string.IsNullOrWhiteSpace(e))
				extra = e.Trim();
		}

		var ct = processCastTime(name, prop["casting-time"], ref reaction);

		return new(name, arcanum, Enum.Parse<PowerLevel>(prop["power-level"]), combat, reaction,
			prop["distance"], prop["duration"], ct, prop["components"], prop["brief"],
			prop["effect"], prop["crit"], prop["fail"], extra, book.Shorthand);
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

	void IGame<Spell>.LearnMaterials(MaterialManifest mf, Compiler comp, Chain<Token> code)
	{
		foreach(var table in code.extractEnvironments("tblr"))
		{
			string[]? context = null;
			var inner = table;
			_ = inner.popArg(); // remove colspec

			foreach(var row in inner.SplitBy(tk => tk is BackBack, true))
			{
				try
				{
					foreach(var mat in extractMaterial(mf, comp, row, context))
					{
						Log.Info(mat.ToString());
						mf.AddMaterial(mat);
					}
				}
				catch(FormatException ex)
				{
					Log.Warn($"Failed to parse material at {row[0].Pos}: {ex.Message}");
				}

				try
				{
					extractVariantDecl(comp, row, ref context);
				}
				catch(FormatException ex)
				{
					Log.Warn($"Failed to parse \\grDeclareVariants at {row[0].Pos}: {ex.Message}");
				}


				foreach(var _rule in row.extractInvocations("grPost", ArgType.SimpleSignature(4)))
				{
					try
					{
						if(_rule is null)
							throw new FormatException("Missing arguments");

						var rule = _rule.Select(Lexer.Untokenize).ToArray();

						if(! Glob.TryParse(rule[0], out var input))
							throw new FormatException("Invalid input pattern: " + rule[0]);

						var inAmt = parseAmount(rule[1]);

						if(! Glob.TryParse(rule[2], out var output))
							throw new FormatException("Invalid output pattern: " + rule[2]);

						var outAmt = parseAmount(rule[3]);

						mf.PostProcess(input, inAmt, output, outAmt);
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