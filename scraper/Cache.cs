using System.Text.Json;
using Grimoire.Util;

namespace Grimoire;

/// <summary>
///  Controls a set of cache files
/// </summary>
/// <param name="Lifetime"></param>
/// <param name="Path"></param>
/// <returns></returns>
public record Cache(float Lifetime, Log Log, params string[] Path)
{
	public string Directory { get;init; } = "cache";
	public JsonSerializerOptions JsonOptions { get;init; } = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private string filePath(string name)
		=> Directory + "/" + string.Join('_', Path.Append(name));

	/// <summary>
	///  Attempts to load a JSON value from a file if it exists and the maximum lifetime isn't exceeded.
	///  Deletes outdated or invalid cache files.
	/// </summary>
	protected async Task<T?> tryReadJson<T>(string path)
	{
		if(!File.Exists(path))
			return default;

		var age = DateTime.Now.Subtract(File.GetCreationTime(path)).TotalSeconds;

		if(age > Lifetime)
		{
			Log.Info($"Refreshing {age}s old cache file {path.Show()}");
			File.Delete(path);
			return default;
		}

		using var c = File.OpenRead(path);
		Log.Info($"Reading {age}s old cache file {path.Show()}");

		try
		{
			return await JsonSerializer.DeserializeAsync<T>(c, JsonOptions)!;			
		}
		catch(Exception ex)
		{
			Log.Warn($"Invalid cache file \"{path}\": {ex.Message}");
			File.Delete(path);
			return default;
		}
	}

	public async ValueTask<T> CacheFunc<T>(string name, Func<Task<T>> compute)
	{
		var p = filePath(name);
		var x = await tryReadJson<T>(p);

		if(x is not null)
			return x;

		var y = await compute();

		if(System.IO.Directory.Exists(Directory))
		{
			await using var f = File.Create(p);
			await JsonSerializer.SerializeAsync(f, y, JsonOptions);
		}
		
		return y;
	}

	/// <summary>
	///  Caches a series of computations.
	///  Swallows exceptions
	/// </summary>
	/// <param name="name"> An identifier to find the cache entry by </param>
	/// <param name="keys"> The static key set </param>
	/// <param name="compute"> A computation to run on cache miss </param>
	/// <param name="progress"> A callback that is given the current index, the total count and the current key being processed. </param>
	/// <returns> A kvp for every given item in keys. </returns>
	public async IAsyncEnumerable<(K key, V val)> CacheMany<K,V>(string name, IEnumerable<K> keys, Func<K, Task<V>> compute, bool logProgress = false)
		where K : notnull
	{
		var p = filePath(name);
		var data = await tryReadJson<Dictionary<K, V>>(p);
		bool cacheExisted = data is not null;

		data ??= [];
		
		var excess = data.Keys.Except(keys);

		if(excess.Any())
		{
			Log.Warn($"Cache file {p.Show()} has unused keys: {excess.Show()}");
			
			foreach(var k in excess)
				data.Remove(k);
		}
		
		var left = new Queue<K>(keys);
		var total = left.Count;

		while(left.TryDequeue(out var key))
		{
			if(logProgress)
				Log.Pin($"{total - left.Count + 1}/{total}: {key}");

			if(! data.TryGetValue(key, out var v))
			{
				try
				{
					v = await compute(key);
					
				}
				catch (Exception ex)
				{
					Log.Warn($"While processing {key}: {ex.Message}");
					continue;
				}

				data[key] = v;
			}

			yield return (key, v);
		}
		
		if(total > 0 && System.IO.Directory.Exists(Directory))
		{
			// ensure the creation time corresponds to the oldest entry in the cache
			var ct = cacheExisted ? File.GetCreationTimeUtc(p) : default;
			
			await using(var f = File.Create(p))
				await JsonSerializer.SerializeAsync(f, data, JsonOptions);

			if(cacheExisted)
				File.SetCreationTimeUtc(p, ct);
		}
	}
}