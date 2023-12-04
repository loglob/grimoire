using Olspy;
using static Util.Extensions;
using Latex;
using Util;

public class Overleaf<TSpell> : ISource<TSpell>
{
	private readonly Olspy.Overleaf overleaf;
	private readonly Olspy.Project project;
	private readonly Compiler latex;
	private readonly IGame<TSpell> game;
	private readonly string includeAnchor;

	public Overleaf(IGame<TSpell> game, Config.OverleafSource config)
	{
		this.game = game;
		this.overleaf = (config.Host is string s) ?
			this.overleaf = new Olspy.Overleaf(s) :
			Olspy.Overleaf.RunningInstance;

		this.project = this.overleaf.Open(config.ProjectID);
		this.latex = new(config.Latex);
		this.includeAnchor = config.IncludeAnchor;

		if(config.User is string u)
			this.overleaf.SetCredentials(config.Password, u);
		else
			this.overleaf.SetCredentials(config.Password);

		foreach (var f in config.localMacros)
			latex.LearnMacrosFrom(File.ReadLines(f), f);
	}

	/// <summary>
	/// Retrieves all code segments from multiple documents.
	/// Filters out documents without an INCLUDE_ANCHOR
	/// </summary>
	internal IEnumerable<(string source, ArraySegment<Token> code)> GetCodeSegments(IEnumerable<Document> docs)
		=> docs.SelectWith(d => d.Lines
				.Take(10)
				.FirstOrDefault(x => x.StartsWith(includeAnchor))
				?.Substring(includeAnchor.Length)
				?.Trim())
			.Where(x => x.Item2 is not null)
			.Select(x => (source: x.Item2!, doc: new ArraySegment<Token>(Lexer.Tokenize(x.Item1.Lines, x.Item1.ID ?? "<unknown overleaf file>"))))
			.Select(x => (x.source, x.doc.DocumentContents() ?? x.doc) );

	public async IAsyncEnumerable<TSpell> Spells()
	{
		var docs = await Cached($"cache/{game.Conf.Shorthand}_overleaf_documents_{project.ID}", async() => {
			if(!await overleaf.Available)
				throw new Exception($"Overleaf instance at {overleaf.Host} isn't ready");

			return await project.GetDocuments();
		});

		var snippets = GetCodeSegments(docs).ToList();

		foreach (var (_, code) in snippets.Where(s => s.source == Config.LatexOptions.MACROS_SOURCE_NAME))
			latex.LearnMacrosFrom(code);

		foreach(var s in snippets
			.Where(s => s.source != Config.LatexOptions.MACROS_SOURCE_NAME)
			.SelectMany(s => latex.ExtractSpells(game, s.code, s.source)))
			yield return s;

	}
}
