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

	public MaterialManifest Manifest { get; } = new();

	private static readonly ArgType[] signature = [ new OptionalArg(), new MandatoryArg() ];

	public Spell ExtractLatexSpell(Compiler comp, Config.Book book, Chain<Token> body)
	{
		var props = body.parseArguments(ArgType.SimpleSignature(8, Chain<Token>.Empty)) ?? throw new FormatException("Incomplete application of \\spell");

		string? hint = props[0].Items().All(tk => tk is WhiteSpace) ? null : comp.ToSafeString(props[0]);
		var name = comp.ToString(props[1]);
		var (level, school, ritual) = ParseLevel(comp.ToSafeString(props[2]));
		var spl = props[3].SplitOn(tk => tk is Character c && c.Char == ',');

		var (_left, right) = props[3].SplitOn(tk => tk is Character c && c.Char == ',').WithValue(
			(lr) => (lr.left, comp.ToHTML(lr.right)),
			(props[3], null)
		);
		var left = comp.ToSafeString(_left);

		var range = comp.ToSafeString(props[4]);
		var (verbal, somatic, material) = ParseComponents(comp.ToSafeString(props[5]));
		var (concentration, duration) = ParseDuration(comp.ToSafeString(props[6]));
		var classes = props[7]
			.SplitBy(tk => tk is WhiteSpace || (tk is Character c && c.Char == ','))
			.Where(seg => seg.Length > 0)
			.Select(comp.ToSafeString)
			.ToArray();

		var (_desc, _upcast) = comp.upcastAnchor is Token[] ua
			&& body.SplitOn(ua, (a,b) => a.IsSame(b)) is var (x,y)
				? (x, y.Just())
				: (body, null);

		return new Spell(
			name, book.Shorthand,
			school, level,
			left, right, ritual,
			range,
			verbal, somatic, material,
			concentration, duration,
			comp.ToHTML(_desc),
			_upcast.WithValue(comp.ToSafeString, null),
			classes,
			null,
			hint
		);
	}

	public ISource<Spell> Instantiate(Config.Source src)
		=> src switch {
			Config.CopySource c => new Copy<Spell>(this, c),
			Config.DndWikiSource w => new DndWiki(this, Conf.Books.Values.ToArray(), w),
			Config.LatexSource l => new LatexFiles<Spell>(this, l),
			Config.OverleafSource o => new Overleaf<Spell>(this, o),
			_ => throw new ArgumentException($"Illegal Source type for D&D 5e: {src}")
		};

}
