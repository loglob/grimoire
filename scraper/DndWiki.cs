using System.Text;
using HtmlAgilityPack;
using static System.StringSplitOptions;

public class DndWiki
{
	private HttpClient client = new HttpClient() { BaseAddress = new Uri("http://dnd5e.wikidot.com") };

	public readonly record struct Spell(
		string name,
		string? source,
		School school, int level,
		string castingTime, bool ritual,
		string range, string? shape,
		string components, string? materials,
		bool concentration, string duration,
		string description,
		string? upcast,
		string[] classes
	);

	/// <summary>
	/// The minimum time between HTTP requests
	/// </summary>
	public int RateLimit { get; init; } = 200;

	public Task<string[]> SpellNames()
		=> Util.Cached("cache/wikidot_names", async () =>
		{
			var doc = await client.GetDocumentAsync("/spells");

			return doc.DocumentNode
				.Descendants("a")
				.Where(n => n.Attributes["href"]?.Value?.Contains("/spell:") ?? false)
				.Select(n => n.InnerText.Trim())
				// drop homebrew and UA
				.Where(n => !n.EndsWith( ')' ))
				.ToArray();
		});

	private static (string, string?) parseParen(string str)
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

	private async Task<Spell> details(string name)
	{
		string cName = new string(string.Join('-', name.Split(new[]{ ' ', '/', ':' }))
			.Where(c => c < 0x7F && c != '\'')
			.Select(Char.ToLower)
			.ToArray());
		var doc = await client.GetDocumentAsync($"/spell:{cName}");

		var content = doc.GetElementbyId("page-content");
		content.Clean();

		Util.AssertEqual("div p p p p", string.Join(' ', content.ChildNodes.Take(5).Select(c => c.Name)),
			"bad page-content makeup");
		Util.AssertEqual("div", content.ChildNodes.Last().Name, "bad page-content makeup");


		string source;
		{
			var ctl = content.ChildNodes[1].InnerText.Split(':', 2, TrimEntries);
			Util.AssertEqual("Source", ctl[0], "Bad source format");
			source = ctl[1];
		}


		bool ritual;
		School school;
		int level;
		{
			var lvlLine = content.ChildNodes[2].InnerText.Split();

			if(lvlLine.Length < 2 || lvlLine.Skip(2).Any(x => x[0] != '('))
				throw new FormatException("Invalid school/level format");

			ritual = lvlLine.Length >= 3 && lvlLine[2].ToLower() == "(ritual)";

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
		}


		content.ChildNodes[3].Clean();
		var props =  content.ChildNodes[3].ChildNodes.SplitBy(n => n.Name == "br").ToArray();

		Util.AssertEqual("2 2 2 2", string.Join(' ', props.Select(l => l.Length.ToString())),
			"Expected 4 lines á 2 fields");

		Util.AssertEqual("Casting Time:", props[0][0].InnerText, "Bad casting time format");
		string cTime = props[0][1].InnerText.Trim();

		string range;
		string? shape;
		Util.AssertEqual("Range:", props[1][0].InnerText, "Bad range/shape format");
		(range, shape) = parseParen(props[1][1].InnerText.Trim());

		string components;
		string? materials;
		Util.AssertEqual("Components:", props[2][0].InnerText, "Bad components format");
		(components, materials) = parseParen(props[2][1].InnerText.Trim());

		bool concentration;
		string duration;
		{
			Util.AssertEqual("Duration:", props[3][0].InnerText, "Bad duration format");
			var d = props[3][1].InnerText.Trim();

			if(concentration = d.StartsWith("Concentration")) // don't trust the page's native whitespace
			{
				var spl = d.Split(null as char[], 4, RemoveEmptyEntries | TrimEntries);
				Util.AssertEqual("concentration, up to", string.Join(' ', spl.Take(3)).ToLower(), "Bad duration format");
				duration = spl[3];
			}
			else
				duration = d;
		}

		string? upcast;
		string desc;
		{
			var du = content.ChildNodes.Skip(4).SkipLast(2).Select(n => n.InnerText.Trim()).ToArray();
			var d = du.TakeWhile(x => !x.StartsWith("At Higher Levels."));
			var u = du.Skip(d.Count()).Select((s,i) => (i == 0) ? s.Substring(17).TrimStart() : s);

			if(! d.Any())
				throw new FormatException("Empty description");

			desc = string.Join('\n', d);
			upcast = u.Any() ? string.Join('\n', u) : null;
		}

		string[] classes;
		{
			var cs = content.ChildNodes[content.ChildNodes.Count - 2].InnerText;
			Util.AssertEqual("spell lists", cs.Substring(0, 11).ToLower(), "Expected class list");
			classes = cs.Substring(12).Split(new[]{' ', ','}, RemoveEmptyEntries).ToArray();
		}

		return new Spell(name, source, school, level, cTime, ritual, range, shape,
			components, materials, concentration, duration,
			desc, upcast, classes);
	}

	public IAsyncEnumerable<Spell> Spells(IEnumerable<string> names)
		=> Util.PartiallyCached("cache/wikidot_spells", names, async (string n) =>
		{
			var timer = Task.Delay(RateLimit);
			var x = await details(n);
			await timer;
			return x;
		}, x => x);
}