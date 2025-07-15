
/** Handles the index.html UI */
namespace UI
{
	import child = Util.child;

	/** Initialized the spell list UI. Must be called from lists.html on page load. */
	export async function initMaterials()
	{
		withSelectedSpells(async function(game, spells, list) {
			const ctx = await game.fetchMaterials()

			if(ctx === null)
			{
				alert("No material data is configured for this instance")
				window.location.href = "index.html"
			}

			const table = document.getElementById("materials") as HTMLTableElement

			for(const s of spells)
			{
				const row = child(table, "tr")
				child(row, "td").innerText = s.name

				const all = ctx.extractMaterials(s)

				if(all.some(x => x === null))
				{
					child(child(row, "td"), "b").innerText = "?"
					child(child(row, "td"), "b").innerText = "?"
					continue
				}


				const persistent = all.filter(m => m.consumed == false)
				const consumed = all.filter(m => m.consumed == true)


				child(row, "td").innerHTML = persistent.map(x => x.material).join("  ");
				child(row, "td").innerHTML = consumed.map(x => x.material).join("  ")

				const inp = child(child(row, "td"), "input") as HTMLInputElement
				inp.type = "number"
			}
		})
	}
}