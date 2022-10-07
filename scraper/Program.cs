

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

	public static async Task Main(string[] args)
	{
		await DndSpells();
	}
}