
namespace UI
{	
	import IGame = Games.IGame
	import ISpell = Data.ISpell

	export const games = {
		dnd5e: "D&D 5e"
	};

	export async function withGameNamed<A>(id : string, f : <T extends ISpell>(g : IGame<T>) => Promise<A>) : Promise<A>
	{
		switch(id)
		{
			case "dnd5e":
				return await f(new Games.DnD5e.Game(id, games[id]));

			default:
				alert(`Invalid game ID: '${id}'. Either the URL is wrong or your browser cache is outdated.`);
				window.location.href = "/";
				throw "bad game ID";
		}
	}

	/** Invokes a function that is generic over all games */
	export async function withGame<A>(f : <T extends ISpell>(g : IGame<T>) => Promise<A>) : Promise<A>
	{
		const id = new URLSearchParams(window.location.search).get("game") || "dnd5e";
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
			query : list.query || list.filter,
			game : list.game || "dnd5e"
		}
	}

	/** Loads the spell list with the given name.
	 * Emits an alert() and changes back to the index page if it doesn't exist.
	*/
	export function getSpellList(name : string) : Data.NamedSpellList
	{
		const listJson = window.localStorage.getItem(name)

		if(listJson === null)
		{
			// maybe handle via a custom html page instead, to serve an actual error code
			alert("That spell list doesn't exist! Did you clear browser data?");
			window.location.href = "index.html";
			return;
		}

		const list = loadSpellList(JSON.parse(listJson));

		return {
			sources: list.sources,
			prepared : list.prepared,
			query : list.query,
			game : list.game,
			name : name
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