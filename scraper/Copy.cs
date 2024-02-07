using static Util.Extensions;

public class Copy<TSpell> : ISource<TSpell>
{
	readonly string[] files;
	readonly Log log;

	public Copy(IGame<TSpell> game, Config.CopySource conf)
	{
		this.log = game.Log.AddTags(conf.Discriminate("copy"));
		this.files = conf.From.ToArray();
	}

#pragma warning disable CS1998
	public async IAsyncEnumerable<TSpell> Spells()
	{
		log.Info($"Copying from {files.Length} file(s)...");
		int cp = 0;

		foreach (var f in files)
		{
			foreach (var spell in LoadJson<List<TSpell>>(f))
			{
				yield return spell;
				cp++;
			}
		}

		log.Info($"Copied {cp} spell(s).");
	}
#pragma warning restore CS1998
}