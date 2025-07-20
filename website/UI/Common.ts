
namespace UI
{
	import IGame = Games.IGame
	import ISpell = Data.ISpell

	/** Cache for database index */
	var gameIndex : Data.GameIndex | null = null

	export const games = {
		dnd5e: "D&D 5e",
		gd: "Goedendag",
		pf2e: "Pathfinder 2e"
	};

	export async function getGameIndex() : Promise<Data.GameIndex>
	{
		if(gameIndex === null)
			gameIndex = await (await fetch(`db/index.json`)).json();
		if(gameIndex === null)
		{
			alert("There is a problem with the database. Contact Administrator if this problem persists.")
			window.location.reload()
			throw Error("Page reload didn't fire correctly")
		}

		return gameIndex;
	}

	export async function withGameNamed<A>(id : string, f : <T extends ISpell>(g : IGame<T>) => Promise<A>) : Promise<A>
	{
		const books = (await getGameIndex())[id];

		if(books) switch(id)
		{
			case "dnd5e":
				return await f(new Games.DnD5e.Game(id, games[id], books));

			case "gd":
				return await f(new Games.Goedendag.Game(id, games[id], books));

			case "pf2e":
				return await f(new Games.Pf2e.Game(id, games[id], books));
		}

		Util.backToIndex(`Invalid game ID: '${id}'. Either the URL is wrong or your browser cache is outdated.`, null)
	}

	/** Invokes a function that is generic over all games */
	export async function withGame<A>(f : <T extends ISpell>(g : IGame<T>) => Promise<A>) : Promise<A>
	{
		var id = new URLSearchParams(window.location.search).get("game");

		if(! id)
			id = Object.keys(await getGameIndex())[0]

		return withGameNamed(id, f);
	}

	export function loadSpellList(list : any) : Data.SpellList
	{
		function isStringArray(dims : number, thing : any) : boolean
		{
			if(!Array.isArray(thing))
				return false;

			return thing.every(x => (dims > 1) ? isStringArray(dims - 1, x) : typeof(x) === "string");
		}

		const q = list.query || list.filter

		if(!isStringArray(3, q) || !isStringArray(1, list.sources) || !isStringArray(1, list.prepared))
		{
			alert("The given spell list is invalid");
			throw "Invalid spell list";
		}

		return {
			sources: list.sources,
			prepared : list.prepared,
			query : q,
			game : list.game || "dnd5e" // dnd was the only option on the last version that didn't add game fields
		}
	}

	/** Loads the spell list with the given name.
	 * Emits an alert() and changes back to the index page if it doesn't exist.
	*/
	export function getSpellList(name : string) : Data.NamedSpellList
	{
		const listJson = window.localStorage.getItem(name)

		if(listJson === null)
			// maybe handle via a custom html page instead, to serve an actual error code
			Util.backToIndex("That spell list doesn't exist! Did you clear browser data?")

		const list = loadSpellList(JSON.parse(listJson));

		return {
			sources: list.sources,
			prepared : list.prepared,
			query : list.query,
			game : list.game,
			name : name
		}
	}

	/** Loads the spells requested by the page URL.
	 * Looks either for the fields `from`, `q` and `game`,
	 * or an URL fragment.
	 *
	 * @param callback Callback to run. Receives the game instance,
	 * 					the requested spells (i.e. those matching the query or the prepared ones on the spell list),
	 * 					and the selected spell list (if any)
	 */
	export async function withSelectedSpells<A>(callback : <T extends ISpell>(g : IGame<T>, spells : T[], l : Data.NamedSpellList | null) => Promise<A>) : Promise<A>
	{
		if(window.location.hash)
		{
			var list = getSpellList(window.location.hash.substring(1));
			var prepared = new Set(list.prepared);

			return await withGameNamed(list.game, async function(g) {
				const spells = (await g.fetchSources(... list.sources)).filter(s => prepared.has(s.name))
				return await callback(g, spells, list)
			})
		}
		else
		{
			return await withGame(async function(g) {
				const q = new URLSearchParams(window.location.search);
				const f = Data.parseQuery(q.get("q") ?? "");
				const spells = (await g.fetchSources(... q.getAll("from"))).filter(s => g.spellMatchesQuery(f, s));

				return await callback(g, spells, null);
			})
		}
	}

	/**
	 * @returns An element that displays a loading animations.
	 */
	export function loading() : HTMLElement
	{
		let l = document.createElement("embed");
		l.height = "5";
		l.src = "loading.svg"

		return l;
	}

	/** Hides or un-hides an element
	 * @param hide Whether to hide or un-hide
	 */
	export function setHidden(element : HTMLElement, hide : boolean)
	{
		element.style.display = hide ? "none" : "initial";
	}
}