using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;

internal static class Util
{
	/// <summary>
	/// Applies a mapping in-situ
	/// </summary>
	public static T[] Map<T>(this T[] arr, Func<T,T> f)
	{
		for (int i = 0; i < arr.Length; i++)
			arr[i] = f(arr[i]);

		return arr;
	}

	public static string ReadOneLine(this Stream s)
	{
		var buf = new byte[1024];
		int off = 0;

		while(true)
		{
			int read = s.Read(buf, off, buf.Length - off);
			int nl = Array.IndexOf<byte>(buf, (byte)'\n', off, read);

			if(off + read < buf.Length - 1 || nl >= 0)
			{
				Array.Resize(ref buf, (nl < 0) ? off + read : nl);
				break;
			}

			off += read;
			Array.Resize(ref buf, buf.Length * 2);
		}

		return new string(UTF8Encoding.UTF8.GetChars(buf));
	}

	public static void WriteString(this Stream s, string ln)
	{
		var enc = UTF8Encoding.UTF8;
		s.Write(enc.GetBytes(ln));
	}

	/// <summary>
	/// Equivalent to ls.Count() > len
	/// </summary>
	/// <param name="len"></param>
	/// <returns></returns>
	public static bool MoreThan<T>(this IEnumerable<T> ls, int len)
	{
		foreach (var _ in ls)
		{
			if(len <= 0)
				return true;

			len--;
		}

		return false;
	}

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