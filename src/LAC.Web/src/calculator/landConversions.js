export const AREA_UNITS = {
  bigha: { label: "Bigha", sqm: 843 },
  biswa: { label: "Biswa", sqm: 42.15 },
  biswansi: { label: "Biswansi", sqm: 2.1075 },
  sqm: { label: "Square metre", sqm: 1 },
  sqyd: { label: "Square yard / Gaj", sqm: 0.83612736 },
  sqft: { label: "Square foot", sqm: 0.09290304 },
  hectare: { label: "Hectare", sqm: 10000 },
  acre: { label: "Acre", sqm: 4046.8564224 },
  sqkm: { label: "Square kilometre", sqm: 1000000 },
};

export const LENGTH_UNITS = {
  ghatta: { label: "Ghatta", metres: 8.25 * 0.3048 },
  feet: { label: "Feet", metres: 0.3048 },
  metre: { label: "Metre", metres: 1 },
  yard: { label: "Yard", metres: 0.9144 },
};

export function isRevenueAreaUnit(unit) {
  return unit === "bigha" || unit === "biswa" || unit === "biswansi";
}

export function validateRevenue(bigha, biswa, biswansi) {
  const values = [bigha, biswa, biswansi].map(Number);
  if (!values.every(Number.isFinite) || values.some((value) => value < 0)) return { valid: false, error: "Use non-negative numbers." };
  if (!Number.isInteger(values[0]) || !Number.isInteger(values[1]) || !Number.isInteger(values[2])) return { valid: false, error: "Revenue fields must be whole numbers." };
  if (values[1] > 19 || values[2] > 19) return { valid: false, error: "Biswa and Biswansi must each be between 0 and 19." };
  if (!Number.isSafeInteger(values[0] * 400 + values[1] * 20 + values[2])) return { valid: false, error: "Revenue area exceeds the exact integer range." };
  return { valid: true, value: { bigha: values[0], biswa: values[1], biswansi: values[2] } };
}

export function parseRevenueShorthand(text) {
  const area = parseAreaInput(text, "bigha");
  if (!area.valid) return area;
  const [whole, fraction = ""] = area.canonical.split(".");
  const denominator = 10n ** BigInt(fraction.length);
  const total = (BigInt(whole) * denominator + BigInt(fraction || "0")) * 400n;
  if (total % denominator !== 0n || total / denominator > BigInt(Number.MAX_SAFE_INTEGER)) return { valid: false, error: "Revenue arithmetic requires an exact whole Biswansi total." };
  return { valid: true, value: fromTotalBiswansi(Number(total / denominator)) };
}

// All revenue inputs share this parser. BigInt preserves notation without rounding.
export function parseAreaInput(input, unit = "bigha") {
  const text = String(input ?? "").trim();
  const invalid = error => ({ valid: false, error });
  if (!AREA_UNITS[unit]) return invalid("Unknown area unit.");
  if (text.includes("-")) {
    const match = text.match(/^(\d+)-(\d+)(?:-(\d+))?$/);
    if (unit !== "bigha" || !match) return invalid("Use 4-16 or 2-9-1 with Bigha; negative areas and malformed separators are invalid.");
    const bigha = BigInt(match[1]), biswa = BigInt(match[2]), biswansi = BigInt(match[3] ?? "0");
    if (biswa > 19n || biswansi > 19n) return invalid("Biswa and Biswansi must each be between 0 and 19.");
    const quarters = bigha * 10000n + biswa * 500n + biswansi * 25n;
    if (quarters > 10000000000000000n) return invalid("Area exceeds the 10^12 limit.");
    const fraction = (quarters % 10000n).toString().padStart(4, "0").replace(/0+$/, "");
    const canonical = (quarters / 10000n).toString() + (fraction ? "." + fraction : "");
    return { valid: true, canonical, number: Number(canonical), interpretation: `${bigha} Bigha ${biswa} Biswa${match[3] ? ` ${biswansi} Biswansi` : ""} = ${canonical} Bigha` };
  }
  if (!/^\d+(?:\.\d{1,12})?$/.test(text)) return invalid("Enter a non-negative plain decimal with at most 12 fractional digits.");
  const [whole, frac = ""] = text.split(".");
  const integer = BigInt(whole);
  if (integer > 1000000000000n || (integer === 1000000000000n && /[1-9]/.test(frac))) return invalid("Area exceeds the 10^12 limit.");
  const fraction = frac.replace(/0+$/, "");
  const canonical = integer.toString() + (fraction ? "." + fraction : "");
  return { valid: true, canonical, number: Number(canonical), interpretation: `${canonical} ${AREA_UNITS[unit].label}` };
}

