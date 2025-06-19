
using Grimoire.Util;

namespace Grimoire.Latex;

abstract public record class ArgType
{
	internal ArgType()
	{}

	/// <summary>
	///  Parses this argument
	/// </summary>
	/// <param name="code"> The input text. Updated with the remaining input. </param>
	/// <returns>
	/// 	The argument's value, or null if a mandatory argument is missing
	/// </returns>
	abstract internal Chain<Token>? parse(ref Chain<Token> code);

	public static ArgType[] SimpleSignature(int arity, Chain<Token>? defaultArg = null)
	{
		if(defaultArg.HasValue && arity == 0)
			throw new ArgumentException("0 arity with defaulted argument");

		var args = new ArgType[arity];

		Array.Fill(args, new MandatoryArg());

		if(defaultArg.HasValue)
			args[0] = new OptionalArg(defaultArg.Value);

		return args;
	}
}
public record class StarArg() : ArgType()
{
	internal override Chain<Token>? parse(ref Chain<Token> code)
	{
		code = code.TrimStart();

		// we use '*' and '' instead of the true and false markers of xparse (since we don't support if/else anyways)
		return (code.IsNotEmpty && code[0] is Character c && c.Char == '*')
			? code.pop()
			: Chain<Token>.Empty;
	}
}
public record class MandatoryArg() : ArgType()
{
	internal override Chain<Token>? parse(ref Chain<Token> code)
		=> code.popArg();
}
public record class OptionalArg(Chain<Token> fallback) : ArgType()
{
	public OptionalArg() : this(Chain<Token>.Empty)
	{}

	internal override Chain<Token>? parse(ref Chain<Token> code)
		=> code.popOptArg().GetValueOrDefault(fallback);
}