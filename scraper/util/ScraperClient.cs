using HtmlAgilityPack;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Grimoire.Util;

public class ScraperClient
{
	private HttpClient client;
	/// <summary>
	/// The minimum time between HTTP requests
	/// </summary>
	public TimeSpan RateLimit { get; init; } = TimeSpan.FromMilliseconds(250);
	/// <summary>
	/// The time that the last HTTP request was sent
	/// </summary>
	private DateTime lastHit = DateTime.Now;

	/// <summary>
	/// The path to the directory to use as cache.
	/// Caching is disabled if this directory doesn't exist
	/// </summary>
	private const string CACHE = "cache/http";

	public ScraperClient(string baseURI)
	{
		this.client = new HttpClient(){ BaseAddress = new Uri(baseURI) };
	}

	public ScraperClient(string baseURI, TimeSpan? rateLimit) : this(baseURI)
	{
		if(rateLimit.HasValue)
			this.RateLimit = rateLimit.Value;
	}

	/// <summary>
	/// Requests a resource from the HTTP server. Enforces rate limit.
	/// </summary>
	private async Task<Stream> sendAsync(HttpRequestMessage req)
	{
		var diff = RateLimit - (DateTime.Now - lastHit);

		if(diff > TimeSpan.Zero)
			await Task.Delay(diff);

		lastHit = DateTime.Now;
		var resp = await client.SendAsync(req);

		resp.EnsureSuccessStatusCode();
		return await resp.Content.ReadAsStreamAsync();		
	}

	/// <summary>
	/// Requests a stream either from web or cache. Populates cache if the CACHE directory exists.
	/// </summary>
	public async Task<Stream> GetStreamAsync(string uri)
	{
		if(Directory.Exists(CACHE))
		{
			var cacheFile = Path.Combine(CACHE, Uri.EscapeDataString(client.BaseAddress + "+" + uri));

			if(!File.Exists(cacheFile))
			{
				using var f = File.Create(cacheFile);
				using var data = await sendAsync(new(HttpMethod.Get, uri));
				await data.CopyToAsync(f);
			}

			return File.OpenRead(cacheFile);
		}
		else
			return await sendAsync(new(HttpMethod.Get, uri));
	}

	public async Task<JsonDocument> GetJsonAsync(string uri)
	{
		using var s = await GetStreamAsync(uri);
		return await JsonDocument.ParseAsync(s);
	}

	/// <summary>
	/// Requests an HTML document from either cache or web
	/// </summary>
	public async Task<HtmlDocument> GetHtmlAsync(string uri)
	{
		var doc = new HtmlDocument();

		using(var s = await GetStreamAsync(uri))
			doc.Load(s);

		return doc;
	}

	/// <summary>
	/// POSTs a JSON api. Never caches.
	/// </summary>
	public async Task<JsonDocument> PostJsonAsync(string uri, JsonNode request)
	{
		var req = new HttpRequestMessage(HttpMethod.Post, uri) {
			Content = JsonContent.Create(request)
		};
		using var str = await sendAsync(req);
		
		return await JsonDocument.ParseAsync(str);
	}
}