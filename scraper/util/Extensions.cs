using HtmlAgilityPack;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Grimoire.Util;

internal static class Extensions
{
	/// <summary>
	/// Asserts that two values are equal. Throws if that is now the case.
	/// </summary>
	/// <param name="a"> The expected value </param>
	/// <param name="b"> The actual value </param>
	/// <param name="message"> An error message to print </param>
	public static void AssertEqual<T>(T a, T b, string message)
	{
		if(! EqualityComparer<T>.Default.Equals(a, b))
			throw new Exception($"{message}: Expected {Show(a)}, got {Show(b)}");
	}

	/// <summary>
	/// Splits an enumerable by a predicate
	/// </summary>
	/// <param name="ls"> The source enumerable </param>
	/// <param name="pred"> If true, discard that value and insert a break </param>
	/// <param name="noEmpty"> If true, discard any empty values </param>
	public static IEnumerable<T[]> SplitBy<T>(this IEnumerable<T> ls, Func<T,bool> pred, bool noEmpty = false)
	{
		var cur = new List<T>();

		foreach (var x in ls)
		{
			if(pred(x) && (cur.Count > 0 || !noEmpty))
			{
				yield return cur.ToArray();
				cur.Clear();
			}
			else
				cur.Add(x);
		}

		if(cur.Count != 0)
			yield return cur.ToArray();
	}

	/// <summary>
	/// Cleans a HtmlNode.
	/// Deletes and flattens <a> nodes and erases empty #text nodes.
	/// Traverses entire node tree.
	/// </summary>
	private static void clean(HtmlNode node)
	{
		if(node.Name == "a")
			node.ParentNode.RemoveChild(node, true);

		for (int i = node.ChildNodes.Count; i-- > 0;)
		{
			var c = node.ChildNodes[i];

			if(c.Name == "#text" && string.IsNullOrWhiteSpace(c.InnerText))
				node.RemoveChild(c);
			else
				clean(c);
		}
	}

