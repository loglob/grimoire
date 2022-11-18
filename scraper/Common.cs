using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using static System.StringSplitOptions;

[JsonConverter(typeof(StringEnumConverter))]
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

public readonly record struct SourceBook(string fullName, string shorthand, string[]? alts)
{
	public bool Matches(string src)
	{
		var stripped = (string x) => x.Where(c => char.IsWhiteSpace(c) || char.IsLetter(c));
		var spaced = (string x) => x.Select(c => char.IsLetter(c) ? c : ' ').Squeeze();

		src = src.ToLower();

		if(src == shorthand.ToLower())
			return true;

		foreach(var name in (alts ?? Enumerable.Empty<string>()).Prepend(fullName))
		{
			var ln = name.ToLower();

			if(src == ln
				|| stripped(src).SequenceEqual(stripped(ln))
				|| spaced(src).SequenceEqual(spaced(ln)))
				return true;
		}

		return false;
	}
}

public readonly record struct Spell(
	string name, string source,
	School school, int level,
	string castingTime, string? reaction, bool ritual,
	string range,
	string components, string? materials,
	bool concentration, string duration,
	string description, string? upcast,
	string[] classes,
	string? statBlock
);


public static class Common
{
	/// <summary>
	/// Parse one of
	/// 	[school] cantrip
	/// 	[level]th-level [school]
	/// possibly followed by '(ritual)'
	/// </summary>
	public static (int level, School school, bool ritual) parseLevel(string input)
	{
		var lvlLine = input.Split(null as char[], StringSplitOptions.RemoveEmptyEntries);

		if(lvlLine.Length < 2 || lvlLine.Skip(2).Any(x => x[0] != '('))
			throw new FormatException("Invalid school/level format");

		bool ritual = lvlLine.Length >= 3 && lvlLine[2].ToLower() == "(ritual)";
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
			Util.AssertEqual(lvlLine[0].Substring(3), "-level", "Bad level format");
			level = lvlLine[0][0] - '0';

			if(level < 1 || level > 9)
				throw new FormatException($"Bad spell level {lvlLine[0][0]}");
		}

		return (level, school, ritual);
	}

	public static (string, string?) parseParen(string str)
	{
		if(str.EndsWith(')'))
		{
			var spl = str.Split('(', TrimEntries);
			Util.AssertEqual(2, spl.Length, "Too many '('");

			return (spl[0], spl[1].Substring(0,spl[1].Length - 1));
		}
		else
			return (str, null);
	}

	public static (bool concentration, string duration) parseDuration(string str)
	{
		str = str.Trim();
		bool conc = str.ToLower().StartsWith("concentration");

		if(conc) // don't trust the page's native whitespace
		{
			var spl = str.Split(null as char[], 4, RemoveEmptyEntries | TrimEntries);
			Util.AssertEqual("concentration, up to",
				string.Join(' ', spl.Take(3).Select(s => s.ToLower())).ToLower(),
				"Bad duration format");

			return (conc, spl[3]);
		}
		else
			return (conc, str);

	}

	public static (string left, string? right) maybeSplitOn(string str, string sep)
	{
		var spl = str.Split(sep, 2, TrimEntries);

		if(spl.Length > 1)
			return (spl[0], spl[1]);
		else
			return (spl[0], null);
	}
}