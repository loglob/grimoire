using System.Text.Json;

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

public static class Program
{
	public static async Task DndSpells()
	{
		var sp = new DndSpells();
		var headers = await sp.SpellHeaders();

		Console.WriteLine($"Loaded {headers.Length} spell headers. Parsing details...");

		int cur = 0;
		int len = 0;

		await foreach (var d in sp.SpellDetails(headers))
		{
			Console.CursorLeft = 0;
			len = Math.Max(d.name.Length, len);

			Console.Write($"Parsed {++cur}/{headers.Length}: {d.name}{new string(' ', len - d.name.Length)}");
		}

		Console.WriteLine($"\nFinished scraping {cur} of {headers.Length} spells");
	}

	public static SourceBook FindSource(this IEnumerable<SourceBook> books, string source)
	{
		var b = books.Where(b => b.Matches(source)).ToArray();

		if(b.Length == 0)
			throw new Exception($"Unknown source: '{source}'");
		else if(b.Length > 1)
			throw new Exception($"Unknown source: '{source}': Ambigous between {string.Join(", ", b)}");
		else
			return b[0];
	}

	// note: http://dnd5e.wikidot.com/wondrous-items contains a full list of these aliases
	public static async Task<SourceBook[]> GetSources()
	{
		using(var f = File.OpenRead("sources.json"))
		{
			if(await JsonSerializer.DeserializeAsync<SourceBook[]>(f) is SourceBook[] ret)
				return ret;
			else
				throw new Exception();
		}
	}

	public static async Task Main(string[] args)
	{
		var wiki = new DndWiki();
		SourceBook[] sources = await GetSources();
		var db = sources.ToDictionary(x => x.shorthand, x => new List<DndWiki.Spell>());
		Console.WriteLine($"Found {sources.Length} sources");

		var n = await wiki.SpellNames();
		Console.WriteLine($"Processing {n.Length} spells...");

		await foreach(var x in wiki.Spells(n, sources))
		{
			db[x.source].Add(x);
		}

		Directory.CreateDirectory("./dbs");
		foreach (var kvp in db)
		{
			if(kvp.Value.Any())
				await kvp.Value.StoreJsonAsync($"./dbs/{kvp.Key}.json");
			else
				Console.WriteLine($"[Warn] No spells for source '{kvp.Key}'");
		}

		await sources.ToDictionary(s => s.shorthand, s => s.fullName).StoreJsonAsync("./dbs/index.json");
	}
}