using Grimoire.Util;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

using Code = Grimoire.Util.Chain<Grimoire.Latex.Token>;

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
	///  Iterates over all tokens on the base level (i.e. not inside braces).
	///  Stops prematurely when a free-standing closing brace is reached, i.e. the base level is closed.
    ///  Yields open braces on base level, but not the corresponding closing brace.
    ///  If a closing brace is yielded, that brace closes the base level (and iteration stops immediately afterwards).
	/// </summary>
	private static IEnumerable<(int offset, Token token)> baseLevel(this IEnumerable<Token> tks)
	{
		int level = 0;
		int offset = 0;

		foreach (var t in tks)
		{
			if(level == 0)
				yield return (offset, t);

			if(t is OpenBrace)
				++level;
			else if(t is CloseBrace && --level < 0)
				break;

			++offset;
		}
	}

	/// <summary>
	///  Counts the amount of tokens until a token on the same level matches the given predicate.
	///  Stops prematurely when a free-standing closing brace is reached, i.e. the positioned scope is closed.
	/// </summary>
	/// <returns>
	///  That number, or -1 if the enumeration ended before the predicate was satisfied.
	///  Doesn't count the accepting token itself.
	/// </returns>
	public static int FindOnSameLevel(this IEnumerable<Token> tks, Func<Token, bool> accept)
		=> tks.baseLevel().FirstOrDefault((x) => accept(x.token), (offset: -1, null!)).offset;

	public static (Code left, Code right)? SplitOn(this Code tokens, Pattern pattern, bool sameLevel)
	{
		if(pattern.MaxWidth == 0)
			return null;

		// too lazy for DFA conversion
		var buf = new Toroid<Token>(pattern.MaxWidth);
		var expect = 0; // detects jumps from skipped groups
		var ix0 = 0;

		foreach(var (offset, token) in sameLevel ? tokens.Items().baseLevel() : tokens.Items().Select((tk,ix) => (offset: ix, token: tk)))
		{
			if(offset != expect)
				buf.Clear();

			expect = offset + 1;

			if(buf.Count == 0)
				ix0 = offset;
			else if(buf.Count == buf.Capacity)
				++ix0;

			buf.Push(token);

			if(pattern.MatchesWindow(buf, out var ixs))
				return (tokens.Slice(0, ix0 + ixs.offset), tokens.Slice(ix0 + ixs.offset + ixs.length));
		}

		while(buf.Pop(out _))
		{
			++ix0;

			if(pattern.MatchesWindow(buf, out var ixs))
				return (tokens.Slice(0, ix0 + ixs.offset), tokens.Slice(ix0 + ixs.offset + ixs.length));
		}

		return null;
	}

	public static int FindOnSameLevel(this IEnumerable<Token> tks, Token[] needle, Func<Token,Token,bool> same)
	{
		if(needle.Length == 0)
			return -1;

		// too lazy to implement KPM
		var buf = new Toroid<Token>(needle.Length);
		var expect = 0; // detects jumps from skipped groups

		return tks.baseLevel().FirstOrDefault(x => {
			if(x.offset != expect)
				buf.Clear();
			expect = x.offset + 1;
			buf.Push(x.token);

			return buf.Count == needle.Length && buf.Zip(needle).All(y => same(y.First, y.Second));
		}, (offset: needle.Length - 2, null!)).offset - needle.Length + 1;
	}

	public static int FindOnSameLevel(this IEnumerable<Token> tks, Token[] needle)
		=> FindOnSameLevel(tks, needle, (x, y) => x.IsSame(y));

	/// <summary>
	///  Finds the contents of the document environment
	/// </summary>
	public static Code? DocumentContents(this Code file)
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

	public static (Code left, Token sep, Code right)? SplitOn(this Code code, Func<Token, bool> sep, bool sameLevel)
		=> code.SplitOn(sameLevel ? checkLevel(sep) : sep);

	public static IEnumerable<Code> SplitBy(this Code code, Func<Token, bool> sep, bool sameLevel)
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
	public static string PosRange(this Code seg)
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

	public static Code TrimStart(this Code tokens)
		=> tokens.DropWhile(t => t is WhiteSpace);

	public static Code TrimEnd(this Code tokens)
	{
		int n = 0;
		int l = tokens.Length;

		while(n < l && tokens[^(n + 1)] is WhiteSpace)
			++n;

		return tokens.Slice(0, l - n);
	}

	public static Code Trim(this Code code)
		=> code.TrimStart().TrimEnd();

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
	public static Code? popOptArg(ref this Code code)
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
	public static Code? popArg(ref this Code code, bool acceptUnbraced = true)
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
	///  Parses macro arguments in-place
	/// </summary>
	/// <param name="args"> The signature to parse </param>
	/// <param name="code"> The slice to read from. Updated with the remaining tokens. </param>
	/// <returns> The argument values, or null if a mandatory argument is missing. </returns>
	public static Code[]? parseArguments(ref this Code code, ArgType[] args)
	{
		var result = new Code[args.Length];

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

	/// <summary>
	///  Wrapper around IEnumerable that allows single-item lookahead
	/// </summary>
	public class LL1<T>(IEnumerator<T> inner)
	{
		private readonly IEnumerator<T> inner = inner;
		private bool hasCurrent = inner.MoveNext();

		public LL1(IEnumerable<T> inner) : this(inner.GetEnumerator())
		{}

		public bool Move([MaybeNullWhen(false)] out T cur)
		{
			if(! hasCurrent)
			{
				cur = default;
				return false;
			}

			cur = inner.Current;
			hasCurrent = inner.MoveNext();

			return true;
		}

		public bool Peek([MaybeNullWhen(false)] out T next)
		{
			next = hasCurrent ? inner.Current : default;
			return hasCurrent;
		}

		/// <returns> Whether any items where consumed </returns>
		public bool MoveWhile(Func<T, bool> pred)
		{
			var movedAny = false;

			while(hasCurrent)
			{
				if(pred(inner.Current))
					hasCurrent = inner.MoveNext();
				else
					break;

				movedAny = true;
			}

			return movedAny;
		}
	}

	public static bool like(this LL1<Token> xs, LL1<Token> ys)
	{
		// trim start
		xs.MoveWhile(tk => tk is WhiteSpace);
		ys.MoveWhile(tk => tk is WhiteSpace);

		while(true)
		{
			var xWs = xs.MoveWhile(tk => tk is WhiteSpace);
			var yWs = ys.MoveWhile(tk => tk is WhiteSpace);

			var xGot = xs.Move(out var xTk);
			var yGot = ys.Move(out var yTk);

			if(xGot != yGot)
				return false;
			if(!xGot)
				return true;
			if(xWs != yWs || !xTk!.IsSame(yTk!))
				return false;
		}
	}

	public static bool like(this IEnumerator<Token> xs, IEnumerator<Token> ys)
		=> new LL1<Token>(xs).like(new LL1<Token>(ys));

	public static bool like(this IEnumerable<Token> xs, IEnumerable<Token> ys)
		=> xs.GetEnumerator().like(ys.GetEnumerator());

	public static IEnumerable<Code> extractEnvironments(this Code code, Func<BeginEnv, bool> predicate)
	{
		for(;;)
		{
			if(! code.SplitOn(x => x is BeginEnv be && predicate(be)).Unpack(out var spL))
				yield break;

			code = spL.right;

			if(! code.SplitOn(x => x is EndEnv ee && ee.Env == ((BeginEnv)spL.at).Env).Unpack(out var spR))
				throw new InvalidDataException($"Illegal slice unbalancing environments");

			yield return spR.left;
			code = spR.right;
		}
	}

	/// <summary>
	///  Iterates over every invocation of the macro `macroName`.
	///  Yields the arguments to those invocations, or null if an invocation was incomplete
	/// </summary>
	/// <param name="code"> Code to search </param>
	/// <param name="macroName"> The macro to look for </param>
	/// <param name="args"> The arguments to each invocation </param>
	public static IEnumerable<Code[]> extractInvocations(this Code code, string macroName, ArgType[] args)
		=> code.Items()
			.FindIndices(tk => tk is MacroName m && m.Macro == macroName)
			.Select(ix =>
			{
				var from = code.Slice(ix + 1);
				return from.parseArguments(args) ?? throw new FormatException($"Incomplete invocation of {macroName}");
			});

	private static readonly ArgType[] SINGLE_ARG = [new MandatoryArg()];

	public static IEnumerable<Code> extractInvocations(this Code code, string macroName)
		=> code.extractInvocations(macroName, SINGLE_ARG).Select(i => i[0]);

	public static Code? extractSingleInvocation(this Code code, string macroName)
		=> extractSingleInvocation(code, macroName, SINGLE_ARG) switch {
			null => null,
			var x => x[0]
		};

	public static Code[]? extractSingleInvocation(this Code code, string macroName, ArgType[] signature)
	{
		var all = code.extractInvocations(macroName, signature).ToList();

		return all.Count switch {
			0 => null,
			1 => all[0],
			_ => throw new FormatException($"At {code[0].Pos}: Too many uses of \\{macroName}")
		};
	}
}