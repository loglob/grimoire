﻿public readonly record struct SourceBook(string fullName, string shorthand, string[]? alts)
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
	string name,
	string source,
	School school, int level,
	string castingTime, bool ritual,
	string range, string? shape,
	string components, string? materials,
	bool concentration, string duration,
	string description, string? upcast,
	string[] classes,
	string? statBlock
);

public static class Program
{
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
	public static SourceBook[] GetSources()
	    => Util.LoadJson<SourceBook[]>("sources.json");
	

	public static async Task Main(string[] args)
	{
		var wiki = new DndWiki();
		SourceBook[] sources = GetSources();
		var db = sources.ToDictionary(x => x.shorthand, x => new List<Spell>());
		Console.WriteLine($"Found {sources.Length} sources");
/*
		var n = await wiki.SpellNames();
		Console.WriteLine($"Processing {n.Length} spells from DnDWiki...");

		await foreach(var x in wiki.Spells(n, sources))
		{
			db[x.source].Add(x);
		}*/

		var ol = new Overleaf(Util.LoadJson<Overleaf.Config>("overleaf.json"));
		var hb = db["HB"];

		foreach(var s in await ol.Spells("HB", false))
			hb.Add(s);

		Directory.CreateDirectory("./dbs");
		foreach (var kvp in db)
		{
			if(kvp.Value.Any())
				kvp.Value.StoreJson($"./dbs/{kvp.Key}.json");
			else
				Console.WriteLine($"[Warn] No spells for source '{kvp.Key}'");
		}

		sources.ToDictionary(s => s.shorthand, s => s.fullName).StoreJson("./dbs/index.json");
	}
}