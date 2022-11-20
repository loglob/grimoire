type Spell =
{
	name : string, source : string,
	school : string, level : number,
	castingTime : string, reaction : string, ritual : boolean,
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
	const r = await fetch("db/index.json");
	return await r.json();
}

/**
 * @param source A source id returned by getSources()
 * @returns The spells of a source
 */
async function getSpells(source : string) : Promise<Spell[]>
{
	const r = await fetch(`db/${source}.json`);
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

