using Grimoire.Latex;
using Grimoire.Util;
using Olspy;
using System.Text.Json;

using static Grimoire.Util.Extensions;

namespace Grimoire;

public class Overleaf<TSpell> : ISource<TSpell>
{
	private readonly Compiler latex;
	private readonly IGame<TSpell> game;
	private readonly Config.OverleafSource config;
	private readonly Log log;
	private readonly Cache cache;

	public Overleaf(IGame<TSpell> game, Config.OverleafSource config)
	{
		this.game = game;
		this.log = game.Log.AddTags(config.Discriminate("overleaf"));
		this.cache = new(config.CacheLifetime, log, game.Conf.Shorthand, "overleaf", config.Auth.CacheID);

		this.latex = new(config.Latex.Options, log);
		this.config = config;

		foreach (var f in config.LocalMacros)
			latex.LearnMacrosFrom(File.ReadLines(f), f);

	}

	private async Task<(Project project, ProjectSession session, Protocol.FolderInfo root)> open()
	{
		var project = await config.Auth.Instantiate()!;
		var session = await project.Join()!;
		var root = (await session.GetProjectInfo()).Project.RootFolder[0]!;
		return (project, session, root);
	}

	public async IAsyncEnumerable<TSpell> Spells()
	{
		Project? project;
		ProjectSession? session = null;
		Protocol.FolderInfo? root = null;
		var lex = new Lexer(log);

		var files = config.Latex.Files.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToHashSet());

		// manifest has to be loaded eagerly
		if(config.Latex.LocalManifest is string p)
		{
			(project, session, root) = await open();

			var mf = root.Lookup(p);

			if(mf is null)
				log.Warn("Cannot open manifest path");
			else
			{
				var manifest = JsonSerializer.Deserialize<Dictionary<string, string[]>>(string.Join(' ', await session.GetDocumentByID(mf.ID)), Config.JsonOpt)!;
				files.UnionAll(manifest);
			}
		}

		var byPath = await cache.CacheMany(
				"files",
				files.Values.SelectMany(x => x),
				async path => {
					if(root is null || session is null)
						(project, session, root) = await open();

					var f = root.Lookup(path) ?? throw new ArgumentException($"Path doesn't exist: {path}");

					return await session.GetDocumentByID(f.ID);
				}
			).Select(kvp => (kvp.key, val: lex.Tokenize(kvp.val, kvp.key)))
			.Select(kvp => (kvp.key, val: kvp.val.DocumentContents() ?? kvp.val))
			.ToDictionaryAsync(kvp => kvp.key, kvp => kvp.val);

		HashSet<string> missing = [];

		if(files.Remove(Config.LatexOptions.MACROS_SOURCE_NAME, out var macroFiles))
		{
			foreach(var f in macroFiles)
			{
				if(byPath.TryGetValue(f, out var content))
					latex.LearnMacrosFrom(content);
				else
					missing.Add(f);
			}
		}

		if(files.Remove(Config.LatexOptions.MATERIAL_SOURCE_NAME, out var materialFiles))
		{
			foreach(var f in materialFiles)
			{
				if(byPath.TryGetValue(f, out var content))
					game.LearnMaterials(content);
				else
					missing.Add(f);
			}
		}

		foreach (var kvp in files)
		{
			foreach (var f in kvp.Value)
			{
				if(byPath.TryGetValue(f, out var tks))
				{
					foreach (var spell in latex.ExtractSpells(game, tks, kvp.Key))
						yield return spell;
				}
				else
					missing.Add(f);
			}
		}

		if(session is not null)
			await session.DisposeAsync();

		if(missing.Count > 0)
			log.Warn($"{missing.Count} referenced files were not found: {string.Join(", ", missing)}");
	}
}
