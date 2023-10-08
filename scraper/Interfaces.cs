public interface IGame<out TSpell>
{
	Config.Game Conf { get; }

	/// <summary>
	///  Extract a spell from LATEX code
	/// </summary>
	TSpell ExtractLatexSpell(Latex comp, string source, IEnumerable<Latex.Token> body, string? upcast);
	
	ISource<TSpell> Instantiate(Config.Source src);
}

public interface ISpell
{
	string Source { get; }
}

public interface ISource<out TSpell>
{
	public IAsyncEnumerable<TSpell> Spells();
}