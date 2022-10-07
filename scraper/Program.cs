public record Spell(string name,
	string castingTime,
	string range, string components, string duration, string source, string description, string? upcast);

public static class Program
{
	public static async Task Main(string[] args)
	{
		var sp = new DndSpells();
		var headers = await sp.SpellHeaders();

		Console.WriteLine($"Loaded {headers.Length} spell headers. Parsing details...");

		int cur = 0;
		int len = 0;
		int pos = Console.CursorTop;

		await foreach (var d in sp.SpellDetails(headers))
		{
			Console.CursorLeft = 0;
			len = Math.Max(d.name.Length, len);

			Console.Write($"Parsed {++cur}/{headers.Length}: {d.name}{new string(' ', len - d.name.Length)}");
		}

		Console.WriteLine($"\nFinished scraping {cur} of {headers.Length} spells");
	}
}