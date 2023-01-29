
public class Copy : ISource
{
	readonly string[] files;

	public Copy(IEnumerable<string> files)
		=> this.files = files.ToArray();

#pragma warning disable CS1998
	public async IAsyncEnumerable<Spell> Spells()
	{
		Console.WriteLine($"Copying from {files.Length} file(s)...");
		int cp = 0;

		foreach (var f in files)
		{
			foreach (var spell in Util.LoadJson<List<Spell>>(f))
			{
				yield return spell;
				cp++;
			}
		}

		Console.WriteLine($"Copied {cp} spell(s).");
	}
#pragma warning restore CS1998
}