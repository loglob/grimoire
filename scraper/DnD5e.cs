using Grimoire.Latex;
using Grimoire.Util;

using static System.StringSplitOptions;
using static Grimoire.Util.Extensions;

namespace Grimoire;

public record DnD5e(Config.Game Conf) : IGame<DnD5e.Spell>
{
	public enum School
	{
		Abjuration,
		Conjuration,
		Divination,
		Enchantment,
		Evocation,
		Illusion,
		Necromancy,
		Transmutation
	}

	public readonly record struct Spell(
		string name, string source,
		School school, int level,
		string castingTime, string? reaction, bool ritual,
		string range,
		bool verbal, bool somatic, string? materials,
		bool concentration, string duration,
		string description, string? upcast,
		string[] classes,
		string? statBlock,
		string? hint
	) : ISpell
	{
		string ISpell.Source => source;
	}

	/// <summary>
	/// Parse one of
	/// 	[school] cantrip
	/// 	[level]th-level [school]
	/// possibly followed by '(ritual)'
	/// </summary>
	public static (int level, School school, bool ritual) ParseLevel(string input)
	{
		var lvlLine = input.Split(null as char[], RemoveEmptyEntries);

		if(lvlLine.Length < 2 || lvlLine.Skip(2).Any(x => x[0] != '('))
			throw new FormatException($"Invalid school/level format: got {input.Show()}");

		bool ritual = lvlLine.Length >= 3 && lvlLine[2].Equals("(ritual)", StringComparison.CurrentCultureIgnoreCase);
		int level;
		School school;

		if (lvlLine[1] == "cantrip")
		{
			level = 0;
			school = Enum.Parse<School>(lvlLine[0], true);
		}
		else
		{
			school = Enum.Parse<School>(lvlLine[1], true);
			var spl = lvlLine[0].Split('-',2);
			AssertEqual("level", spl[1].ToLower(), "Bad level format");
			level = int.Parse(spl[0].Substring(0, spl[0].Length - 2));
		}

		return (level, school, ritual);
	}

	public static (string, string?) ParseParen(string str)
	{
		if(str.EndsWith(')'))
		{
			var spl = str.Split('(', TrimEntries);
			AssertEqual(2, spl.Length, "Too many '('");

			return (spl[0], spl[1].Substring(0,spl[1].Length - 1));
		}
		else
			return (str, null);
	}

	public static (bool concentration, string duration) ParseDuration(string str)
	{
		str = str.Trim();
		bool conc = str.StartsWith("concentration", StringComparison.CurrentCultureIgnoreCase);

		if(conc) // don't trust the page's native whitespace
		{
			var spl = str.Split(null as char[], 4, RemoveEmptyEntries | TrimEntries);
			AssertEqual("concentration, up to",
				string.Join(' ', spl.Take(3).Select(s => s.ToLower())).ToLower(),
				"Bad duration format");

			return (conc, spl[3]);
		}
		else
			return (conc, str);

	}

	public static (bool verbal, bool somatic, string? material) ParseComponents(string text)
	{
		bool mat = false, verbal = false, somatic = false;
		(string components, string? materials) = ParseParen(text);

		foreach (var comp in components.Split(',', StringSplitOptions.TrimEntries))
		{
			switch(comp.ToLower())
			{
				case "v":
					AssertEqual(false, verbal, $"Component {comp} is redundant");
					verbal = true;
				break;

				case "s":
					AssertEqual(false, somatic, $"Component {comp} is redundant");
					somatic = true;
				break;

				case "m":
					AssertEqual(false, mat, $"Component {comp} is redundant");
					mat = true;
				break;

				default:
					throw new FormatException($"Unexpected component: {comp}");
			}
		}

		if(!mat)
			AssertEqual(null, materials, "Got materials despite no 'M' in components");
		else if(materials is null)
			throw new FormatException("Expected materials due to 'M' in components");

		return (verbal, somatic, materials);
	}

	public Log Log { get; } = Log.DEFAULT.AddTags(Conf.Shorthand);

	public Spell ExtractLatexSpell(Compiler comp, Config.Book book, Chain<Token> body)
	{
		var (_props, rest) = body.Args(1, 8);

		string? hint = _props[0].WithValue(comp.ToString, null);

		var props = _props.Skip(1).Select(x => {
			if(! x.HasValue)
				throw new FormatException("Partial spell properties");

			return comp.ToString(x.Value);
		}).ToArray();

		var name = props[0];
		var (level, school, ritual) = ParseLevel(props[1]);
		var (left, right) = MaybeSplitOn(props[2], ",");
		var range = props[3];
		var (verbal, somatic, material) = ParseComponents(props[4]);
		var (concentration, duration) = ParseDuration(props[5]);
		var classes = props[6].Split((char[])[ ' ', '\t', ',' ], RemoveEmptyEntries).ToArray();

		var (_desc, _upcast) = comp.upcastAnchor is Token[] ua && rest.SplitOn(ua, (a,b) => a.IsSame(b)) is var (x,y)
			? (x, (Chain<Token>?)y)
			: (rest, null);

		string desc = comp.ToHTML(_desc);

		return new Spell(
			name, book.Shorthand,
			school, level,
			left, right, ritual,
			range,
			verbal, somatic, material,
			concentration, duration,
			comp.ToHTML(_desc),
			_upcast.WithValue(comp.ToString, null),
			classes,
			null,
			hint
		);
	}

	public ISource<Spell> Instantiate(Config.Source src)
		=> src switch {
			Config.CopySource c => new Copy<Spell>(this, c),
			Config.DndWikiSource w => new DndWiki(Conf.Books.Values.ToArray(), w),
			Config.LatexSource l => new LatexFiles<Spell>(this, l),
			Config.OverleafSource o => new Overleaf<Spell>(this, o),
			_ => throw new ArgumentException($"Illegal Source type for D&D 5e: {src}")
		};

}