	/// <summary>
	/// Normalizes whitespace in HTML node.
	/// Also deletes and flattens <a> nodes.
	/// </summary>
	public static void Clean(this HtmlNode node)
	{
		node.InnerHtml = string.Join(' ',
			node.InnerHtml.Split(null as char[], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

		clean(node);
	}

	/// <summary>
	/// Discards any adjacent, equal elements.
	/// Yields and compares against the first element of a group.
	/// </summary>
	public static IEnumerable<T> Squeeze<T>(this IEnumerable<T> ls, IEqualityComparer<T> comp)
	{
		T? last = default;
		bool first = true;

		foreach (var x in ls)
		{
			if(first || !comp.Equals(last, x))
				yield return x;

			last = x;
			first = false;
		}
	}

	/// <summary>
	/// Discards any adjacent, equal elements.
	/// Yields and compares against the first element of a group.
	/// </summary>
	public static IEnumerable<T> Squeeze<T>(this IEnumerable<T> ls)
		=> ls.Squeeze(EqualityComparer<T>.Default);

	/// <summary>
	/// Looks up a human-readable book name.
	/// </summary>
	public static Config.Book FindSource(this Config.Book[] books, string source)
	{
		var b = books.Where(b => b.Matches(source)).ToArray();

		if(b.Length == 0)
			throw new Exception($"Unknown source: {Show(source)}");
		else if(b.Length > 1)
			throw new Exception($"Unknown source: {Show(source)}: Ambiguous between {b.Show()}");
		else
			return b[0];
	}

	/// <summary>
	/// Maps with a decorating function
	/// </summary>
	public static IEnumerable<(A,B)> SelectWith<A,B>(this IEnumerable<A> ls, Func<A,B> f)
		=> ls.Select(x => (x, f(x)));

	private static IEnumerable<object> unpack(IEnumerable xs)
	{
		foreach(var x in xs)
			yield return x;
	}

	public static string Show(string s)
	{
		var b = new StringBuilder();
		b.Append('"');

		foreach (var c in s)
		{
			if(c == '"')
				b.Append("\\\"");
			else if(c == '\n')
				b.Append("\\n");
			else if(c == '\\')
				b.Append("\\\\");
			else
				b.Append(c);
		}

		b.Append('"');
		return b.ToString();
	}

	public static string Show(this TimeSpan dt)
		=> (dt.TotalHours >= 24)
			? $"{(int)dt.TotalHours}h"
			: (dt.TotalHours >= 1)
				? $"{dt.Hours}h{dt.Minutes}m"
				: (dt.TotalMinutes >= 1)
					? $"{dt.Minutes}m{dt.Seconds}s"
					: $"{dt.TotalSeconds:0.000}s";

	public static string Show(this object? any)
		=> any switch {
			null => "null" ,
			char c => $"'{c}'" ,
			string s => Show(s) ,
			IDictionary d => "{ " + string.Join(", ", unpack(d.Keys).Select(x => Show(x) + " -> " + Show(d[x]))) + " }" ,
			IEnumerable l => "[ " + string.Join(", ", unpack(l).Select(Show)) + " ]",
			_ => any.ToString()!
		};

	public static (string left, string? right) MaybeSplitOn(string str, string sep)
	{
		var spl = str.Split(sep, 2, StringSplitOptions.TrimEntries);

		if(spl.Length > 1)
			return (spl[0], spl[1]);
		else
			return (spl[0], null);
	}

	public static int FirstIndexOf<T>(this IEnumerable<T> xs, Func<T, bool> cond)
	{
		int i = 0;

		foreach (var x in xs)
		{
			if(cond(x))
				return i;

			++i;
		}

		return -1;
	}

	private static int[] kpmHelperArray<T>(T[] needle, Func<T,T,bool> eq)
	{
		int[] ret = new int[needle.Length];
		int curLen = 0;

		ret[0] = 0;

		for(int i = 1; i < needle.Length;)
		{
			if(eq(needle[i], needle[curLen]))
			{
				ret[i] = ++curLen;
				++i;
			}
			else if(curLen > 0)
				curLen = ret[curLen - 1];
			else
			{
				ret[i] = 0;
				++i;
			}
		}

		return ret;
	}

	public static IEnumerable<int> FindIndices<T>(this IEnumerable<T> haystack, T[] needle, Func<T,T,bool> eq, bool overlapping = true)
	{
		int[] rewind = kpmHelperArray(needle, eq);
		int m = 0;
		int i = 0;


		foreach (var x in haystack)
		{
			rescan:

			if(eq(x, needle[m]))
			{
				if(m + 1 == needle.Length)
				{
					yield return i - m;
					m = overlapping ? rewind[m] : 0;
				}
				else
					++m;
			}
			else if(m != 0)
			{
				m = rewind[m - 1];
				goto rescan;
			}

			++i;
		}
	}

	public static IEnumerable<(T cur, T? next)> Pairs<T>(this IEnumerable<T> xs) where T : struct
	{
		bool gotLast = false;
		T last = default;

		foreach (var x in xs)
		{
			if(gotLast)
				yield return (last, x);

			gotLast = true;
			last = x;
		}

		if(gotLast)
			yield return (last, null);
	}

	public static B WithValue<A,B>(this A? a, Func<A,B> f, B fallback) where A : struct
		=> a.HasValue ? f(a.Value) : fallback;

	public static string Quote(this string str)
		=> str.Any(char.IsWhiteSpace) ? str.Show() : str;

	public static IEnumerable<int> FindIndices<T>(this IEnumerable<T> xs, Func<T, bool> cond)
	{
		int i = 0;

		foreach (var x in xs)
		{
			if(cond(x))
				yield return i;

			++i;
		}
	}

	public static void AddRange<T>(this ISet<T> set, IEnumerable<T> items)
	{
		foreach (var x in items)
			set.Add(x);
	}

	public static void UnionAll<K, V, Vs>(this IDictionary<K, HashSet<V>> dictA, IDictionary<K, Vs> dictB) where Vs : IEnumerable<V>
	{
		foreach (var kvp in dictB)
		{
			if(dictA.TryGetValue(kvp.Key, out var vs))
				vs.AddRange(kvp.Value);
			else
				dictA.Add(kvp.Key, kvp.Value.ToHashSet());
		}
	}

	public static T? Just<T>(this T x) where T : struct
		=> x;

	public static bool Bind<T>(this T? maybe, [MaybeNullWhen(false)] out T value) where T : struct
	{
		if(maybe.HasValue)
		{
			value = maybe.Value;
			return true;
		}
		else
		{
			value = default;
			return false;
		}
	}

}