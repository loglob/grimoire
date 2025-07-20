using Grimoire.Latex;
using Grimoire.Util;
using Olspy;
using System.Collections.Immutable;
using System.Text.Json;

using static Grimoire.Util.Extensions;
using static Grimoire.Config.LatexOptions;
using Code = Grimoire.Util.Chain<Grimoire.Latex.Token>;

namespace Grimoire;

public class Overleaf<TSpell> : ISource<TSpell>
{
	public readonly record struct InputFile(string path, string source, Code contents);
	public readonly record struct Context(Compiler compiler, List<InputFile> materialFiles, List<InputFile> codeFiles);


	private readonly Compiler latex;
	public IGame<TSpell> Game { get; }
	private readonly Config.OverleafSource config;
	private readonly Log log;
	private readonly Cache cache;
	private Context? storedContext = null;

	public Overleaf(IGame<TSpell> game, Config.OverleafSource config)
	{
		this.Game = game;
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

	private async ValueTask<Context> initialize()
	{
		if(storedContext.HasValue)
			return storedContext.Value;

		Project? project;
		ProjectSession? session = null;
		Protocol.FolderInfo? root = null;
		var lex = new Lexer(log);

		// maps source names onto paths
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
				var manifest = JsonSerializer.Deserialize<Dictionary<string, ImmutableList<string>>>(
					string.Join(' ', await session.GetDocumentByID(mf.ID)), Program.JsonOptions
				)!;
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

		if(files.Remove(Config.LatexOptions.MACROS_SOURCE_NAME, out var macroFiles))
		{
			foreach(var f in macroFiles)
			{
				if(byPath.TryGetValue(f, out var content))
					latex.LearnMacrosFrom(content);
				else
					log.Warn($"Missing macro source file {f}");
			}
		}

		List<string> missing = [];
		List<InputFile> matFiles = [];

		if(files.Remove(MATERIAL_SOURCE_NAME, out var _matFiles))
		{
			foreach(var file in _matFiles)
			{
				if(byPath.TryGetValue(file, out var code))
					matFiles.Add(new(file, MATERIAL_SOURCE_NAME, code));
				else
					missing.Add(file);
			}
		}

		List<InputFile> input = [];

		foreach(var kvp in files)
		{
			foreach(var file in kvp.Value)
			{
				if(byPath.TryGetValue(file, out var content))
					input.Add(new(file, kvp.Key, content));
				else
					missing.Add(file);
			}
		}

		if(missing.Count > 0)
			log.Warn($"{missing.Count} referenced files were not found: {string.Join(", ", missing)}");

		storedContext = new(latex, matFiles, input);
		return storedContext.Value;
	}

	async IAsyncEnumerable<TSpell> ISource<TSpell>.Spells()
	{
		var ctx = await initialize();

		foreach(var f in ctx.codeFiles)
		{
			foreach(var spell in ctx.compiler.ExtractSpells(Game, f.contents, f.source))
				yield return spell;
		}
	}

	async Task ISource<TSpell>.LoadMaterials()
	{
		var ctx = await initialize();

		if(ctx.materialFiles.Count == 0)
			return;

		foreach(var f in ctx.materialFiles)
		{
			int mCount = Game.Manifest.Materials.Count;
			int uCount = Game.Manifest.Units.Count;

			Game.ExtractMaterials(ctx.compiler, f.contents);

			if(mCount == Game.Manifest.Materials.Count && uCount == Game.Manifest.Units.Count)
				log.Warn($"File '{f.path}' does not define any materials");
		}
	}
}
