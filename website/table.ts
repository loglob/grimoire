import Spell = Spells.Spell;

namespace Table
{
	export var searchField : HTMLInputElement;

	/** The state of the table */
	let state : { sortOn: keyof Spell, reverse: boolean, filter: string[][][], spells : Spell[], display : Spell[] }
		= { sortOn: "level", reverse: true, filter: [], spells: [], display: [] };

	/** The headers of the spell table, in order */
	const headers : (keyof Spell)[] = [ "name", "level", "school", "castingTime", "ritual", "concentration", "source" ];

	export var customRowElements : (s : Spell) => HTMLTableCellElement[] = null

	/** Initializes the table with the known headers, and sets up the search-field text input to filter the table */
	export function init(q : string|null = null)
	{
		const UP_ARROW = "\u2191";
		const DOWN_ARROW = "\u2193"

		for (const h of headers)
		{
			var x = document.getElementById(`${h}-header`);

			if(x === undefined || x === null)
				throw `No header for ${h}`

			let m = document.createElement("b");
			m.id = `${h}-marker`;
			x.appendChild(m);

			if(h === state.sortOn)
				m.innerText = state.reverse ? UP_ARROW : DOWN_ARROW;

			x.onclick = _ => {
				if(state.sortOn === h)
					state.reverse = !state.reverse;
				else
				{
					document.getElementById(`${state.sortOn}-marker`).innerText = "";
					state.sortOn = h;
					state.reverse = false;
				}

				m.innerText = state.reverse ? UP_ARROW : DOWN_ARROW;
				reset();
				return false;
			}
		}

		searchField = document.getElementById("search-field") as HTMLInputElement;

		searchField.oninput = _ => {
			state.filter = searchField.value
				.split(';').map(x => x
					.split('|').map(y => y
						.split(',').map(z => z.toLowerCase().split(/\s+/).filter(x => x.length).join(' '))));
			reset(false);
		};

		if(q)
		{
			searchField.value = q;
			searchField.oninput(null)
		}
	}

	function compareSpell(l : Spell, r : Spell) : number
	{
		let so = state.sortOn;
		let cmp = l[so] > r[so] ? -1 : l[so] < r[so] ? +1 : 0;

		return (state.reverse ? -cmp : +cmp);
	}

	function toRow(spell : Spell) : HTMLTableRowElement
	{
		var row = document.createElement("tr");

		if(customRowElements)
		{
			for (const cell of customRowElements(spell))
				row.appendChild(cell);
		}

		{
			let cell = document.createElement("td");
			let link = document.createElement("a");
			link.href=`details.html?from=${encodeURIComponent(spell.source)}&spell=${encodeURIComponent(spell.name)}`;
			link.innerText = spell.name;
			cell.appendChild(link);
			cell.classList.add("left");
			row.appendChild(cell);
		}

		let td = (x : string) => {
			let c = document.createElement("td");
			c.innerText = x;
			row.appendChild(c);
		}

		td(spell.level.toString());
		td(spell.school);
		td(spell.castingTime);
		td(spell.ritual ? "yes" : "no");
		td(spell.concentration ? "yes" : "no");
		td(spell.source);

		return row;
	}

	function updateCount()
	{
		const sp = document.getElementById("spell-count") as HTMLSpanElement;
		sp.innerText = `Found ${state.display.length} spells`
	}

	/** Removes all spells that match the predicate from the table and tableState
	 * @param pred The predicate to match for deletion
	 */
	export function filter(pred : (spell: Spell, index: number) => boolean)
	{
		var t = document.getElementById("spells");

		for (let i = state.display.length; i--;)
		{
			if(pred(state.display[i], i))
			{
				state.display.splice(i, 1);
				t.removeChild(t.children[i + 1]);
			}
		}

		state.spells = state.spells.filter((v,i) => !pred(v,i));
		updateCount();
	}

	/** Inserts into the table, preserving sortedness and filtering
	 * @param spells A list of new spells
	*/
	export function insert(spells : Spell[]) : void
	{
		spells.sort(compareSpell);

		if(state.spells.length == 0)
		{
			state.spells = spells;
			state.display = Array(...spells);
			return reset(false);
		}

		const t = document.getElementById("spells");
		let off = 0;

		for(const spell of spells)
		{
			if(!Spells.match(state.filter, spell))
				continue;

			while(off < state.display.length && compareSpell(spell, state.display[off]) > 0)
				off++;

			let row = toRow(spell);

			if(off < state.display.length)
			{
				state.display.splice(off, 0, spell);
				t.insertBefore(row, t.children[1 + off]);
			}
			else
			{
				state.display.push(spell);
				t.appendChild(row);
			}

			off++;
		}

		state.spells.push(...spells);
		updateCount();
	}

	/** Re-sorts and re-filters the table and rebuilds the displayed table from scratch. */
	function reset(resort : boolean = true) : void
	{
		if(resort)
			state.spells.sort(compareSpell);

		const t = document.getElementById("spells");
		state.display = state.spells.filter(s => Spells.match(state.filter, s));

		while(t.childElementCount > 1)
			t.removeChild(t.lastChild);

		for (const s of state.display)
			t.appendChild(toRow(s));

		updateCount();
	}

	/** Returns the parsed filter */
	export function getFilter() : string[][][]
	{
		return state.filter
	}
}