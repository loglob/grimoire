using Grimoire.Latex;
using Grimoire.Util;

namespace Grimoire;

public record class Pf2e(Config.Game Conf) : IGame<Pf2e.Spell>
{
	public Log Log { get; } = Log.DEFAULT.AddTags(Conf.Shorthand);

	public enum Tradition
	{
		Arcane,
		Divine,
		Occult,
		Primal,
		Focus,
		Elemental
	}

	public readonly record struct Spell(
		string name, string source,
		string summary,
		Tradition[] traditions, int level,
		string castingTime, int seconds,
		string? reaction,
		string components,
		string range, int feet,
		string? targets, string? area,
		string? duration, string? save,
		string[] tags,
		string description,
		int page
	) : ISpell
	{
		string ISpell.Source => source;
	}


	Spell IGame<Spell>.ExtractLatexSpell(Compiler comp, Config.Book source, Chain<Token> code)
		=> throw new NotImplementedException("Latex not supported on Pf2e");

	ISource<Spell> IGame<Spell>.Instantiate(Config.Source src)
		=> src switch {
			Config.CopySource c => new Copy<Spell>(this, c),
			Config.NethysSource n => new AoN(Conf.Books.Values.ToArray(), n),
			_ => throw new ArgumentException($"Illegal Source type for pf2e: {src}")
		};

}