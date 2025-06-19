using Grimoire.Util;
using System.Collections;
using System.Diagnostics;
using System.Text;

using CodeSegment = Grimoire.Util.Chain<Grimoire.Latex.Token>;

namespace Grimoire.Latex;

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
	///  Finds the contents of the document environment
	/// </summary>
	public static CodeSegment? DocumentContents(this CodeSegment file)
	{
		var begin = file.Items().FirstIndexOf(x => x is BeginEnv b && b.Env == "document");

		if(begin < 0)
			return null;

		file = file.Slice(begin + 1);

		return file.Slice(0, file.Items().FirstIndexOf(x => x is EndEnv e && e.Env == "document"));
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

	public static ArraySegment<Token> TrimStart(this ArraySegment<Token> code)
	{
		int n;

		for(n = 0; n < code.Count && code[n] is WhiteSpace; ++n)
		{}

		return code.Slice(n);
	}

	public static ArraySegment<Token> Trim(this ArraySegment<Token> code)
		=> code.Trim(static tk => tk is WhiteSpace);


	public static ArraySegment<T> Trim<T>(this ArraySegment<T> seg, Predicate<T> pred)
	{
		int l = 0;

		while(l < seg.Count && pred(seg[l]))
			++l;

		int n = seg.Count - l;

		while(n > 0 && pred(seg[l + n - 1]))
			--n;

		return seg.Slice(l, n);
	}

	/// <summary>
	///  Sane `IEnumerable<T>.SingleOrDefault()`
	/// </summary>
	public static T? SingleOrNull<T>(this IEnumerable<T> xs) where T : class
	{
		T? result = null;

		foreach(var x in xs)
		{
			if(result is not null)
				return null;

			result = x;
		}

		return result;
	}

	/// <summary>
	///  Formats the coordinate range of a code segment
	/// </summary>
	public static string PosRange(this CodeSegment seg)
	{
		if(seg.IsEmpty)
			return "<empty>";

		var l = seg[0].Pos;
		var r = seg[^1].Pos;

		return l.ToString() + (l.File == r.File
			? $"..{r.Row}:{r.Col}" : $" .. {r}");
	}

	public static void TrimLeft(this StringBuilder sb)
	{
		int l;

		for (l = 0; l < sb.Length && char.IsWhiteSpace(sb[l]); ++l)
		{}

		if(l > 0)
			sb.Remove(0, l);
	}

	public static void TrimRight(this StringBuilder sb)
	{
		int r;

		for (r = sb.Length; r > 0 && char.IsWhiteSpace(sb[r - 1]); --r)
		{}

		if(r < sb.Length)
			sb.Remove(r, sb.Length - r);

	}

	public static void Trim(this StringBuilder sb)
	{
		sb.TrimLeft();
		sb.TrimRight();
	}

	public static bool IsParBreak(this Token tk)
		=> tk is Character c && c.Char == '\n';

	public static CodeSegment TrimStart(this CodeSegment tokens)
		=> tokens.DropWhile(t => t is WhiteSpace);

	/// <summary>
	///  Removes the first `n` tokens IN-PLACE
	/// </summary>
	public static Chain<T> pop<T>(ref this Chain<T> seg, int n = 1)
	{
		var head = seg.Slice(0, n);
		seg = seg.Slice(n);

		return head;
	}

	/// <summary>
	///  Pops a single optional argument IN-PLACE
	/// </summary>
	public static CodeSegment? popOptArg(ref this CodeSegment code)
	{
		code = code.TrimStart();

		if(code.IsNotEmpty && code[0] is Character opening && opening.Char == '[')
		{
			int closing = code.Items().FindOnSameLevel(t => t is Character c && c.Char == ']');

			if(closing < 0) // TODO: get a proper log instance here
				Log.DEFAULT.Warn($"Optional argument started at {opening.Pos} never terminated");
			else
			{
				var inside = code.Slice(1, closing - 1);
				code = code.Slice(closing + 1);

				return inside;
			}
		}

		return null;
	}

	/// <summary>
	///  Pops a single mandatory argument IN-PLACE
	/// </summary>
	/// <param ></param>
	public static CodeSegment? popArg(ref this CodeSegment code, bool acceptUnbraced = true)
	{
		code = code.TrimStart();

		if(code.IsEmpty || (code[0] is Character nl && nl.Char == '\n')) // paragraph breaks
			return null;

		if(code[0] is OpenBrace opening)
		{
			int width = code.Items().Skip(1).FindOnSameLevel(t => t is CloseBrace);

			if(width < 0) // this should've been made impossible during lexing
				throw new UnreachableException($"An impossible slice happened, orphaning the open brace at {opening.Pos}");

			var inside = code.Slice(1, width);
			code = code.Slice(2 + width);

			return inside;
		}
		else if(acceptUnbraced)
			return pop(ref code);
		else
			return null;
	}

	/// <summary>
	///  Parses macro arguments
	/// </summary>
	/// <param name="args"> The signature to parse </param>
	/// <param name="code"> The slice to read from. Updated with the remaining tokens. </param>
	/// <returns> The argument values, or null if a mandatory argument is missing. </returns>
	public static CodeSegment[]? parseArguments(ref this CodeSegment code, ArgType[] args)
	{
		var result = new CodeSegment[args.Length];

		for (int i = 0; i < args.Length; i++)
		{
			var r = args[i].parse(ref code);

			if(r.HasValue)
				result[i] = r.Value;
			else
				return null;
		}

		return result;
	}
}