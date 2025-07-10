using Grimoire.Latex;
using Grimoire.Util;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Text;
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

	public Log Log { get; } = Log.DEFAULT.AddTags(Conf.Shorthand);

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

	public readonly struct Cost
	{
		private const int COPPER_PER_SILVER = 36;
		private const int SILVER_PER_GOLD = 12;
		private const int COPPER_PER_GOLD = SILVER_PER_GOLD * COPPER_PER_SILVER;

		public readonly int Gold, Silver, Copper;

		public int TotalCopper
			=> Gold * COPPER_PER_GOLD  +  Silver * COPPER_PER_SILVER  +  Copper;

		public Cost(int copper)
		{
			Copper = copper % COPPER_PER_SILVER;
			int silver = copper / COPPER_PER_SILVER;
			Silver = silver % SILVER_PER_GOLD;
			Gold = silver / SILVER_PER_GOLD;
		}

		public Cost(int gold, int silver, int copper) : this(gold*12*36 + silver*36 + copper)
		{ }

		public static bool operator >(Cost l, Cost r)
			=> l.TotalCopper > r.TotalCopper;
		public static bool operator <(Cost l, Cost r)
			=> l.TotalCopper < r.TotalCopper;
		public static bool operator >=(Cost l, Cost r)
			=> l.TotalCopper >= r.TotalCopper;
		public static bool operator <=(Cost l, Cost r)
			=> l.TotalCopper <= r.TotalCopper;
		public static bool operator ==(Cost l, Cost r)
			=> l.TotalCopper == r.TotalCopper;
		public static bool operator !=(Cost l, Cost r)
			=> l.TotalCopper != r.TotalCopper;

		public override bool Equals([NotNullWhen(true)] object? obj)
			=> obj is Cost c && this == c;

		public override int GetHashCode()
			=> TotalCopper;

		public static bool TryParse(Chain<Token> data, [MaybeNullWhen(false)] out Cost result)
		{
			int? cost = null;
			bool costFin = false;
			bool fin = false;
			result = default;

			foreach(var tk in data.Items())
			{
				if(tk is WhiteSpace)
				{
					if(cost.HasValue)
						costFin = true;
				}
				else if(tk is Character digit && char.IsDigit(digit.Char) && !costFin)
					cost = cost.GetValueOrDefault(0) * 10 + (digit.Char - '0');
				else if(tk is MacroName macro && cost.HasValue && !fin)
				{
					costFin = true;
					fin = true;
					switch(macro.Macro)
					{
						case "Cu":
							result = new(0, 0, cost.Value);
						break;
						case "Ag":
							result = new(0, cost.Value, 0);
							break;
						case "Au":
							result = new(cost.Value, 0, 0);
							break;

						default:
							return false;
					}
				}
				else
					return false;
			}

			return fin;
		}

		public override string ToString()
		{
			var b = new StringBuilder();

			if(Gold > 0)
			{
				b.Append(Gold);
				b.Append('G');
			}
			if(Silver > 0)
			{
				if(b.Length > 0)
					b.Append(' ');

				b.Append(Silver);
				b.Append('S');
			}
			if(Copper > 0 || b.Length == 0)
			{
				if(b.Length > 0)
					b.Append(' ');

				b.Append(Copper);
				b.Append('C');
			}

			return b.ToString();
		}
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

	public Spell ExtractLatexSpell(Compiler comp, Config.Book book, Chain<Token> body)
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

	public ISource<Spell> Instantiate(Config.Source src)
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

	private static Token[] units = [
		new MacroName("gr", default),
		new MacroName("drop", default)
	];

	private static Regex unitRegex = new(@"([0-9]+)\s*\[([a-z]+)]");


	public void LearnMaterials(Compiler comp, Chain<Token> body)
	{
		// there is a headline at the very start
		var veryFirstRow = true;

		foreach(var table in body.extractEnvironments("tblr"))
		{
			var inner = table;
			_ = inner.popArg();

			// this table format is fucked up...
			foreach(var row in inner.SplitBy(tk => tk is BackBack, true))
			{
				if(veryFirstRow)
				{
					veryFirstRow = false;
					continue;
				}

				var cols = row
					.SplitBy(tk => tk is AlignTab, true)
					.ToList();

				if(cols.Count != 3 || !Cost.TryParse(cols[2], out var cost))
					continue;

				var name = Regex.Replace(comp.ToString(cols[0]), @"(\n|\s)+", " ");
				var descr = comp.ToString(cols[1]);

				var um = unitRegex.Match(descr);

				if(! um.Success)
					continue;

				Console.WriteLine($"\"{name}\" costs {cost} per {um.Groups[1]} {um.Groups[2]}");
			}
		}
	}
}