namespace Latex;

using CodeSegment = Util.Chain<Token>;

using System.Collections;
using System.Diagnostics;
using Util;

public static class Extensions
{
    private record ListWrapper<T>(Chain<T> Chain) : IReadOnlyList<T>
    {
        T IReadOnlyList<T>.this[int index] => Chain[index];

        int IReadOnlyCollection<T>.Count => Chain.Length;

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
			=> Chain.Items().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
			=> Chain.Items().GetEnumerator();
    }

	public static IReadOnlyList<T> AsList<T>(this Chain<T> chain)
		=> new ListWrapper<T>(chain);

    /// <summary>
    ///  Counts the amount of tokens until a token on the same level matches the given predicate.
    ///  Stops prematurely when a free-standing closing brace is reached, i.e. the positioned scope is closed.
    /// </summary>
    /// <returns>
    ///  That number, or -1 if the enumeration ended before the predicate was satisfied.
    ///  Doesn't count the accepting token itself.
    /// </returns>
    public static int FindOnSameLevel(this IEnumerable<Token> tks, Func<Token, bool> accept)
	{
		int level = 0;
		int count = 0;

		foreach (var t in tks)
		{
			if(level == 0 && accept(t))
				return count;

			if(t is OpenBrace)
				++level;
			else if(t is CloseBrace && --level < 0)
				break;

			++count;
		}

		return -1;
	}

	/// <summary>
	///  Locates a single mandatory macro argument.
	/// </summary>
	/// <param name="chain"> Token list to read </param>
	/// <param name="offset"> Offset at which to start searching </param>
	/// <returns>
	///  start: index where the argument _value_ starts (excluding parens)
	///  		-1 if no argument is found
	///  len: length of the argument value (excluding closing parens)
	///  end: index at which the argument has ended, i.e. after closing parens
	/// </returns>
	public static (int start, int len, int end) LocateArg(this IReadOnlyList<Token> chain, int offset = 0)
	{
		while(offset < chain.Count && chain[offset] is WhiteSpace)
			++offset;

		if(offset >= chain.Count)
			return ( -1, 0, offset );
		else if(chain[offset] is OpenBrace o)
		{
			int l = chain.Skip(offset + 1).FindOnSameLevel(t => t is CloseBrace);

			if(l < 0) // this should've been made impossible during lexing
				throw new UnreachableException($"An impossible slice happened, orphaning the open brace at {o.Pos} (chain offset {offset})");

			return ( offset + 1, l, offset + 2 + l );
		}
		else
			return ( offset, 1, offset + 1 );
	}

	/// <summary>
	///  Variant of LocateArg() that locates an optional argument
	/// </summary>
	public static (int start, int len, int end) LocateOptArg(this IReadOnlyList<Token> chain, int offset = 0)
	{
		while(offset < chain.Count && chain[offset] is WhiteSpace)
			++offset;

		if(offset < chain.Count && chain[offset] is Character c && c.Char == '[')
		{
			int l = chain.Skip(offset + 1).FindOnSameLevel(t => t is Character c && c.Char == ']');

			if(l < 0)
				Console.Error.WriteLine($"[WARN] Optional argument started at {c.Pos} never terminated");
			else
				return (offset + 1, l, offset + 2 + l);
		}

		return (-1, 0, offset);
	}

	/// <summary>
	///  Locates arguments to a macro
	/// </summary>
	/// <param name="chain"> The chain to process </param>
	/// <param name="offset"> Where to start in the chain, i.e. the index just past the invoking \macro </param>
	/// <param name="optionalArity"> Amount of optional arguments, less than arity. </param>
	/// <param name="arity"> Amount of total arguments to parse </param>
	/// <returns>
	///  args: The slices for those argument values. Or (-1,0) if an argument is missing.
	///  		Parenthesized arguments do not include the parenthesis
	///  end: The position immediately past the final argument. May be chain.Count if there are no tokens past the end.
	/// </returns>
	public static ((int index, int len)[] args, int end) LocateArgs(this IReadOnlyList<Token> chain, int offset, int optionalArity, int arity)
	{
		if(arity < optionalArity)
			throw new ArgumentOutOfRangeException(nameof(optionalArity));

		List<(int, int)> ret = new();

		for(;arity > 0 && offset < chain.Count; --arity)
		{
			int s, l;

			if(optionalArity > 0)
			{
				(s,l, offset) = LocateOptArg(chain, offset);

				--optionalArity;
			}
			else
				(s,l, offset) = LocateArg(chain, offset);

			ret.Add((s,l));
		}

		if(arity > 0)
			ret.AddRange(Enumerable.Repeat((-1, 0), arity));

		return (ret.ToArray(), offset);
	}

	public static ((int index, int len)[] args, int end) LocateArgs(this CodeSegment chain, int offset, int optionalArity, int arity)
		=> chain.AsList().LocateArgs(offset, optionalArity, arity);

	public static (CodeSegment?[] args, CodeSegment rest) Args(this CodeSegment chain, int optionalArity, int arity)
	{
		var (a, e) = chain.AsList().LocateArgs(0, optionalArity, arity);

		return (a.Select(il => il.index < 0 ? (CodeSegment?)null : chain.Slice(il.index, il.len)).ToArray(), chain.Slice(e));
	}

	/// <summary>
	///  Finds the contents of the document environment
	/// </summary>
	public static ArraySegment<Token>? DocumentContents(this ArraySegment<Token> file)
	{
		var begin = file.FirstIndexOf(x => x is BeginEnv b && b.Env == "document");

		if(begin < 0)
			return null;

		file = file.Slice(begin + 1);

		return file.Slice(0, file.FirstIndexOf(x => x is EndEnv e && e.Env == "document"));
	}

	private static Func<Token, bool> checkLevel(Func<Token, bool> cond)
	{
		int level = 0;

		return tk => {
			if(level == 0 && cond(tk))
				return true;
			if(level >= 0)
			{
				if(tk is OpenBrace or BeginEnv)
					++level;
				else if(tk is CloseBrace or EndEnv)
					--level;
			}

			return false;
		};
	}

	public static (CodeSegment left, Token sep, CodeSegment right)? SplitOn(this CodeSegment code, Func<Token, bool> sep, bool sameLevel)
		=> code.SplitOn(sameLevel ? checkLevel(sep) : sep);

	public static IEnumerable<CodeSegment> SplitBy(this CodeSegment code, Func<Token, bool> sep, bool sameLevel)
		=> code.SplitBy(sameLevel ? checkLevel(sep) : sep);

}