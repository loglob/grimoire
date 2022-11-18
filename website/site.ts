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

let spellsBySource : { [index: string] : Spell[] } = {};
let sources : { [id: string] : string } = {}

async function loadSources()
{
	let elem = document.getElementById("source-selector");
	sources = await getSources();
	document.getElementById("source-selector-placeholder")?.remove();

	for (const id in sources) {
		spellsBySource[id] = [];

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
				spellsBySource[id] = [];
			else
			{
				setHidden(l, false);
				spellsBySource[id] = await getSpells(id);
				setHidden(l, true);
			}

			buildTable()
		}

		container.appendChild(select);
		container.appendChild(l);
		elem?.appendChild(container);
	}
}

function buildTable()
{
	var t = document.getElementById("spells");

	while(t.childElementCount > 1)
		t.removeChild(t.lastChild);

	for(const src in spellsBySource)
	{
		for(const spell of spellsBySource[src])
		{
			var row = document.createElement("tr");
			let td = (x : string) => { let c = document.createElement("td"); c.innerText = x; row.appendChild(c) }

			td(spell.name);
			td(spell.level.toString());
			td(spell.school);
			td(spell.castingTime);
			td(spell.ritual ? "yes" : "no");
			td(spell.concentration ? "yes" : "no");
			td(sources[spell.source]);

			t.appendChild(row);
		}
	}
}

document.addEventListener("load", _ => loadSources());