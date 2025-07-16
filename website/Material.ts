namespace Material
{
	/** A scalar with a unit */
	export type Amount = { number: number, unit: string };
	/** Definition of a material
	 * @param unit Unit of `amount`
	 * @param price Number of copper pieces that amount costs
	*/
	export type Material = Readonly<{ amount: Amount, price: number }>;

	/**  The structure of the database file
	 * @param units All defined units, mapped onto an equivalent amount
	 * @param materials
	*/
	export type Manifest = Readonly<{
		units:  { readonly [bigUnit: string]: Amount },
		materials: { readonly [name: string]: Material }
	}>

	/** Converts an amount to base units (the smallest units of each dimension) */
	export function normalizeAmount(mf : Manifest, amt : Amount) : Amount | null
	{
		const base = mf.units[amt.unit]

		if(base === undefined)
			return null

		return { unit: base.unit, number: base.number * amt.number }
	}

	/** Calculates the price of a spell material
	 * @param mf The material manifest
	 * @param name Name of the material. Must be an exact, already processed name
	 * @param amount Amount of material
	 * @returns The total price for the requested materials, or null if the material couldn't be resolved
	 */
	export function solvePrice(mf : Manifest, name : string, amount : Amount) : number | null
	{
		const mat = mf.materials[name.toLowerCase()]

		if(mat === undefined)
		{
			console.log(`No such material '${name}'`)
			return null
		}

		// TODO: maybe cache these somewhere
		const matNorm = normalizeAmount(mf, mat.amount)
		const norm = normalizeAmount(mf, amount)

		if(matNorm === null)
		{
			console.error(`Material ${name} has ill-defined unit '${mat.amount.unit}'`)
			return null
		}

		if(norm === null)
		{
			console.log(`No such unit '${amount.unit}'`)
			return null
		}

		if(norm.unit != matNorm.unit)
		{
			console.log(`Unit mismatch, material comes in ${matNorm.unit}, but ${norm.unit} requested`)
			return null
		}

		return mat.price * norm.number / matNorm.number // return fractional prices
	}
}