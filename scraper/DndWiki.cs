using HtmlAgilityPack;
using Grimoire.Util;

using static System.StringSplitOptions;
using static Grimoire.DnD5e;
using static Grimoire.Util.Extensions;

namespace Grimoire;

public record DndWiki(DnD5e Game, Config.Book[] Books, Config.DndWikiSource Cfg) : ISource<Spell>
{
	private readonly ScraperClient client = new("http://dnd5e.wikidot.com", Cfg.RateLimit);
	private static readonly Log log = Log.DEFAULT.AddTags("dndwiki");
	private readonly Cache cache = new(Cfg.CacheLifetime, log, "dndwiki");
	IGame<Spell> ISource<Spell>.Game => Game;

	public ValueTask<string[]> SpellNames()
		=> cache.CacheFunc("names", async() =>
		{
			var doc = await client.GetHtmlAsync("/spells");

			return doc.DocumentNode
				.Descendants("a")
				.Where(n => n.Attributes["href"]?.Value?.Contains("/spell:") ?? false)
				.Select(n => n.InnerText.Trim())
				// drop homebrew and UA
				.Where(n => !n.EndsWith( ')' ))
				.ToArray();
		});

	private async Task<Spell> details(string name)
	{
		string cName = new(string
			.Join('-', name.Split([ ' ', '/', ':' ]))
			.Where(c => c < 0x7F && c != '\'')
			.Select(Char.ToLower)
			.ToArray());
		var doc = await client.GetHtmlAsync($"/spell:{cName}");

		var content = doc.GetElementbyId("page-content");
		content.Clean();

		AssertEqual("div p p p p", string.Join(' ', content.ChildNodes.Take(5).Select(c => c.Name)),
			"bad page-content makeup");
		AssertEqual("div", content.ChildNodes.Last().Name, "bad page-content makeup");


		string source;
		{
			string[] ctl = content.ChildNodes[1].InnerText.Split(':', 2, TrimEntries);
			AssertEqual("Source", ctl[0], "Bad source format");
			source = Books.FindSource(ctl[1].Split('/')[0]).Shorthand;
		}


		bool ritual;
		School school;
		int level;
		(level, school, ritual) = ParseLevel(content.ChildNodes[2].InnerText);


		content.ChildNodes[3].Clean();
		var props = content.ChildNodes[3].ChildNodes.SplitBy(n => n.Name == "br").ToArray();

		if(props.Length != 4 || props.Any(p => p.Length < 2))
			throw new FormatException($"Expected 4 lines with at least 2 fields each; Got {props.Select(l => l.Length).Show()}");

		static string chkProb(HtmlNode[] pr, string f)
		{
			AssertEqual(f.ToLower() + ":", pr[0].InnerText.ToLower(), $"Bad {f} format");
			return string.Join(' ', pr.Skip(1).Select(x => x.InnerText.Trim()));
		}

		(string cTime, string? reaction) = MaybeSplitOn(chkProb(props[0], "casting time"), ",");
		string range = ParseParen(chkProb(props[1], "range")).Item1;
		(bool verbal, bool somatic, string? materials) = ParseComponents(chkProb(props[2], "components"));
		(bool concentration, string duration) = ParseDuration(chkProb(props[3], "duration"));

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
			AssertEqual("p", cs.Name, "Expected class list to be a paragraph");
			AssertEqual("spell lists", csTxt.Substring(0, 11).ToLower(), "Expected class list");

			rest.Remove(cs);
			classes = csTxt.Substring(12).Split((char[])[ ' ', ',' ], RemoveEmptyEntries).ToArray();
		}

		string? upcast;
		string desc = string.Join('\n', rest.Select(x => x.OuterHtml));
		{
			var d = rest.TakeWhile(x => !x.InnerText.TrimStart().StartsWith("at higher levels", StringComparison.CurrentCultureIgnoreCase)).ToList();
			var u = rest.Skip(d.Count).Select((x,i) => {
				if(i == 0)
				{
					x.Clean();
					string ahl = x.ChildNodes[0].InnerText.Trim().ToLower();
					AssertEqual("at higher levels", ahl.Length < 16 ? ahl : ahl.Substring(0, 16), "Bad upcast format");
					x.ChildNodes.RemoveAt(0);
				}
				return x;
			}).ToList();

			if(d.Count == 0)
				throw new FormatException("Empty description");

			desc = string.Join('\n', d.Select(x => x.OuterHtml));
			upcast = u.Count == 0 ? null : string.Join('\n', u.Select(s => s.OuterHtml));
		}

		return new Spell(name, source, school, level, cTime, reaction, ritual, range,
			verbal, somatic, materials, concentration, duration,
			desc, upcast, classes, statBlock, null);
	}

	public IAsyncEnumerable<Spell> Spells(IEnumerable<string> names)
		=> cache.CacheMany("spells", names, details).Select(kvp => kvp.val);

	public async IAsyncEnumerable<Spell> Spells()
	{
		var names = await this.SpellNames();
		log.Info($"Processing {names.Length} spells from DnDWiki...");

		await foreach(var s in Spells(names))
			yield return s;
	}
}