// Revenue arithmetic is deliberately integer-only.  Area conversion may use
// decimals, but a valid Delhi revenue triplet always has an exact Biswansi total.
export function toTotalBiswansi(revenue) {
  const checked = validateRevenue(revenue?.bigha, revenue?.biswa, revenue?.biswansi);
  if (!checked.valid) throw new RangeError(checked.error);
  const { bigha, biswa, biswansi } = checked.value;
  return bigha * 400 + biswa * 20 + biswansi;
}

export function fromTotalBiswansi(total) {
  const value = Number(total);
  if (!Number.isSafeInteger(value) || value < 0) throw new RangeError("Total Biswansi must be a non-negative whole number.");
  const bigha = Math.floor(value / 400);
  const remaining = value % 400;
  return { bigha, biswa: Math.floor(remaining / 20), biswansi: remaining % 20 };
}

export function addRevenueAreas(values) {
  if (!Array.isArray(values) || values.length === 0) throw new RangeError("Add at least one revenue area.");
  return fromTotalBiswansi(values.reduce((total, value) => total + toTotalBiswansi(value), 0));
}

export function subtractRevenueAreas(a, b) {
  const remainder = toTotalBiswansi(a) - toTotalBiswansi(b);
  if (remainder < 0) throw new RangeError("The subtraction result cannot be negative.");
  return fromTotalBiswansi(remainder);
}

export function revenueTotals(revenue) {
  const totalBiswansi = toTotalBiswansi(revenue);
  return { totalBiswansi, totalBiswa: totalBiswansi / 20, totalBigha: totalBiswansi / 400 };
}

export function normalizeRevenueTotal(value, unit) {
  const number = Number(value);
  if (!Number.isSafeInteger(number) || number < 0) throw new RangeError("Enter a non-negative whole total.");
  const multiplier = unit === "bigha" ? 400 : unit === "biswa" ? 20 : unit === "biswansi" ? 1 : null;
  if (multiplier === null) throw new RangeError("Unknown revenue unit.");
  return fromTotalBiswansi(number * multiplier);
}

export function revenueToSqm(revenue) {
  return revenue.bigha * 843 + revenue.biswa * 42.15 + revenue.biswansi * 2.1075;
}

export function sqmToRevenue(squareMetres) {
  const totalBiswansi = Number(squareMetres) / 2.1075;
  const bigha = Math.floor(totalBiswansi / 400);
  const afterBigha = totalBiswansi - bigha * 400;
  const biswa = Math.floor(afterBigha / 20);
  const biswansi = afterBigha - biswa * 20;
  return { bigha, biswa, biswansi, totalBiswansi };
}

// Display is rounded only at the final revenue notation boundary. This keeps
// conversion math precise while preventing floating-point tails in the UI.
export function formatRevenueFromSqm(squareMetres) {
  const roundedTotal = Math.round((Number(squareMetres) / 2.1075) * 10000) / 10000;
  let bigha = Math.floor(roundedTotal / 400);
  let remaining = roundedTotal - bigha * 400;
  let biswa = Math.floor(remaining / 20);
  let biswansi = Math.round((remaining - biswa * 20) * 10000) / 10000;
  if (biswansi >= 20) { biswansi = 0; biswa += 1; }
  if (biswa >= 20) { biswa = 0; bigha += 1; }
  return `${bigha}-${biswa}-${Number(biswansi.toFixed(4))}`;
}

export function areaToSqm(value, unit) {
  const number = Number(value);
  if (!Number.isFinite(number) || number < 0 || !AREA_UNITS[unit]) return null;
  return number * AREA_UNITS[unit].sqm;
}

export function sqmToArea(squareMetres, unit) {
  return Number(squareMetres) / AREA_UNITS[unit].sqm;
}

export function allAreaConversions(squareMetres) {
  return Object.fromEntries(Object.keys(AREA_UNITS).map((unit) => [unit, sqmToArea(squareMetres, unit)]));
}

export function lengthToMetres(value, unit) {
  const number = Number(value);
  if (!Number.isFinite(number) || number < 0 || !LENGTH_UNITS[unit]) return null;
  return number * LENGTH_UNITS[unit].metres;
}

export function metresToLength(metres, unit) {
  return Number(metres) / LENGTH_UNITS[unit].metres;
}
