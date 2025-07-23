/* Handles the list-index.html UI */

namespace UI
{
	function gameName(id : string) : string
	{
		// TODO: put this into Database
		switch(id)
		{
			case "gd": return "Goedendag";
			case "dnd5e": return "D&D 5th Edition";
			case "pf2e": return "Pathfinder 2nd Edition";
			default: return id;
		}
	}

	/** Initialized the spell list UI. Must be called from lists.html on page load. */
	export async function initListIndex()
	{
		const container = Util.getElement("list-index-container");
		const keys = []

		for(let i = 0;; ++i)
		{
			const key = localStorage.key(i);

			if(key === null)
				break;

			keys.push(key)
		}

		const filter = new URLSearchParams(window.location.search).getAll("game");

		for(const key of keys.sort())
		{
			const list = JSON.parse(localStorage.getItem(key)!!) as Data.SpellList;

			if(filter.length > 0 && filter.every(g => g != list.game))
				continue;

			const entry = Util.child(container, "a", "list-item") as HTMLLinkElement;
			entry.href = `list.html#${key}`;

			Util.child(entry, "span", "list-title").innerText = key;

			entry.append(
				document.createElement("br"),
				list.prepared.length.toString(),
				" spells from ", gameName(list.game)
			);
		}
	}
}