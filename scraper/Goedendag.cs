using Grimoire.Latex;
using Grimoire.Util;

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

	public readonly record struct Component(
		string display,
		bool consumed,
		bool used
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

			if(! _spl.Bind(out var spl))
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

	private Component[] extractComponents(Compiler compiler, Chain<Token> code)
		=> separateComponents(
				code.Length > 2 && code[0] is OpenBrace && code[^1] is CloseBrace
					? code.Slice(1, code.Length - 2) : code
		).Select(piece => {
			bool delta = true;
			bool consumed = false;
			bool used = false;

			Log.Info(Lexer.Untokenize(piece));

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

			return new Component(compiler.ToHTML(piece), consumed, used);
		}).ToArray();


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
}