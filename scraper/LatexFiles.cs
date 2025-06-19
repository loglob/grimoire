using Grimoire.Latex;
using Grimoire.Util;
using System.Text.Json;

namespace Grimoire;

/// <summary>
/// Scraper for processing LaTeX files
/// </summary>
public record LatexFiles<TSpell>(IGame<TSpell> Game, Config.LatexSource Conf) : ISource<TSpell>
{
	readonly Log log = Game.Log.AddTags(Conf.Discriminate("local"));

	public IAsyncEnumerable<TSpell> Spells()
	{
		var comp = new Compiler(Conf.Options, log);
		var lex = new Lexer(log);

		var macroFiles = Conf.MacroFiles.ToHashSet();
		var files = Conf.Files.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToHashSet());

		if(Conf.LocalManifest is not null)
		{
			using var f = File.OpenRead(Conf.LocalManifest);
			var manifest = JsonSerializer.Deserialize<Config.LatexManifest>(f, Config.JsonOpt);

			if(manifest!.Files is not null)
				files.UnionAll(manifest.Files);

			if(manifest!.MacroFiles is not null)
				macroFiles.AddRange(manifest.MacroFiles);
		}

		foreach(var f in macroFiles)
			comp.LearnMacrosFrom(File.ReadLines(f), f);

		var segments = files
			.SelectMany(kvp => kvp.Value.Select(file => (kvp.Key, file)))
			.Select(x => (source: x.Key, code: lex.Tokenize(File.ReadLines(x.file), x.file) ))
			.ToList();

		foreach (var (_, code) in segments.Where(seg => seg.source == Config.LatexOptions.MACROS_SOURCE_NAME))
			comp.LearnMacrosFrom(code);

		return segments
			.Where(seg => seg.source != Config.LatexOptions.MACROS_SOURCE_NAME)
			.SelectMany(seg => comp.ExtractSpells(Game, seg.code, seg.source))
			.ToAsyncEnumerable();
	}
}