using System.Collections.Generic;
using System.Collections;
using System.Text;
using Newtonsoft.Json;
using HtmlAgilityPack;

internal static class Util
{
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

	public static void StoreJson<T>(this T obj, string filename)
	{
		using(var f = File.CreateText(filename))
			new JsonSerializer().Serialize(f, obj);
	}

	public static async Task<HtmlDocument> GetDocumentAsync(this HttpClient client, string url)
	{
		var doc = new HtmlDocument();

		using(var r = await client.GetAsync(url))
		{
			r.EnsureSuccessStatusCode();

			using(var s = await r.Content.ReadAsStreamAsync())
				doc.Load(s);
		}

		return doc;
	}

	public static void AssertEqual<T>(T a, T b, string message)
	{
		if(! EqualityComparer<T>.Default.Equals(a, b))
			throw new Exception($"{message}: Expected {a}, got {b}");
	}

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

	private static void clean(HtmlNode node)
	{
		for (int i = node.ChildNodes.Count; i-- > 0;)
		{
			var c = node.ChildNodes[i];

			if(c.Name == "#text" && string.IsNullOrWhiteSpace(c.InnerText))
				node.RemoveChild(c);
			else
				clean(c);
		}
	}

	public static void Clean(this HtmlNode node)
	{
		node.InnerHtml = string.Join(' ',
			node.InnerHtml.Split(null as char[], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

		clean(node);
	}

	public static IEnumerable<T> Squeeze<T>(this IEnumerable<T> ls)
		=> ls.Squeeze(EqualityComparer<T>.Default);

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
	/// <param name="text"></param>
	/// <param name="s"></param>
	/// <returns></returns>
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

	public static IEnumerable<(T value, int index)> Indexed<T>(this IEnumerable<T> values)
		=> values.Select((x,i) => (x,i));

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
			=> this.Current;

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

	public static IEnumerator<T> FollowedBy<T>(this IEnumerable<T> ls, IEnumerator<T> after)
		=> new ConcatEnumerator<T>(ls.GetEnumerator(), after);

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
}