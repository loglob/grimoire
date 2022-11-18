/** Acts like fetch(), but uses a cache, if possible
 * @param req A fetch request
 * @returns A matching response
 */
async function cachedFetch(req: RequestInfo | URL) : Promise<Response>
{
	// caches is only available in secure contexts
	if(typeof caches === "undefined")
		return await fetch(req);

	const cache = await caches.open("spell-db");
	const val = await cache.match(req);

	if(val == undefined)
	{
		const resp = await fetch(req);
		await cache.put(req, resp)

		return resp;
	}
	else
		return val;
}

type Spell =
{
	name : string, source : string,
	school : string, level : number,
	castingTime : string, ritual : boolean,
	range : string,
	components : string, materials : string|null,
	concentration : boolean, duration : string,
	description : string, upcast : string|null,
	classes : string[],
	statBlock : string|null
}

/**
 * @returns The index showing all known sources
 */
async function getSources() : Promise<{ [id: string] : string }>
{
	const r = await cachedFetch("/db/index.json");
	return await r.json();
}

/**
 * @param source A source id returned by getSources()
 * @returns The spells of a source
 */
async function getSpells(source : string) : Promise<Spell[]>
{
	const r = await cachedFetch(`/db/${source}.json`);
	return await r.json();
}

type SpellList =
{
	/** The sources this list is built from */
	sources : string[],
	/** List of spell names that are prepared. May be outside of the include set. */
	prepared : string[],
	/** include any spell matched by these filters */
	include : { class : string|null, school : string|null, name : string|null }[]
}

