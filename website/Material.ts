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

	/** Divides two amounts
	 * @returns null on unit mismatch or non-whole result
	 * @returns a factor that scaled denom to num
	 */
	function divAmount(num : Amount, denom : Amount) : number | null
	{
		// TODO: unit conversion
		if(num.unit !== denom.unit)
			return null

		// TODO: figure out fractional scaling
		if(num.number % denom.number != 0)
			return null

		return num.number / denom.number
	}

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

		const norm = normalizeAmount(mf, amount)

		if(norm === null)
		{
			console.log(`No such unit '${amount.unit}'`)
			return null
		}

		const n = divAmount(norm, mat.amount)

		if(n === null)
		{
			console.log(`Material only comes in packs of ${mat.amount}, but ${norm} was requested`)
			return null
		}

		return mat.price * n
	}
}