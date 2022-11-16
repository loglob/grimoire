using System.Text;
using HtmlAgilityPack;
using static System.StringSplitOptions;

public class DndWiki
{
	private HttpClient client = new HttpClient() { BaseAddress = new Uri("http://dnd5e.wikidot.com") };


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


	private async Task<Spell> details(string name, IEnumerable<SourceBook> sources)
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
			source = sources.FindSource(ctl[1].Split('/')[0]).shorthand;
		}


		bool ritual;
		School school;
		int level;
		(level, school, ritual) = Common.parseLevel(content.ChildNodes[2].InnerText);


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

		string range = Common.parseParen(chkProb(props[1], "range")).Item1;

		string components;
		string? materials;
		(components, materials) = Common.parseParen(chkProb(props[2], "components"));

		bool concentration;
		string duration;
		(concentration, duration) = Common.parseDuration(chkProb(props[3], "duration"));

		var rest = content.ChildNodes.Skip(4).SkipLast(1).ToList();
		string? statBlock;

		if(rest.Last().Name == "table")
		{
			var x = rest.Last();
			rest.Remove(x);
			statBlock = x.OuterHtml;
		}
		else
			statBlock = null;

		string[] classes;
		{
			var cs = rest.Last();
			var csTxt = cs.InnerText;
			Util.AssertEqual("p", cs.Name, "Expected class list to be a paragraph");
			Util.AssertEqual("spell lists", csTxt.Substring(0, 11).ToLower(), "Expected class list");

			rest.Remove(cs);
			classes = csTxt.Substring(12).Split(new[]{' ', ','}, RemoveEmptyEntries).ToArray();
		}

		string? upcast;
		string desc = string.Join('\n', rest.Select(x => x.OuterHtml));
		{
			var d = rest.TakeWhile(x => !x.InnerText.TrimStart().ToLower().StartsWith("at higher levels"));
			var u = rest.Skip(d.Count()).Select((x,i) => {
				if(i == 0)
				{
					x.Clean();
					string ahl = x.ChildNodes[0].InnerText.Trim().ToLower();
					Util.AssertEqual("at higher levels", ahl.Length > 17 ? ahl : ahl.Substring(0, 16), "Bad upcast format");
					x.ChildNodes.RemoveAt(0);
				}
				return x;
			}).ToList();

			if(!d.Any())
				throw new FormatException("Empty description");

			desc = string.Join('\n', d.Select(x => x.OuterHtml));
			upcast = u.Any() ? string.Join('\n', u.Select(s => s.OuterHtml)) : null;
		}

		return new Spell(name, source, school, level, cTime, ritual, range,
			components, materials, concentration, duration,
			desc, upcast, classes, statBlock);
	}

	public IAsyncEnumerable<Spell> Spells(IEnumerable<string> names, IEnumerable<SourceBook> sources)
		=> Util.PartiallyCached("cache/wikidot_spells", names, async (string n) =>
		{
			var timer = Task.Delay(RateLimit);
			var x = await details(n, sources);
			await timer;
			return x;
		}, x => x);
}