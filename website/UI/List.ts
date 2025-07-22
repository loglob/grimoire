/* Handles the list.html UI */

namespace UI
{
	import ISpell = Data.ISpell;
	import IGame = Games.IGame;

	/** Encapsulates the spell list UI */
	class List<TSpell extends ISpell>
	{
		private readonly game : IGame<TSpell>
		private readonly table : Table<TSpell>
		private readonly preparedSet : Set<string>
		private readonly sources : string[]
		private readonly allSpells : TSpell[]
		readonly name : string
		/** The query that produces the spell list's basis */
		private query : Data.Query

		isPrepared(s : TSpell) : boolean
		{
			return this.preparedSet.has(s.name);
		}

		/** @returns Whether all *shown* spells are prepared */
		allPrepared() : boolean
		{
			return this.table.getRows().every(s => this.isPrepared(s.spell));
		}

		static async init<TSpell extends ISpell>(game : IGame<TSpell>, data : Data.NamedSpellList) : Promise<List<TSpell>>
		{
			game.withMaterials(_ => {
				setHidden(Util.getElement("materials-view"), false)
			});

			const sp = await game.fetchSources(... data.sources);
			return new List(game, data, sp)
		}

		/**
		 * @param game
		 * @param sl
		 * @param allSpells Every spell in the selected sources
		 */
		constructor(game : IGame<TSpell>, sl : Data.NamedSpellList, allSpells : TSpell[])
		{
			this.game = game;
			this.preparedSet = new Set(sl.prepared);
			this.sources = sl.sources;
			this.allSpells = allSpells
			this.name = sl.name;
			this.query = sl.query;

			const selAll = document.getElementById("prepare-all") as HTMLInputElement;

			this.table = new Table(game, null, null, s => {
				const inp = document.createElement("input")
				inp.type = "checkbox"
				inp.checked = this.isPrepared(s)
				inp.onclick = _ => {
					// set up checkboxes for preparing
					if(this.isPrepared(s))
					{
						this.preparedSet.delete(s.name);
						inp.checked = false;
						// restore invariant
						selAll.checked = false;
					}
					else
					{
						this.preparedSet.add(s.name);
						inp.checked = true;
						// possibly restore all-quantifier
						selAll.checked = this.allPrepared();
					}

					// eager write-back for now
					this.store();
				}

				const td = document.createElement("td");
				td.appendChild(inp);

				return [td];
			});

			{
				// invariant
				selAll.checked = this.allPrepared();

				selAll.onclick = ev => {
					// bool was already flipped by default code
					const wantState = selAll.checked;

					for (const row of this.table.getRows())
					{
						const cb = row.cells.at(0)!.firstChild as HTMLInputElement

						if(this.isPrepared(row.spell) === wantState)
							continue;

						if(wantState)
							this.preparedSet.add(row.spell.name);
						else
							this.preparedSet.delete(row.spell.name);

						cb.checked = wantState;
						ev.stopPropagation()
					}

					// eager write-back for now
					this.store();
				}

				this.table.onDisplayChange = () => {
					selAll.checked = this.allPrepared();
				}
			}

			game.isPrepared = x => this.isPrepared(x);

			document.title += `: ${this.name}`;


			this.table.insert(allSpells.filter(s => this.includeSpell(s)));

			{
				const nameField = document.getElementById("list-name") as HTMLInputElement;
				nameField.value = this.name;
				nameField.readOnly = true; // TODO editable list name
			}

			{
				const globalSearch = document.getElementById("global-search") as HTMLInputElement;

				// this would be nicer if integrated into the table logic as a sort of multi-level filter
				globalSearch.onchange = _ => {
					if(globalSearch.checked)
						this.table.insert(this.allSpells.filter(s => !this.includeSpell(s)));
					else
						this.table.deleteIf(s => !this.includeSpell(s));
				}
			}

			{
				const downloadList = document.getElementById("download-list") as HTMLButtonElement;

				downloadList.onclick = _ => {
					const data = window.localStorage.getItem(this.name)

					if(data === null)
						alert("Unexpected error saving spell list. " +
							"Your browser settings might prevent local storage (unlikely). " +
							"File a bug report if problem persists")
					else
						window.open(`data:application/json,${encodeURIComponent(data)}`);
				}
			}

			{
				const cardView = document.getElementById("spell-card-view") as HTMLButtonElement;

				cardView.onclick = _ => window.location.href = `cards.html#${this.name}`;
			}

			{
				const matView = document.getElementById("materials-view") as HTMLButtonElement;

				matView.onclick = _ => window.location.href = `materials.html#${this.name}`;
			}
		}

		private includeSpell(s : TSpell)
		{
			return this.game.spellMatchesQuery(this.query, s) || this.preparedSet.has(s.name)
		}

		/** Saves this spell list to local browser storage. */
		store() : void
		{
			const newList : Data.SpellList = {
				query : this.query,
				sources : this.sources,
				prepared : Array.from(this.preparedSet),
				game : this.game.shorthand
			}

			window.localStorage.setItem(this.name, JSON.stringify(newList))
		}
	}

	/** Initialized the spell list UI. Must be called from lists.html on page load. */
	export async function initList()
	{
		if(!window.location.hash)
			window.location.href = "index.html";

		const list = getSpellList(decodeURIComponent(window.location.hash.substring(1)));

		withGameNamed(list.game, async function(g) {
			await List.init(g, list);
		});
	}
}

