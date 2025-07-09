using Grimoire.Latex;
using Grimoire.Util;

namespace Grimoire;

public interface IGame<out TSpell>
{
	Config.Game Conf { get; }

	public Log Log { get; }

	/// <summary>
	///  Extract a spell from LATEX code segment
	/// </summary>
	TSpell ExtractLatexSpell(Compiler comp, Config.Book source, Chain<Token> code);

	ISource<TSpell> Instantiate(Config.Source src);

	void LearnMaterials(Chain<Token> code)
		=> throw new InvalidOperationException($"Game {Conf.Shorthand} doesn't support material parsing");
}

public interface ISpell
{
	string Source { get; }
}

public interface ISource<out TSpell>
{
	public IAsyncEnumerable<TSpell> Spells();
}