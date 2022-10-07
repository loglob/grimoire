using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;

/// <summary>
/// A scraper for dnd-spells.com
/// </summary>
public class DndSpells
{
	private HttpClient client = new HttpClient() { BaseAddress = new Uri("https://www.dnd-spells.com") };

	/// <summary>
	/// The minimum time between HTTP requests
	/// </summary>
	public int RateLimit { get; init; } = 200;

	private static void clean(HtmlNode node)
	{
		node.InnerHtml = string.Join(' ',
			node.InnerHtml.Split(null as char[], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

		for (int i = node.ChildNodes.Count; i-- > 0;)
		{
			var c = node.ChildNodes[i];

			if(c.Name == "#text" && string.IsNullOrWhiteSpace(c.InnerText))
				node.RemoveChild(c);
			else
				clean(c);
		}
	}

	public readonly record struct SpellHeader(string name, int level, string school, string castingTime, bool ritual, bool concentration, string[] @class, string source);

	public readonly record struct Spell(
		string name, int level, string school,
		string castingTime, bool ritual,
		string range,
		string components,
		string duration, bool concentration,
		string[] @class,
		string source, int page,
		string description, string? upcast);

	private void checkTableHeader(HtmlNode header)
	{
		if(header is null)
			throw new FormatException("No table header found");

		if(header.FirstChild.Name != "tr")
			throw new FormatException($"Table header format not as expected: Expected thead.tr, got thead.{header.FirstChild.Name}");

		var fieldElems = header.FirstChild.ChildNodes.Skip(1).Select(n =>
		{
			if (n.Name != "th" || n.FirstChild?.Name != "#text")
				throw new FormatException($"Table header format not as expected: Expected thead.tr.th.#text, got thead.tr.{n.Name}.{n.FirstChild?.Name}");

			return n.FirstChild.InnerText.Trim();
		}).ToArray();

		var expected = typeof(SpellHeader).GetProperties().Select(p => p.Name.ToLower()).ToArray();

		if(!fieldElems.Select(n => string.Join("",n.ToLower().Split())).SequenceEqual(expected))
			throw new FormatException($"Unexpected header format; Expected { string.Join(' ', expected) }");
	}


	public async Task<SpellHeader[]> SpellHeaders()
	{
		const string cache = "cache/dnd-spells_SpellHeaders";

		if(File.Exists(cache))
		{
			try
			{
				if(await Util.LoadJsonAsync<SpellHeader[]>(cache) is SpellHeader[] x)
					return x;
			}
			catch(Exception) {}

			Console.Error.WriteLine("Cache Invalid; continuing");
		}

		var doc = await (client.GetDocumentAsync("/spells"));

		var table = doc.GetElementbyId("example");

		if(table is null)
			throw new FormatException("Failed parsing dnd-spells website: Spell table not found");

		clean(table);

		try
		{
			checkTableHeader(table.ChildNodes.FindFirst("thead"));
		}
		catch(Exception ex)
		{
			Console.Error.WriteLine(ex.Message);
			Console.Error.WriteLine("Continuing despite format error. Table header is:\n" + table.FirstChild.OuterHtml);
		}

		var spells = table.ChildNodes.FindFirst("tbody").ChildNodes.Where(cn => cn.Name == "tr")
			.Select(r => {
				var fields = r.ChildNodes.Where(c => c.Name == "td").Skip(1).Select(c => WebUtility.HtmlDecode(c.InnerText)).ToArray();

				if(fields.Length != 8)
					Console.Error.WriteLine($"Row with too many cells! Got {fields.Length}, expected 8");

				const string oinw = "(Open in new Window)";
				const string rit = "(Ritual)";

				if(fields[0].EndsWith(oinw, StringComparison.OrdinalIgnoreCase))
					fields[0] = fields[0].Substring(0, fields[0].Length - oinw.Length).Trim();

				if(fields[0].EndsWith(rit, StringComparison.OrdinalIgnoreCase))
				{
					Util.AssertEqual("yes", fields[4].ToLower(), "Ritual tags don't match");
					fields[0] = fields[0].Substring(0, fields[0].Length - rit.Length).Trim();
				}

				return new SpellHeader( fields[0], int.Parse(fields[1]), fields[2],
					fields[3], fields[4].ToLower() == "yes", fields[5].ToLower() == "yes",
					fields[6].Split(), fields[7]);
			}).ToArray();

		if(Directory.Exists("cache"))
		{
			using(var f = File.Create(cache))
				await JsonSerializer.SerializeAsync(f, spells);
		}

		return spells;
	}

	private async Task<Spell> spellDetails(SpellHeader header)
	{
		var id = string.Join('-', header.name
			.Split()
			.Select(w => new string(w
				.Where(c => Char.IsLetter(c) || c == '-')
				.Select(char.ToLower)
				.ToArray())
		)	);

		// detect poison & disease is incorrectly placed, so its hardcoded here
		if(header.ritual && id != "detect-poison-and-disease")
			id += "-ritual";

		var doc = await client.GetDocumentAsync($"/spell/{id}");
		var query = doc.DocumentNode.SelectNodes("//div[@class='col-md-12']");

		// a non-existing spell redirects to the index instead of emitting an error
		if(query is null)
			throw new FormatException($"Failed retrieving spell '{header.name}' from /spell/{id}");

		var body = query[0];

		clean(body);

		// split by horizontal lines
		var sections = body.ChildNodes.SplitBy(n => n.Name == "hr").ToArray();

		var htmlDesc = string.Join(' ', sections[1]
			.Where(n => !string.IsNullOrWhiteSpace(n.InnerText))
			.Select(n => n.InnerHtml));

		const string separator = "<span>At higher level</span>";

		if(htmlDesc.Contains("higher level") && !htmlDesc.Contains(separator))
			await Console.Error.WriteLineAsync("[WARN] Possibly failed parsing an upcast section");

		string[] splitDescriptions = htmlDesc.Split(separator, 2, StringSplitOptions.TrimEntries);

		var srcTxt = sections[2][0].InnerText.Split(null as char[], 3);
		Util.AssertEqual("Page:", srcTxt[0], "Bad source format");

		// parse properties
		{
			var properties = sections[0];

			Util.AssertEqual(header.school, properties.First(n => n.Name == "p").InnerText, "Wrong school");
			Util.AssertEqual("p", properties[properties.Length - 1].Name, "Bad page format");

			var props = properties[properties.Length - 1].ChildNodes
				.SplitBy(n => n.Name == "br", true)
				.ToArray();

			var keys = new[] { "Level", "Casting time", "Range", "Components", "Duration" };

			Util.AssertEqual(keys.Length, props.Length, "Wrong number of properties");

			for (int i = 0; i < keys.Length; i++)
				Util.AssertEqual(keys[i], props[i][0].InnerText.Split(':')[0], "Bad property name");

			Util.AssertEqual(header.level > 0 ? header.level.ToString() : "Cantrip", props[0][1].InnerText, "Bad spell level");
			Util.AssertEqual(header.castingTime, props[1][1].InnerText, "Bad casting time");

			var range = props[2][1].InnerText;
			var comp = props[3][1].InnerText;
			var dur = props[4][1].InnerText;

			const string consPrefix = "Concentration, up to";
			bool isCons = dur.StartsWith(consPrefix);

			Util.AssertEqual(header.concentration, isCons, "Bad concentration value");

			if(isCons)
				dur = dur.Substring(consPrefix.Length).Trim();

			if(splitDescriptions[0].StartsWith('(') && comp.Contains('M'))
			{
				var compDesc = splitDescriptions[0].Split(')', 2, StringSplitOptions.TrimEntries);
				comp += ' ' + compDesc[0] + ')';

				const string br = "<br>";
				splitDescriptions[0] = compDesc[1].StartsWith(br) ? compDesc[1].Substring(br.Length) : compDesc[1];
			}

			return new Spell( header.name, header.level, header.school,
				header.castingTime, header.ritual,
				range,
				comp,
				dur, header.concentration,
				header.@class,
				header.source, int.Parse(srcTxt[1]),
				splitDescriptions[0], splitDescriptions.Length > 1 ? splitDescriptions[1] : null );
		}
	}

	public async IAsyncEnumerable<Spell> SpellDetails(SpellHeader[] headers)
	{
		var mapped = new Dictionary<string, SpellHeader>();

		foreach(var h in headers)
		{
			if(!mapped.TryAdd(h.name, h))
				await Console.Error.WriteLineAsync($"[WARN] Dropping duplicate spell {h.name} from {h.source}");
		}

		const string cache = "cache/dnd-spells_Details";
		var output = new List<Spell>();

		if(File.Exists(cache))
		{
			Spell[]? cached = null;

			try
			{
				cached = await Util.LoadJsonAsync<Spell[]>(cache);
			}
			catch (Exception)
			{}

			if(!(cached is null))
			{
				foreach(var c in cached)
				{
					output.Add(c);

					if( mapped.Remove(c.name) )
						yield return c;
				}
			}
		}


		foreach(var v in mapped.Values)
		{
			var timer = Task.Delay(RateLimit);
			Spell? s = null;

			try
			{
				s = await spellDetails(v);
			} catch(Exception e)
			{
				Console.Error.WriteLine($"Error parsing spell '{v.name}': {e.Message}");
				Console.Error.WriteLine();
				//Console.Error.WriteLine(e.StackTrace);
			}

			if(s.HasValue)
			{
				output.Add(s.Value);
				yield return s.Value;
			}

			await timer;
		}

		if(Directory.Exists("cache"))
		{
			using(var f = File.Create(cache))
				await JsonSerializer.SerializeAsync(f, output);
		}
	}

}