/* Handles the list.html UI */
namespace List
{
	/** Saves a spell list with a modified prepared set.
	 * Updated the current
	 * @param list The current spell list
	 * @param prepared The new prepared set
	 */
	function storeWith(list : Util.NamedSpellList, prepared : Iterable<string>|ArrayLike<string>)
	{
		const newList : Spells.SpellList = {
			filter : list.filter,
			sources : list.sources,
			prepared : Array.from(prepared)
		}

		window.localStorage.setItem(list.name, JSON.stringify(newList))
	}

	/** Initialized the spell list UI. Must be called from lists.html on page load. */
	export async function initUI()
	{
		if(!window.location.hash)
			window.location.href = "index.html";

		const list = Util.getSpellList(window.location.hash.substring(1));
		var preparedSet = new Set(list.prepared);

		document.title += `: ${list.name}`;

		{
			const nameField = document.getElementById("list-name") as HTMLInputElement;
			nameField.value = list.name;
			nameField.readOnly = true; // TODO editable list name
		}

		Spells.isPrepared = x => preparedSet.has(x.name);

		// TODO select all
		Table.customRowElements = s => {
			const inp = document.createElement("input")
			inp.type = "checkbox"
			inp.checked = preparedSet.has(s.name)
			inp.onclick = _ => {
				if(preparedSet.has(s.name))
				{
					preparedSet.delete(s.name);
					inp.checked = false;
				}
				else
				{
					preparedSet.add(s.name);
					inp.checked = true;
				}

				console.log(preparedSet);
				// eager write-back for now
				storeWith(list, preparedSet)
			}

			const td = document.createElement("td");
			td.appendChild(inp);

			return [td];
		};

		const spells = (await Promise.all(list.sources.map(Spells.getFrom))).flat();
		const inclSpell = function(s : Spell) { return Spells.match(list.filter, s) || preparedSet.has(s.name) }

		Table.insert(spells.filter(inclSpell));
		Table.init();

		{
			const globalSearch = document.getElementById("global-search") as HTMLInputElement;

			// this would be nicer if integrated into the table logic as a sort of multi-level filter
			globalSearch.onchange = _ => {
				if(globalSearch.checked)
					Table.insert(spells.filter(s => !inclSpell(s)));
				else
					Table.filter(s => !inclSpell(s));
			}
		}

		{
			const downloadList = document.getElementById("download-list") as HTMLButtonElement;

			downloadList.onclick = _ =>
				window.open(`data:application/json,${encodeURIComponent(window.localStorage.getItem(list.name))}`);
		}

		{
			const cardView = document.getElementById("spell-card-view") as HTMLButtonElement;

			cardView.onclick = _ =>
				window.location.href = `cards.html#${list.name}`;
		}
	}
}

