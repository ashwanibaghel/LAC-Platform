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

export function validateRevenue(bigha, biswa, biswansi) {
  const values = [bigha, biswa, biswansi].map(Number);
  if (!values.every(Number.isFinite) || values.some((value) => value < 0)) return { valid: false, error: "Use non-negative numbers." };
  if (!Number.isInteger(values[0]) || !Number.isInteger(values[1]) || !Number.isInteger(values[2])) return { valid: false, error: "Revenue fields must be whole numbers." };
  if (values[1] > 19 || values[2] > 19) return { valid: false, error: "Biswa and Biswansi must each be between 0 and 19." };
  return { valid: true, value: { bigha: values[0], biswa: values[1], biswansi: values[2] } };
}

export function parseRevenueShorthand(text) {
  const match = String(text || "").trim().match(/^(\d+)-(\d+)-(\d+)$/);
  if (!match) return { valid: false, error: "Use Bigha-Biswa-Biswansi, for example 2-9-1." };
  return validateRevenue(match[1], match[2], match[3]);
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
