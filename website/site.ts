function loading()
{
	let l = document.createElement("embed");
	l.height = "5";
	l.src = "loading.svg"

	return l;
}

function setHidden(element : HTMLElement, hide : boolean)
{
	element.style.display = hide ? "none" : "initial";
}

let sources : { [id: string] : string } = {}

async function loadSources()
{
	let elem = document.getElementById("source-selector");
	sources = await getSources();
	document.getElementById("source-selector-placeholder")?.remove();

	for (const id in sources)
	{
		let container = document.createElement("div");
		container.innerText = sources[id];

		let l = loading();
		setHidden(l, true);
		let select = document.createElement("input");

		select.type = "checkbox";
		select.id = `source_${id}`;
		select.checked = false;

		select.onchange = async _ => {
			if(!select.checked)
				filterTable(s => s.source === id);
			else
			{
				setHidden(l, false);
				insertTable(await getSpells(id));
				setHidden(l, true);
			}
		}

		container.appendChild(select);
		container.appendChild(l);
		elem?.appendChild(container);
	}

	const sf = document.getElementById("search-field") as HTMLInputElement;
	sf.oninput = _ => {
		tableState.filter = sf.value
			.split(';').map(x => x
				.split('|').map(y => y
					.split(',').map(z => z.toLowerCase().split(/\s+/).filter(x => x.length).join(' '))));
		resetTable(false);
	};
}

/** The headers of the spell table, in order */
const headers : (keyof Spell)[] = [ "name", "level", "school", "castingTime", "ritual", "concentration", "source" ];

function initUI()
{
	loadSources();

	for (const h of headers)
	{
		var x = document.getElementById(`${h}-header`);

		if(x === undefined || x === null)
			throw `No header for ${h}`

		let m = document.createElement("b");
		m.id = `${h}-marker`;
		x.appendChild(m);

		if(h === tableState.sortOn)
			m.innerText = tableState.reverse ? "\u2191" : "\u2193";

		x.onclick = _ => {
			if(tableState.sortOn === h)
				tableState.reverse = !tableState.reverse;
			else
			{
				document.getElementById(`${tableState.sortOn}-marker`).innerText = "";
				tableState.sortOn = h;
				tableState.reverse = false;
			}

			m.innerText = tableState.reverse ? "\u2191" : "\u2193";
			resetTable();
			return false;
		}
	}
}

/** The state of the table */
let tableState : { sortOn: keyof Spell, reverse: boolean, filter: string[][][], spells : Spell[], display : Spell[] }
	= { sortOn: "level", reverse: true, filter: [], spells: [], display: [] };

function spellMatches(s : Spell) : boolean
{
	function keyword(s : Spell, field : keyof Spell)
	{
		return (s[field] ? '' : '!') + field;
	}

	function numRange(s : string, v : number)
	{
		let vars = s.split('-').map(x => Number.parseInt(x));
		if(vars.length < 1 || vars.length > 2 || vars.some(x => !isFinite(x)))
			return false;
		else if(vars.length > 1)
			return vars[0] <= v && v <= vars[1];
		else
			return vars[0] == v;
	}

	return tableState.filter
		.every(x => x
			.some(y => y
				.every(z => s.name.toLocaleLowerCase().includes(z)
					|| s.classes.some(c => c.toLowerCase() === z)
					|| s.school.toLowerCase() === z
					|| s.castingTime.toLowerCase() === z
					|| s.duration.toLowerCase() === z
					|| z === keyword(s, "ritual")
					|| z === keyword(s, "concentration")
					|| z === keyword(s, "upcast")
					|| (z.length && z[0] === 'l' && numRange(z.substring(1), s.level))
		)	)	)
}

function compareSpell(l : Spell, r : Spell) : number
{
	let so = tableState.sortOn;
	let cmp = l[so] > r[so] ? -1 : l[so] < r[so] ? +1 : 0;

	return (tableState.reverse ? -cmp : +cmp);
}

function toRow(spell : Spell) : HTMLTableRowElement
{
	var row = document.createElement("tr");
	let td = (x : string) => {
		let c = document.createElement("td");
		c.innerText = x;
		row.appendChild(c);
	}

	{
		let cell = document.createElement("td");
		let link = document.createElement("a");
		link.href=`details.html?from=${encodeURIComponent(spell.source)}&spell=${encodeURIComponent(spell.name)}`;
		link.innerText = spell.name;
		cell.appendChild(link);
		row.appendChild(cell);
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
	sp.innerText = `Found ${tableState.display.length} spells`
}

/** Removes all spells that match the predicate from the table and tableState
 * @param pred The predicate to match for deletion
 */
function filterTable(pred : (spell: Spell, index: number) => boolean)
{
	var t = document.getElementById("spells");

	for (let i = tableState.display.length; i--;)
	{
		if(pred(tableState.display[i], i))
		{
			tableState.display.splice(i, 1);
			t.removeChild(t.children[i + 1]);
		}
	}

	tableState.spells = tableState.spells.filter((v,i) => !pred(v,i));
	updateCount();
}

/** Inserts into the table, preserving sortedness and filtering
 * @param spells A list of new spells
*/
function insertTable(spells : Spell[]) : void
{
	spells.sort(compareSpell);

	if(tableState.spells.length == 0)
	{
		tableState.spells = spells;
		tableState.display = Array(...spells);
		return resetTable(false);
	}

	const t = document.getElementById("spells");
	let off = 0;

	for(const spell of spells)
	{
		if(!spellMatches(spell))
			continue;

		while(off < tableState.display.length && compareSpell(spell, tableState.display[off]) > 0)
			off++;

		let row = toRow(spell);

		if(off < tableState.display.length)
		{
			tableState.display.splice(off, 0, spell);
			t.insertBefore(row, t.children[1 + off]);
		}
		else
		{
			tableState.display.push(spell);
			t.appendChild(row);
		}

		off++;
	}

	tableState.spells.push(...spells);
	updateCount();
}

/** Re-sorts and re-filters the table and rebuilds the displayed table from scratch. */
function resetTable(resort : boolean = true) : void
{
	if(resort)
		tableState.spells.sort(compareSpell);

	const t = document.getElementById("spells");
	tableState.display = tableState.spells.filter(spellMatches);

	while(t.childElementCount > 1)
		t.removeChild(t.lastChild);

	for (const s of tableState.display)
		t.appendChild(toRow(s));

	updateCount();
}
