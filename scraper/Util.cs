using HtmlAgilityPack;
using Newtonsoft.Json;
using System.Collections;
using System.Text;

internal static class Util
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
				return Util.LoadJson<T>(cache);
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
				dict = Util.LoadJson<Dictionary<Tkey, Tval>>(cache);
			} catch(Exception)
			{
				Console.Error.WriteLine("[WARN] Invalid cache");
			}
		}

		int count = keys.Count();
		int i = 0;
		int nlen = 0;

		foreach(var k in keys)
		{
			if(progress != null)
			{
				string name = progress(k);
				Console.Write($"{++i}/{count}: {name}");
				if(name.Length < nlen)
					Console.Write(new string(' ', nlen - name.Length));
				else
					nlen = name.Length;
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
	/// Extracts every span of lines that is started by s and ended by another occurrence of s or end of file.
	/// s may also appear within a line
	/// </summary>
	public static IEnumerable<string[]> Spans(this IEnumerable<string> lines, string s)
	{
		List<string>? cur = null;

		foreach (var l in lines)
		{
			int i = l.IndexOf(s);

			if(i >= 0)
			{
				if(cur == null)
					cur = new List<string>();
				else
				{
					yield return cur.ToArray();
					cur.Clear();
				}

				cur.Add(l.Substring(i + s.Length));
			}
			else if(cur != null)
				cur.Add(l);
		}

		if(cur != null)
			yield return cur.ToArray();
	}

	/// <summary>
	/// Associates each value with its index
	/// </summary>
	public static IEnumerable<(T value, int index)> Indexed<T>(this IEnumerable<T> values)
		=> values.Select((x,i) => (x,i));

	/// <summary>
	/// Encapsulates two enumerators one after another
	/// </summary>
	private class ConcatEnumerator<T> : IEnumerator<T>
	{
		private readonly IEnumerator<T> first, second;
		bool inSecond = false;

		public ConcatEnumerator(IEnumerator<T> f, IEnumerator<T> s)
		{
			this.first = f;
			this.second = s;
		}

		public T Current
			=> inSecond ? second.Current : first.Current;

		object IEnumerator.Current
		#pragma warning disable CS8603
			=> this.Current;
		#pragma warning restore CS8603

		public bool MoveNext()
		{
			if(!inSecond && first.MoveNext())
				return true;
			else
			{
				inSecond = true;
				return second.MoveNext();
			}
		}

		public void Reset()
		{
			inSecond = false;
			first.Reset();
			second.Reset();
		}

		void IDisposable.Dispose()
		{
			first.Dispose();
			second.Dispose();
		}
	}

	/// <summary>
	/// Concatenation with an enumerator
	/// </summary>
	public static IEnumerator<T> FollowedBy<T>(this IEnumerable<T> ls, IEnumerator<T> after)
		=> new ConcatEnumerator<T>(ls.GetEnumerator(), after);

	/// <summary>
	/// Looks for a deliminator in the enumerable.
	/// If that deliminator is found, all text after it is yielded.
	/// The deliminator may be in the middle of a line, and all text of that line after the deliminator is yielded.
	/// If that deliminator is never found, forward the input enumerable.
	/// </summary>
	public static IEnumerable<string> StartedWith(this IEnumerable<string> xs, string delim)
	{
		bool got = false;

		foreach (var x in xs)
		{
			if(got)
				yield return x;
			else
			{
				int p = x.IndexOf(delim);

				if(p < 0)
					continue;

				got = true;
				var s = x.Substring(p + delim.Length);

				if(!string.IsNullOrWhiteSpace(s))
					yield return s;
			}
		}

		if(!got) foreach (var x in xs)
			yield return x;
	}

	/// <summary>
	/// Complement of StartedWith().
	/// Forwards the input enumerable until the given deliminator is found in any part of a line.
	/// Also yields preceding parts of the ending line if they aren't empty or whitespace.
	/// </summary>
	public static IEnumerable<string> EndedBy(this IEnumerable<string> xs, string delim)
	{
		foreach (var x in xs)
		{
			int p = x.IndexOf(delim);

			if(p < 0)
				yield return x;
			else
			{
				var s = x.Substring(0, p);

				if(!string.IsNullOrWhiteSpace(s))
					yield return s;

				yield break;
			}
		}
	}

	/// <summary>
	/// Splits by deliminator over multiple lines, preserving line separators.
	/// Trims around deliminator.
	/// </summary>
	public static IEnumerable<string[]> Split(this IEnumerable<string> xs, string delim)
	{
		var cur = new List<string>();

		foreach (var x in xs)
		{
			var spl = x.Split(delim, 2, StringSplitOptions.TrimEntries);

			if(spl.Length < 2)
				cur.Add(x);
			else
			{
				if (spl[0].Length > 0)
					cur.Add(spl[0]);

				yield return cur.ToArray();
				cur.Clear();

				if (spl[1].Length > 0)
					cur.Add(spl[1]);
			}
		}

		yield return cur.ToArray();
	}

	/// <summary>
	/// Trims an array from the left and the right
	/// </summary>
	/// <param name="arr"> The array to segment </param>
	/// <param name="pred"> Whether to trim an element </param>
	public static ArraySegment<T> Trim<T>(this T[] arr, Func<T,bool> pred)
	{
		int l;

		for (l = 0; l < arr.Length && pred(arr[l]); l++);

		if(l >= arr.Length)
			return new ArraySegment<T>();

		// assert !pred(arr[l])
		int r;

		for (r = arr.Length - 1; r > l && pred(arr[r]); r--);

		return new ArraySegment<T>(arr, l, r - l + 1);
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
}