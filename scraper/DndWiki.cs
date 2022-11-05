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
		string[] classes,
		string? statBlock
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

		if(props.Count() != 4 || props.Any(p => p.Count() < 2))
			throw new FormatException($"Expected 4 lines with at least 2 fields each; Got [{string.Join(' ', props.Select(l => l.Length.ToString()))}]");

		Func<HtmlNode[], string, string> chkProb = (pr, f) =>
		{
			Util.AssertEqual(f.ToLower() + ":", pr[0].InnerText.ToLower(), $"Bad {f} format");
			return string.Join(' ', pr.Skip(1).Select(x => x.InnerText.Trim()));
		};

		string cTime = chkProb(props[0], "casting time");

		string range;
		string? shape;
		(range, shape) = parseParen(chkProb(props[1], "range"));

		string components;
		string? materials;
		(components, materials) = parseParen(chkProb(props[2], "components"));

		bool concentration;
		string duration;
		{
			var d = chkProb(props[3], "duration");

			if(concentration = d.StartsWith("Concentration")) // don't trust the page's native whitespace
			{
				var spl = d.Split(null as char[], 4, RemoveEmptyEntries | TrimEntries);
				Util.AssertEqual("concentration, up to", string.Join(' ', spl.Take(3)).ToLower(), "Bad duration format");
				duration = spl[3];
			}
			else
				duration = d;
		}

		string[] pars = content.ChildNodes
			.Skip(4)
			.TakeWhile(x => x.Name == "p")
			.Select(n => n.InnerText.Trim())
			.ToArray();

		string? upcast;
		string desc;
		{
			var du = pars.SkipLast(1);
			var d = du.TakeWhile(x => !x.StartsWith("At Higher Levels."));
			var u = du.Skip(d.Count()).Select((s,i) => (i == 0) ? s.Substring(17).TrimStart() : s);

			if(! d.Any())
				throw new FormatException("Empty description");

			desc = string.Join('\n', d);
			upcast = u.Any() ? string.Join('\n', u) : null;
		}

		string[] classes;
		{
			var cs = pars[pars.Length - 1];
			Util.AssertEqual("spell lists", cs.Substring(0, 11).ToLower(), "Expected class list");
			classes = cs.Substring(12).Split(new[]{' ', ','}, RemoveEmptyEntries).ToArray();
		}

		string? statBlock = null;
		var trailing = content.ChildNodes.Skip(4 + pars.Length);

		if(trailing.First().Name == "table")
		{
			statBlock = trailing.First().OuterHtml;
			trailing = trailing.Skip(1);
		}
		else
			statBlock = null;

		Util.AssertEqual("div", string.Join(' ', trailing.Select(x => x.Name)),
			"Bad trailing tags");

		return new Spell(name, source, school, level, cTime, ritual, range, shape,
			components, materials, concentration, duration,
			desc, upcast, classes, statBlock);
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