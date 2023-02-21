/** Misc. functions for HTML and interacting with the database. */
namespace Util
{
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

	/**
	 * @returns A <b> element displaying the given text
	 */
	export function bold(txt : string) : HTMLElement
	{
		const b = document.createElement("b");
		b.innerText = txt;
		return b;
	}

	/** Hides or un-hides an element
	 * @param hide Whether to hide or un-hide
	 */
	export function setHidden(element : HTMLElement, hide : boolean)
	{
		element.style.display = hide ? "none" : "initial";
	}

	/** Loads the source index from the database
	 * @returns The index showing all known sources
	 */
	export async function getSources() : Promise<{ [id: string] : string }>
	{
		const r = await fetch("db/index.json");
		return await r.json();
	}

	export type NamedSpellList = Spells.SpellList & { name : string };

	/** Loads the spell list with the given name. */
	export function getSpellList(name : string) : NamedSpellList
	{
		const listJson = window.localStorage.getItem(name)

		if(listJson === null)
		{
			// maybe handle via a custom html page instead, to serve an actual error code
			alert("That spell list doesn't exist! Did you clear browser data?");
			window.location.href = "index.html";
		}

		const list = JSON.parse(listJson) as Spells.SpellList;

		return {
			sources: list.sources,
			prepared : list.prepared,
			filter : list.filter,
			name : name
		}
	}
}
