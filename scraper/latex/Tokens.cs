using Util;

namespace Latex;

public readonly record struct Position(string File, int Row, int Col)
{
	public override string ToString()
		=> $"{File.Quote()}:{Row}:{Col}";
}

public abstract record Token(Position Pos)
{
	public abstract override string ToString();

	public string At
		=> $"{this} at {Pos}";

	/// <returns> A string showing a human-readable compilation result for this token </returns>
	public abstract string Display();

	public abstract bool IsSame(Token other);

}

/// <summary>
/// A reference to a macro, of the form \<name>.
/// </summary>
/// <param name="Macro">The name of the referenced macro, without backslash</param>
public sealed record MacroName(string Macro, Position Pos) : Token(Pos)
{
	public override string ToString() => $"\\{Macro}";
	public override string Display() => "";

	public override bool IsSame(Token other)
		=> other is MacroName m && m.Macro == Macro;
}

/// <summary>
/// Any regular character that isn't whitespace.
/// </summary>
public sealed record Character(char Char, Position Pos) : Token(Pos)
{
	public override string ToString() => Char.ToString();

	public override string Display() => Char.ToString();

	public override bool IsSame(Token other)
		=> other is Character c && c.Char == Char;
}

/// <summary>
///  A closing brace.
///  Checked to be matched during lexing.
/// </summary>
public sealed record CloseBrace(Position Pos) : Token(Pos)
{
	public override string ToString() => "}";

	public override string Display() => "";

	public override bool IsSame(Token other)
		=> other is CloseBrace;
}

/// <summary>
///  An opening brace.
///  Checked to be matched during lexing.
/// </summary>
public sealed record OpenBrace(Position Pos) : Token(Pos)
{
	public override string ToString() => "{";
	public override string Display() => "";

	public override bool IsSame(Token other)
		=> other is OpenBrace;
}

/// <summary>
/// TeX whitespace, which is discarded when searching for function arguments
/// </summary>
public sealed record WhiteSpace(Position Pos) : Token(Pos)
{
	public override string ToString() => " ";
	public override string Display() => " ";

	public override bool IsSame(Token other)
		=> other is WhiteSpace;
}

/// <summary>
/// Reference to an argument, of the form #<number>
/// </summary>
public sealed record ArgumentRef(int Number, Position Pos) : Token(Pos)
{
	public override string ToString() => $"#{Number}";
	public override string Display() => "";

	public override bool IsSame(Token other)
		=> other is ArgumentRef a && a.Number == Number;
}

/// <summary>
/// A chunk that should not be escaped when translating to HTML
/// </summary>
public sealed record HtmlChunk(string Data, Position Pos) : Token(Pos)
{
	public override string ToString() => $"\\<{Data}\\>";
	public override string Display() => Data;

	public override bool IsSame(Token other)
		=> other is HtmlChunk h && h.Data == Data;
}

/// <summary>
///  Special token for "\\"
/// </summary>
public sealed record BackBack(Position Pos) : Token(Pos)
{
	public override string ToString() => "\\\\";
	public override string Display() => "\n";

	public override bool IsSame(Token other)
		=> other is BackBack;
}


public abstract record EnvToken(string Env, Position Pos) : Token(Pos)
{}

public sealed record BeginEnv(string Env, Position Pos) : EnvToken(Env, Pos)
{
	public override string ToString() => $"\\begin{{{Env}}}";
	public override string Display() => "";

	public override bool IsSame(Token other)
		=> other is BeginEnv b && b.Env == Env;
}

public sealed record EndEnv(string Env, Position Pos) : EnvToken(Env, Pos)
{
	public override string ToString() => $"\\end{{{Env}}}";
	public override string Display() => "";


	public override bool IsSame(Token other)
		=> other is EndEnv e && e.Env == Env;
}

public sealed record AlignTab(Position Pos) : Token(Pos)
{
	public override string ToString() => "&";
	public override string Display() => "";


	public override bool IsSame(Token other)
		=> other is AlignTab;
}
