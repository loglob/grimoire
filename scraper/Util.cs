using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;

internal static class Util
{
	public static async Task<T?> LoadJsonAsync<T>(string filename)
	{
		using(var f = File.OpenRead(filename))
		{
			return await JsonSerializer.DeserializeAsync<T>(f);
		}
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
}