using Grimoire.Latex;
using Grimoire.Util;
using System.Diagnostics.CodeAnalysis;

namespace Grimoire;

public interface IGame<out TSpell>
{
	Config.Game Conf { get; }

	public Log Log { get; }

	/// <summary>
	///  Extract a spell from a LaTeX code segment
	/// </summary>
	TSpell ExtractLatexSpell(Compiler comp, Config.Book source, Chain<Token> code);

	ISource<TSpell> Instantiate(Config.Source src);

	/// <summary>
	/// Initializes a manifest with the populated with the game's units
	/// </summary>
	MaterialManifest InitUnits()
		=> new();

	/// <summary>
	///  Extract materials from a LaTeX code segment
	/// </summary>
	void LearnMaterials(MaterialManifest mf, Compiler comp, Chain<Token> code)
		=> throw new InvalidOperationException($"Game {Conf.Shorthand} doesn't support material parsing");
}

public interface ISpell
{
	string Source { get; }
}

public interface ISource<out TSpell>
{
	/// <summary>
	///  Finds the spells defined by this source
    ///  Should be pure.
	/// </summary>
	public IAsyncEnumerable<TSpell> Spells();

	/// <summary>
	///  Determines whether this source provides any materials
	/// </summary>
	/// <param name="manifest"> The manifest to write back to </param>
	public Task<bool> HasMaterials(MaterialManifest manifest)
		=> Task.FromResult(false);
}
