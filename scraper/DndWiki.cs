using HtmlAgilityPack;

public class DndWiki
{
	private HttpClient client = new HttpClient() { BaseAddress = new Uri("http://dnd5e.wikidot.com") };

	public readonly record struct SpellHeader(
		string name, int level,
		string school, string? source,
		string castingTime, bool ritual,
		string range, string? shape,
		string duration, bool concentration,
		string components);

	private IEnumerable<SpellHeader> spellLevel(HtmlDocument doc, int level)
	{
		var tab = doc.GetElementbyId($"wiki-tab-0-{level}");

		if(tab is null)
			throw new FormatException("Bad page format: Can't locate spell tab");

		var tbody = tab.FirstChild.FirstChild;

		Util.AssertEqual("table", tbody.Name, "Bad page format: Can't locate spell table");

		if(tbody is null)
			throw new FormatException("Bad page format: Can't locate table body");

		{
			var fields = new[] { "Spell Name", "School", "Casting Time", "Range", "Duration", "Components" };
			var headers = tbody.FirstChild.ChildNodes;

			Util.AssertEqual(fields.Length, headers.Count, "Bad header format");

			for (int i = 0; i < headers.Count; i++)
				Util.AssertEqual(fields[i], headers[i].InnerText, "Bad header title");
		}

		foreach (var r in tbody.ChildNodes.Skip(1))
		{
			var src = r.ChildNodes[1].FirstChild.Element("sup")?.InnerText;
			var school = r.ChildNodes[1].InnerText;

			if(src is string s)
				school = school.Substring(0, school.Length - s.Length).Trim();

			var name = r.FirstChild.InnerText;

			if(name.EndsWith(')'))
				name = name.Split('(', 2, StringSplitOptions.TrimEntries)[0];

			var time = r.ChildNodes[2].InnerText;
			var ritual = time.EndsWith(" R");

			if(ritual)
				time = time.Substring(0, time.Length - 2).Trim();

			var rangeShape = r.ChildNodes[3].InnerText.Split('(', 2, StringSplitOptions.TrimEntries);

			var duration = r.ChildNodes[4].InnerText;
			const string concStr = "Concentration, up to";
			bool conc = duration.StartsWith(concStr);

			if(conc)
				duration = duration.Substring(concStr.Length).Trim();

			yield return new SpellHeader(name, level,
				school, src,
				time, ritual,
				rangeShape[0], (rangeShape.Length < 2) ? null :  rangeShape[1].Substring(0, rangeShape[1].Length - 1).Trim(),
				duration, conc,
				r.ChildNodes[5].InnerText);
		}


	}

	public async Task<SpellHeader[]> SpellHeaders()
	{
		const string cache = "cache/wikidot_headers";

		if(File.Exists(cache))
		{
			try
			{
				if(await Util.LoadJsonAsync<SpellHeader[]>(cache) is SpellHeader[] sh)
					return sh;
			} catch(Exception)
			{}

			Console.Error.WriteLine("[WARN] Invalid cache");
		}

		var doc = await client.GetDocumentAsync("/spells");
		doc.DocumentNode.Clean();

		var buf = new SpellHeader[10][];

		Parallel.For(0, 10, i => buf[i] = spellLevel(doc, i).ToArray());

		var ret = new SpellHeader[buf.Sum(x => x.Length)];
		int c = 0;

		foreach(var b in buf)
		{
			Array.Copy(b, 0, ret, c, b.Length);
			c += b.Length;
		}

		if(Directory.Exists("cache"))
			await ret.StoreJsonAsync(cache);

		return ret;
	}
}