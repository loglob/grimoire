using Latex;
using Olspy;
using System.Text.Json;
using Util;

using static Util.Extensions;

public class Overleaf<TSpell> : ISource<TSpell>
{
	private readonly Compiler latex;
	private readonly IGame<TSpell> game;
	private readonly Config.OverleafSource config;
	private readonly Log log;

	public Overleaf(IGame<TSpell> game, Config.OverleafSource config)
	{
		this.game = game;
		this.log = Log.DEFAULT.AddTags(game.Conf.Shorthand, "overleaf");

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

		var macroFiles = config.Latex.MacroFiles.ToHashSet();
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
				var manifest = JsonSerializer.Deserialize<Config.LatexManifest>(string.Join(' ', await session.GetDocumentByID(mf.ID)), Config.JsonOpt)!;

				if(manifest.MacroFiles is not null)
					macroFiles.AddRange(manifest.MacroFiles);

				if(manifest.Files is not null)
					files.UnionAll(manifest.Files);
			}
		}

		var byPath = await PartiallyCached(
				$"cache/{game.Conf.Shorthand}_overleaf_root_{config.Auth.CacheID}",
				files.Values.SelectMany(x => x)
					.Concat(macroFiles),
				log,
				async path => {
					if(root is null || session is null)
						(project, session, root) = await open();

					var f = root.Lookup(path) ?? throw new ArgumentException($"Path doesn't exist: {path}");

					return await session.GetDocumentByID(f.ID);
				}
			).Select(kvp => (kvp.key, val: new ArraySegment<Token>(lex.Tokenize(kvp.val, kvp.key))))
			.Select(kvp => (kvp.key, val: kvp.val.DocumentContents() ?? kvp.val))
			.ToDictionaryAsync(kvp => kvp.key, kvp => kvp.val);

		List<string> missing = [];

		foreach (var f in macroFiles)
		{
			if(byPath.TryGetValue(f, out var content))
				latex.LearnMacrosFrom(content);
			else
				missing.Add(f);
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
