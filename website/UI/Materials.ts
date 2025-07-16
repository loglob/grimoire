
/** Handles the index.html UI */
namespace UI
{
	import child = Util.child;
	import IMaterialContext = Games.IMaterialContext;
	import IGame = Games.IGame;
	import SpellMaterial = Games.SpellMaterial;

	type Price = Readonly<{ value: number, unsure: boolean }>

	const ZERO : Price = { value: 0, unsure: false }

	function scale(p : Price, x : number) : Price
	{
		return { value: p.value * x, unsure: p.unsure }
	}

	function sum(a : Price, ...bs : Price[])
	{
		for (const b of bs)
			a = { value: a.value + b.value, unsure: a.unsure || b.unsure }

		return a
	}

	/** State container for observing  */
	class SumGadget<T extends Data.ISpell>
	{
		readonly parent : Materials<T>;
		public readonly consumed : Price;
		public readonly persistent : Price;
		readonly input : HTMLInputElement;
		castCount : number = 0;

		constructor(parent : Materials<T>, persistent : Price, consumed : Price, input : HTMLInputElement)
		{
			this.parent = parent;
			this.persistent = persistent;
			this.consumed = consumed;
			this.input = input;

			input.onchange = _ => this.listener();
		}

		listener()
		{
			const newValue = parseInt(this.input.value)

			if(newValue == this.castCount || isNaN(newValue) || newValue < 0)
				return

			this.castCount = newValue
			this.parent.recomputeTotals()
		}
	}

	class Materials<TSpell extends Data.ISpell>
	{
		readonly context : IMaterialContext<TSpell>
		readonly game : IGame<TSpell>
		readonly gadgets : SumGadget<TSpell>[] = []

		constructor(context : IMaterialContext<TSpell>, game : IGame<TSpell>)
		{
			this.context = context;
			this.game = game;
		}

		/** Formats a single material
		 * @param container An element to append content to
		 * @param material The material to process
		 * @param prependSeparator If true, puts a separator before the content
		 * @returns The price of a single instance of the material
		 * @returns null on parsing/unit error when calculating price
		 */
		formatMaterial(container : HTMLElement, material : SpellMaterial, prependSeparator : boolean) : number | null
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

			return price
		}

		formatPrice(p : Price) : (string|Node)[]
		{
			if(p.unsure && p.value == 0)
				return ["???"]

			const price = this.context.formatPrice(p.value)
			return p.unsure ? [price, "+?"] : [price]
		}

		formatMaterials(row : HTMLTableRowElement, materials : SpellMaterial[]) : Price
		{
			const result = { value: 0, unsure: false }
			const left = child(row, "td")

			materials.forEach((m, ix) => {
				const price = this.formatMaterial(left, m, ix > 0)

				if(price === null)
					result.unsure = true
				else
					result.value += price
			});

			const right = child(row, "td")

			if(result.value > 0)
				right.append(...this.formatPrice(result))

			return result
		}

		formatSpells(table : HTMLTableElement, spells : TSpell[])
		{
			for(const s of spells)
			{
				const row = child(table, "tr") as HTMLTableRowElement
				const spellRef = child(child(row, "td"), "a") as HTMLLinkElement
				spellRef.href = this.game.spellURL(s)
				spellRef.innerText = s.name

				const all = this.context.extractMaterials(s)
				const persist = this.formatMaterials(row, all.filter(m => m.consumed == false))
				const consumed = this.formatMaterials(row, all.filter(m => m.consumed == true))

				const inp = child(child(row, "td"), "input") as HTMLInputElement
				inp.type = "number"
				inp.max = "9999"
				inp.min = "0"

				this.gadgets.push(new SumGadget(this, persist, consumed, inp))
			}
		}

		/** regenerates the totals displayed under the table */
		recomputeTotals()
		{
			var totalCasts = 0
			var totalSpells = 0
			var persistentSum = ZERO
			var consumedSum = ZERO

			for(const g of this.gadgets)
			{
				totalCasts += g.castCount

				if(g.castCount > 0)
				{
					++totalSpells;
					persistentSum = sum(persistentSum, g.persistent);
					consumedSum = sum(consumedSum, scale(g.consumed, g.castCount))
				}
			}

			const consumedContainer = document.getElementById("consumed-totals")
			const persistentContainer = document.getElementById("persistent-totals")
			consumedContainer.innerHTML = "";
			persistentContainer.innerHTML = "";

			consumedContainer.append(
				`Material cost for all ${totalCasts} casting${totalCasts != 1 ? 's' : ''} is `,
				Util.wrap("b", ...this.formatPrice(consumedSum))
			);

			persistentContainer.append(
				`Casting all ${totalSpells} selected spell${totalSpells != 1 ? 's' : ''} requires an additional `,
				Util.wrap("b", ...this.formatPrice(persistentSum)),
				` in persistent components`
			)
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

			const mat = new Materials(ctx, game)
			mat.formatSpells(table, spells)
			mat.recomputeTotals()

			return
		})
	}
}