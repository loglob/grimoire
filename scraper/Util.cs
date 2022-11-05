using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;

internal static class Util
{
	public static async Task<T> LoadJsonAsync<T>(string filename)
	{
		using(var f = File.OpenRead(filename))
		{
			var res = await JsonSerializer.DeserializeAsync<T>(f);

			if(res is null)
				throw new FormatException($"Failed parsing JSON of {typeof(T).Name}");
			else
				return res;
		}
	}

	public static async Task StoreJsonAsync<T>(this T obj, string filename)
	{
		using(var f = File.Create(filename))
			await JsonSerializer.SerializeAsync(f, obj);
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
				return await Util.LoadJsonAsync<T>(cache);
			} catch(Exception)
			{}

			Console.Error.WriteLine("[WARN] Invalid cache");
		}

		var ret = await task();

		if(Directory.Exists(Path.GetDirectoryName(cache)))
			await ret.StoreJsonAsync(cache);

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
				dict = await Util.LoadJsonAsync<Dictionary<Tkey, Tval>>(cache);
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
			await dict.StoreJsonAsync(cache);
	}
}