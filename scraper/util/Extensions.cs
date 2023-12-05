using HtmlAgilityPack;
using Newtonsoft.Json;
using System.Text;

namespace Util;

internal static class Extensions
{
	/// <summary>
	/// Loads an JSON formatted object from a file
	/// </summary>
	public static T LoadJson<T>(string filename)
	{
		using(var f = File.OpenText(filename))
		using(var j = new JsonTextReader(f))
		{
			var res = new JsonSerializer().Deserialize<T>(j);

			if(res is null)
				throw new FormatException($"Failed parsing JSON of {typeof(T).Name}");
			else
				return res;
		}
	}

	/// <summary>
	/// Stores an object as JSON in a file
	/// </summary>
	public static void StoreJson<T>(this T obj, string filename)
	{
		using(var f = File.CreateText(filename))
			new JsonSerializer().Serialize(f, obj);
	}

	/// <summary>
	/// Asserts that two values are equal. Throws if that is now the case.
	/// </summary>
	/// <param name="a"> The expected value </param>
	/// <param name="b"> The actual value </param>
	/// <param name="message"> An error message to print </param>
	public static void AssertEqual<T>(T a, T b, string message)
	{
		if(! EqualityComparer<T>.Default.Equals(a, b))
			throw new Exception($"{message}: Expected {a}, got {b}");
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

		if(cur.Any())
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
	/// Either read from cache or run an async computation.
	/// Writes a cache entry if the directory below cache exists.
	/// </summary>
	/// <param name="cache"> The file to load from. Read/Stored as JSON. </param>
	/// <param name="task"> How to compute a result if the cache doesn't exist or is invalid. </param>
	public static async Task<T> Cached<T>(string cache, Func<Task<T>> task)
	{
		if(File.Exists(cache))
		{
			try
			{
				return LoadJson<T>(cache);
			} catch(Exception)
			{}

			Console.Error.WriteLine("[WARN] Invalid cache");
		}

		var ret = await task();

		if(Directory.Exists(Path.GetDirectoryName(cache)))
			ret.StoreJson(cache);

		return ret;
	}

	/// <summary>
	/// Run multiple computations that are cached part-by-part.
	/// </summary>
	/// <param name="cache"> The file to load from. Read/Stored as JSON dictionary. </param>
	/// <param name="keys"> The list of input keys. Processed in-order. </param>
	/// <param name="task"> How to compute a result if the cache doesn't exist or is invalid. </param>
	/// <param name="progress">
	/// 	A callback that is issued after every processed element.
	/// 	Returns a name to print as progress report.
	/// </param>
	public static async IAsyncEnumerable<Tval> PartiallyCached<Tkey, Tval>(
		string cache, IEnumerable<Tkey> keys, Func<Tkey, Task<Tval>> task, Func<Tkey, string>? progress = null)
		where Tkey : notnull
	{
		Dictionary<Tkey, Tval> dict = new Dictionary<Tkey, Tval>();

		if(File.Exists(cache))
		{
			try
			{
				dict = LoadJson<Dictionary<Tkey, Tval>>(cache);
			} catch(Exception)
			{
				Console.Error.WriteLine("[WARN] Invalid cache");
			}
		}

		int count = keys.Count();
		int i = 0;
		int nLen = 0;

		foreach(var k in keys)
		{
			if(progress != null)
			{
				string name = progress(k);
				Console.Write($"{++i}/{count}: {name}");
				if(name.Length < nLen)
					Console.Write(new string(' ', nLen - name.Length));
				else
					nLen = name.Length;
				Console.CursorLeft = 0;
			}

			if(dict.TryGetValue(k, out var v))
				yield return v;
			else
			{
				try
				{
					dict[k] = await task(k);
				} catch(Exception ex)
				{
					if(progress != null)
						Console.WriteLine();
					Console.Error.WriteLine(ex.Message);
				}

				if(dict.TryGetValue(k, out var y))
					yield return y;
			}
		}

		if(Directory.Exists(Path.GetDirectoryName(cache)))
			dict.StoreJson(cache);
	}

	/// <summary>
	/// Looks up a human-readable book name.
	/// </summary>
	public static Config.Book FindSource(this Config.Book[] books, string source)
	{
		var b = books.Where(b => b.Matches(source)).ToArray();

		if(b.Length == 0)
			throw new Exception($"Unknown source: '{source}'");
		else if(b.Length > 1)
			throw new Exception($"Unknown source: '{source}': Ambigous between {b.Show()}");
		else
			return b[0];
	}

	/// <summary>
	/// Maps with a decorating function
	/// </summary>
	public static IEnumerable<(A,B)> SelectWith<A,B>(this IEnumerable<A> ls, Func<A,B> f)
		=> ls.Select(x => (x, f(x)));

	public static string Show<A,B>(this Dictionary<A,B[]> dict) where A : notnull
		=> dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Show()).Show();

	public static string Show<A,B>(this Dictionary<A,B> dict) where A : notnull
	{
		var sb = new StringBuilder("{");
		bool first = true;

		foreach (var item in dict)
		{
			if(!first)
				sb.Append(',');

			sb.Append($" {item.Key}: {item.Value}");
			first = false;
		}

		sb.Append(" }");

		return sb.ToString();
	}


	public static string Show<T>(this T[] arr)
	{
		var sb = new StringBuilder("[");
		bool first = true;

		foreach (var item in arr)
		{
			sb.Append(first ? " " : ", ");
			sb.Append(item);
			first = false;
		}

		sb.Append(" ]");

		return sb.ToString();
	}

	public static string Show(this string[] arr)
	{
		var sb = new StringBuilder("[");
		bool first = true;

		foreach (var item in arr)
		{
			sb.Append(first ? " " : ", ");
			sb.Append('\'');
			sb.Append(item);
			sb.Append('\'');
			first = false;
		}

		sb.Append(" ]");

		return sb.ToString();
	}

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
		=> str.Any(char.IsWhiteSpace) ? $"'{str}'" : str;

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
}