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
}