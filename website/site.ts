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
				filterTable(s => s.source === id)
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
			setTable(tableState.entries);
            return false;
		}
	}
}

/** The state of the table */
let tableState : { sortOn: keyof Spell, reverse: boolean, entries : Spell[] } = { sortOn: "level", reverse: true, entries: [] }

/** Removes all spells that match the predicate from the table and tableState
 * @param pred The predicate to match for deletion
 */
function filterTable(pred : (spell: Spell, index: number) => boolean)
{
	var t = document.getElementById("spells");

	for (let i = tableState.entries.length; i--;)
	{
		if(pred(tableState.entries[i], i))
		{
			tableState.entries.splice(i, 1);
			t.removeChild(t.children[i + 1]);
		}
	}
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

/** Inserts into the table, preserving sortedness */
function insertTable(spells : Spell[]) : void
{
	if(tableState.entries.length == 0)
		return setTable(spells);

	const t = document.getElementById("spells");
	let off = 0;

	spells.sort(compareSpell)

	for(const spell of spells)
	{
		while(off < tableState.entries.length && compareSpell(spell, tableState.entries[off]) > 0)
			off++;
		
		let row = toRow(spell);

		if(off < tableState.entries.length)
		{
			tableState.entries.splice(off, 0, spell);
			t.insertBefore(row, t.children[1 + off]);
		}
		else
		{
			tableState.entries.push(spell);
			t.appendChild(row);
		}

		off++;
	}
}

/** Rebuilds the spell table from scratch */
function setTable(spells : Spell[]) : void
{
	if(tableState.sortOn !== null)
		spells.sort(compareSpell);

	tableState.entries = spells;
	const t = document.getElementById("spells");

	while(t.childElementCount > 1)
		t.removeChild(t.lastChild);

	for (const s of tableState.entries)
		t.appendChild(toRow(s));
}

