using System.Text.Json;
using static Grimoire.Util.Extensions;

namespace Grimoire;

public class Copy<TSpell>(IGame<TSpell> game, Config.CopySource conf) : ISource<TSpell>
{
	readonly string[] files = [.. conf.From];
	readonly Log log = game.Log.AddTags(conf.Discriminate("copy"));

	public IGame<TSpell> Game { get; } = game;

	public async IAsyncEnumerable<TSpell> Spells()
	{
		log.Info($"Copying from {files.Length} file(s)...");
		int cp = 0;

		foreach (var f in files)
		{
			await using var o = File.OpenRead(f);
			bool warnedNull = false;

			await foreach (var spell in JsonSerializer.DeserializeAsyncEnumerable<TSpell>(o, Program.JsonOptions))
			{
				if(spell is null)
				{
					if(! warnedNull)
					{
						log.AddTags(f).Warn("null value in copy input");
						warnedNull = true;
					}

					continue;
				}

				yield return spell;
				++cp;
			}
		}

		log.Info($"Copied {cp} spell(s).");
	}
}