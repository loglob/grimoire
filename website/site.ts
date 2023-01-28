function loading()
{
	let l = document.createElement("embed");
	l.height = "5";
	l.src = "loading.svg"

	return l;
}

/**
 * @returns The index showing all known sources
 */
async function getSources() : Promise<{ [id: string] : string }>
{
	const r = await fetch("db/index.json");
	return await r.json();
}

function setHidden(element : HTMLElement, hide : boolean)
{
	element.style.display = hide ? "none" : "initial";
}

let sources : { [id: string] : string } = {}

/** Looks up existing sources and inserts them into the source selector
 * @param preload A list of book IDs to import immediately
*/
async function loadSources(preload : string[])
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
			if(select.checked)
			{
				setHidden(l, false);
				Table.insert(await Spells.getFrom(id));
				setHidden(l, true);
			}
			else
				Table.filter(s => s.source === id);
		}

		container.appendChild(select);
		container.appendChild(l);
		elem?.appendChild(container);

		if(preload.some(x => x.toUpperCase() === id))
		{
			select.checked = true;
			select.onchange(null);
		}
	}
}

function initUI()
{
	const p = new URLSearchParams(window.location.search);
	loadSources(p.getAll("from"));

	Table.init(p.get("q"));

	document.getElementById("static-link").onclick = _ => {
		const fs = Object.keys(sources)
			.filter(x => (document.getElementById(`source_${x}`) as HTMLInputElement)?.checked)
			.map(x => `from=${encodeURIComponent(x)}`)
			.join('&');
		const url = `${window.location.origin}${window.location.pathname}?q=${encodeURIComponent(Table.searchField.value)}${fs.length ? '&' : ''}${fs}`;
		console.log(url);
		navigator.clipboard.writeText(url)
		return false;
	}
}
