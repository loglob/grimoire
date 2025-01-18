using Grimoire.Markdown;
using Grimoire.Util;
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Grimoire.Pf2e;

namespace Grimoire;
#pragma warning disable SYSLIB1045

public record AoN(Config.Book[] books, Config.NethysSource Cfg) : ISource<Pf2e.Spell>
{
	private static readonly Log log = Log.DEFAULT.AddTags("AoN");
	private readonly Cache cache = new(Cfg.CacheLifetime, log, "AoN");

	/// <summary>
    ///  The record returned by the elasticsearch API
    /// </summary>
    /// <param name="actions"> A plain string describing the casting time </param>
    /// <param name="actions_number"> Number of seconds for cast </param>
    /// <param name="bloodline"> The Sorcerer subclasses with access to the spell </param>
    /// <param name="component"> The components required </param>
    /// <param name="patron_theme"> The Witch subclasses with access to the spell </param>
    /// <param name="primary_source_raw"> Book name with page number </param>
    /// <param name="range_raw"> The raw range text </param>
    /// <param name=""></param>
	private record SpellData(
		string actions, int actions_number,
		string[]? bloodline,
		string[]? component,
		int level,
		string name,
		string[] patron_theme,
		string primary_source,
		string primary_source_raw,
		string? range_raw,
		int range,
		string? area_raw,
		string? saving_throw,
		string markdown,
		string[]? tradition,
		string[] trait,
		string summary,
		string? target,
		string? duration_raw,
		string[]? heighten,
		string? trigger
	);

	private record ObjectCtx(params string[] keys)
	{
		public static JsonObject operator *(ObjectCtx c, JsonNode? n)
		{
			var cur = n!;

			for (int i = c.keys.Length - 1; i >= 0 ; i--)
				cur = new JsonObject() { { c.keys[i], cur } };

			return (JsonObject)cur;
		}

		public static ObjectCtx operator *(ObjectCtx x, ObjectCtx y)
			=> new(x.keys.Concat(y.keys).ToArray());
	}

	private static ObjectCtx singleton(params string[] keys)
		=> new(keys);

	private JsonObject query(string book)
		=> new JsonObject() {
			{ "query", singleton("bool") * new JsonObject() {
				{ "filter", new JsonArray() {
					singleton("term", "category") * JsonValue.Create("spell"),
					singleton("match_phrase", "primary_source") * JsonValue.Create(book),
				}},
				// select legacy or remaster content
				{ "must_not", new JsonArray() {
					singleton("exists", "field") * JsonValue.Create(Cfg.Legacy ? "legacy_id" : "remaster_id")
				} }
			}},
			{ "size", 1000 }
		};

	private void set(ref bool flag, string flagName)
	{
		if(flag)
			throw new FormatException($"{flagName} given twice");
	}

	// matches (unnested) parenthesis
	private readonly static Regex parenRegex = new(@"\([^(]*\)");
	private readonly static Regex pageRegex = new(@"pg.\s*([0-9]+)\s*$");

	private Spell toSpell(Config.Book b, SpellData r)
	{
		var comp = r.component is null ? "" : string.Join(", ", r.component);

		var lists = r.tradition?.Select(x =>
				Enum.TryParse<Tradition>(x.Trim(), out var t) ? t : throw new FormatException($"Invalid tradition: '{x}'")
			)?.ToArray() ?? [];
		int page;
		{
			var m = pageRegex.Match(r.primary_source_raw);

			if(! m.Success)
				throw new FormatException($"Invalid primary source: '{r.primary_source_raw}'");

			page = int.Parse(m.Groups[1].ValueSpan);
		}

		string desc;
		{
			// AoN markdown is shoddy, sometimes just plain invalid, and actually just HTML
			var tx = r.markdown.Split("---", 2)[1];
			desc = ToHtml.Convert(Markdown.Parser.ParseLines(tx));
		}

		return new Spell(
			r.name, b.Shorthand,
			r.summary,
			lists, r.level,
			r.actions, r.actions_number,
			r.trigger, comp,
			r.range_raw ?? "", r.range,
			r.target,
			r.area_raw, r.duration_raw, r.saving_throw,
			r.trait, desc,
			page
		);
	}

	public async IAsyncEnumerable<Spell> Spells()
	{
		var client = new ScraperClient(new("https://elasticsearch.aonprd.com"));

		foreach(var b in books)
		{
			var log = AoN.log.AddTags(b.Shorthand);
			using var data = await cache.CacheFunc(
				b.Shorthand,
				() => client.PostJsonAsync("/aon/_search?stats=search", query(b.FullName))
			);

			var total = data.RootElement.GetProperty("hits").GetProperty("total").GetProperty("value").GetInt32();
			var xs = data.RootElement
				.GetProperty("hits")
				.GetProperty("hits")
				.EnumerateArray()
				.Select(x => x.GetProperty("_source").Deserialize<SpellData>())
				.ToArray();

			if(total > xs.Length)
				log.Warn($"Processing only {xs.Length} of {total} hits. This is either a bug in grimoire or a rate limit by AoN.");

			foreach(var r in xs)
			{
				if(r is null)
				{
					log.Warn($"Records contain null entry");
					continue;
				}

				// The Elasticsearch query is a bit too lenient (i.e. "Player Core" also matches "Player Core 2")
				if(!r.primary_source.Equals(b.FullName, StringComparison.CurrentCultureIgnoreCase))
					continue;

				Spell s;

				try
				{
					s = toSpell(b, r);
				}
				catch(Exception e)
				{
					log.Warn($"In {r.name}: {e.Message}");

					if(e is NullReferenceException or ArgumentException)
						await Console.Error.WriteLineAsync(e.StackTrace);

					continue;
				}

				yield return s;
			}
		}

		yield break;
	}
}