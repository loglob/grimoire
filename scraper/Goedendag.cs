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
		Wytch
	}

	public enum PowerLevel
	{
		Generalist,
		Petty,
		Lesser,
		Greater
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

	public Spell ExtractLatexSpell(Compiler comp, string source, Chain<Token> body)
	{
		if(body.Args(0, 3) is not ([ var _name, var _tag, var _prop ], var _extra) || _name is null || _tag is null || _prop is null)
			throw new FormatException("Bad spell format, missing arguments to \\spell");

		bool combat = false;
		bool reaction = false;
		string name;

		{
			var nameWords = comp.ToString(_name.Value).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
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

		Arcanum arcanum;

		{
			var tag = comp.ToString(_tag.Value);

			arcanum = tag.Split(':') switch {
				["arc", _] => throw new NotASpellException(),
				[_, "general",  _] => Arcanum.General,
				[_, "nature",  _] => Arcanum.Nature,
				[_, "ele",  _] => Arcanum.Elementalism,
				[_, "charm",  _] => Arcanum.Charms,
				[_, "divine",  _] => Arcanum.Divine,
				[_, "conj", _] => Arcanum.Conjuration,
				[_, "wytch", _] => Arcanum.Wytch,
				_ => throw new FormatException($"Unexpected label format {tag.Show()}")
			};
		}

		HashSet<string> htmlKeys = [ "crit", "effect", "fail" ];
		HashSet<string> plainKeys = [ "brief", "casting-time", "components", "distance", "duration", "power-level" ];

		var prop = _prop.Value.SplitBy(tk => tk is Character c && c.Char == ',', true)
			.Select(v => v
				.SplitOn(tk => tk is Character c && c.Char == '=')
				?? throw new FormatException("properties are not assignment list"))
			.Select(x => (key: Lexer.Untokenize(x.left).Trim(), val: x.right))
			.ToDictionary(x => x.key, x => (htmlKeys.Contains(x.key) ? comp.ToHTML(x.val) : comp.ToString(x.val)).Trim());

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
			var e = comp.ToHTML(_extra);

			if(! string.IsNullOrWhiteSpace(e))
				extra = e.Trim();
		}

		return new(name, arcanum, Enum.Parse<PowerLevel>(prop["power-level"]), combat, reaction,
			prop["distance"], prop["duration"], prop["casting-time"], prop["components"], prop["brief"],
			prop["effect"], prop["crit"], prop["fail"], extra, source);
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