
/** Handles the index.html UI */
namespace UI
{
	import child = Util.child;
	import IMaterialContext = Games.IMaterialContext;
	import IGame = Games.IGame;
	import SpellMaterial = Games.SpellMaterial;

	class Materials<TSpell extends Data.ISpell>
	{
		readonly context : IMaterialContext<TSpell>
		readonly game : IGame<TSpell>

		constructor(context : IMaterialContext<TSpell>, game : IGame<TSpell>)
		{
			this.context = context;
			this.game = game;
		}

		formatMaterial(container : HTMLElement, material : SpellMaterial, prependSeparator : boolean) : void
		{
			if(prependSeparator)
				child(container, "span", "material-sep").innerText = ","

			if(material.amount !== null)
			{
				const amt = material.amount

				child(container, "b", "material-unit").innerText = `${amt.number} ` + (amt.unit == "1" ? '' : `[${amt.unit}] `)
			}

			child(container, "span", "material-name").innerHTML = material.material;

			const price = material.amount == null ? null : Material.solvePrice(this.context.manifest, material.material, material.amount)
			const priceElement = child(container, "b")
			priceElement.innerText = " ("

			if(price === null)
				priceElement.innerText += "?"
			else
				priceElement.appendChild(this.context.formatPrice(price))

			priceElement.appendChild(document.createTextNode(")"))
		}

		formatMaterials(materials : SpellMaterial[]) : HTMLTableCellElement
		{
			const col = document.createElement("td")

			materials.forEach((m, ix) => {
				this.formatMaterial(col, m, ix > 0)
			});

			return col
		}

		formatSpells(table : HTMLTableElement, spells : TSpell[])
		{
			for(const s of spells)
			{
				const row = child(table, "tr")
				const spellRef = child(child(row, "td"), "a") as HTMLLinkElement
				spellRef.href = this.game.spellURL(s)
				spellRef.innerText = s.name

				const all = this.context.extractMaterials(s)
				row.appendChild(this.formatMaterials(all.filter(m => m.consumed == false)))
				row.appendChild(this.formatMaterials(all.filter(m => m.consumed == true)))

				const inp = child(child(row, "td"), "input") as HTMLInputElement
				inp.type = "number"
				inp.max = "9999"
				inp.min = "0"
			}
		}
	}

	/** Initialized the spell list UI. Must be called from lists.html on page load. */
	export async function initMaterials()
	{
		withSelectedSpells(async function(game, spells) {
			const ctx = await game.fetchMaterials()

			if(ctx === null)
			{
				alert("No material data is configured for this instance")
				window.location.href = "index.html"
			}

			const table = document.getElementById("materials") as HTMLTableElement

			new Materials(ctx, game).formatSpells(table, spells)
			return
		})
	}
}