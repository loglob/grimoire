using Grimoire.Latex;
using Grimoire.Util;

namespace Grimoire;

public interface IGame<out TSpell>
{
	Config.Game Conf { get; }

	public Log Log { get; }

	/// <summary>
	/// The manifest shared by all of this game's sources
	/// </summary>
	public MaterialManifest Manifest { get; }

	/// <summary>
	///  Extract a spell from a LaTeX code segment
	/// </summary>
	TSpell ExtractLatexSpell(Compiler comp, Config.Book source, Chain<Token> code);

	ISource<TSpell> Instantiate(Config.Source src);

	/// <summary>
	///  Extract materials from a LaTeX code segment
	/// </summary>
	void ExtractMaterials(Compiler comp, Chain<Token> code)
		=> throw new InvalidOperationException($"Game {Conf.Shorthand} doesn't support material parsing");
}

public interface ISpell
{
	string Source { get; }
}

public interface ISource<out TSpell>
{
	public IGame<TSpell> Game { get; }

	/// <summary>
	///  Finds the spells defined by this source
	///  Should be pure.
	/// </summary>
	public IAsyncEnumerable<TSpell> Spells();

	/// <summary>
	///  Loads any materials this source defines.
	///  Must be called after `Initialize()`.
	///
	///  Writes back to its game's manifest.
	/// </summary>
	public Task LoadMaterials()
		=> Task.CompletedTask;
}
