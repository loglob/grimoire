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

	public static SourceBook FindSource(IEnumerable<SourceBook> books, string source)
	{
		var b = books.Where(b => b.Matches(source)).ToArray();

		if(b.Length == 0)
			throw new Exception($"Unknown source: '{source}'");
		else if(b.Length > 1)
			throw new Exception($"Unknown source: '{source}': Ambigous between {string.Join(", ", b)}");
		else
			return b[0];
	}

	public static async Task Main(string[] args)
	{
		var sources = (await new DndWiki().SpellHeaders())
			.Select(s => s.source)
			.Concat((await new DndSpells().SpellHeaders())
				.Select(s => s.source));

		var books = await Util.LoadJsonAsync<SourceBook[]>("sources.json");
		var maps = new Dictionary<string, SourceBook>();

		foreach (var s in sources)
		{
			if(s is null)
				continue;
			try
			{
				maps.TryAdd(s, FindSource(books, s));
			}
			catch (Exception ex)
			{
				await Console.Error.WriteLineAsync(ex.Message);
			}
		}

		foreach(var kvp in maps)
			Console.WriteLine($"{kvp.Key} -> {kvp.Value}");
	}
}