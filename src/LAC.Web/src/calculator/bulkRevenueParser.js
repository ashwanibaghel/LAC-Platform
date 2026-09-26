import { allAreaConversions, fromTotalBiswansi, revenueTotals, toTotalBiswansi } from "./landConversions.js";

export const BULK_FORMATS = {
  auto: "Auto detect",
  shorthand: "Revenue shorthand (Bigha-Biswa-Biswansi)",
  columns3: "3 columns: Bigha | Biswa | Biswansi",
  columns4: "4 columns: Reference | Bigha | Biswa | Biswansi",
  transposed: "3 rows: Bigha / Biswa / Biswansi",
  separate: "Separate Unit Lists",
};

const integer = (text, unit) => {
  const value = String(text ?? "").trim();
  if (!/^\d+$/.test(value)) return { error: `Invalid ${unit} value "${value}".` };
  const number = Number(value);
  if (!Number.isSafeInteger(number)) return { error: `${unit} value is too large for exact calculation.` };
  if (unit !== "Bigha" && number > 19) return { error: `${unit} must be between 0 and 19.` };
  return { value: number };
};

const record = (index, sourceRow, cells, reference = "") => {
  const [bighaRaw, biswaRaw, biswansiRaw] = cells;
  const parsed = [integer(bighaRaw, "Bigha"), integer(biswaRaw, "Biswa"), integer(biswansiRaw, "Biswansi")];
  const error = parsed.find(component => component.error)?.error;
  const value = error ? null : { bigha: parsed[0].value, biswa: parsed[1].value, biswansi: parsed[2].value };
  if (value && !Number.isSafeInteger(toTotalBiswansi(value))) return { index, sourceRow, reference, cells, value: null, error: "Revenue area exceeds the exact integer range." };
  return { index, sourceRow, reference, cells, value, error: error || null };
};

const problem = (index, sourceRow, cells, error, reference = "") => ({ index, sourceRow, reference, cells, value: null, error });
const result = (format, rows, unresolved = false, recordCount = rows.length) => ({
  format, label: BULK_FORMATS[format], rows, unresolved, recordCount,
  validCount: rows.filter(row => row.value).length,
  errorCount: rows.filter(row => row.error).length,
});
const linesOf = text => {
  const lines = String(text).replace(/\r\n?/g, "\n").split("\n");
  while (lines.length && lines[0] === "") lines.shift();
  while (lines.length && lines.at(-1) === "") lines.pop();
  return lines;
};
const headerKey = cell => cell.trim().toLowerCase().replace(/[^a-z0-9]/g, "");
const isHeader = (cells, format) => {
  const keys = cells.map(headerKey);
  const expected = format === "columns4" ? ["reference", "bigha", "biswa", "biswansi"] : ["bigha", "biswa", "biswansi"];
  if (format === "columns4" && ["khasra", "khasrano", "sno"].includes(keys[0])) keys[0] = "reference";
  return keys.length === expected.length && keys.every((key, index) => key === expected[index]);
};

export function tokenizeUnitList(text) {
  if (!String(text).trim()) return [];
  return String(text).replace(/\r\n?/g, "\n").trim().split(/[,\t\n]/).map(token => token.trim());
}

export function detectBulkRevenueFormat(text) {
  const source = String(text).trim();
  if (!source) return null;
  const shorthandTokens = tokenizeUnitList(source);
  if (shorthandTokens.length && shorthandTokens.every(token => /^\d+-\d+-\d+$/.test(token))) return "shorthand";
  const lines = linesOf(source);
  const cells = lines.map(line => line.split("\t"));
  // A 3-by-3 numeric matrix could represent records or transposed units.
  if (lines.length > 1 && lines.length !== 3 && cells.every(row => row.length === 3 && row.every(cell => /^\d+$/.test(cell.trim()))) ) return "columns3";
  if (lines.length > 1 && isHeader(cells[0], "columns3") && cells.slice(1).every(row => row.length === 3 && row.every(cell => /^\d+$/.test(cell.trim())))) return "columns3";
  return null;
}

export function parseRevenueShorthandBulk(text) {
  const tokens = tokenizeUnitList(text);
  return result("shorthand", tokens.map((token, index) => {
    const match = token.match(/^(\d+)-(\d+)-(\d+)$/);
    return match ? record(index + 1, index + 1, match.slice(1)) : problem(index + 1, index + 1, [token], `Invalid revenue shorthand "${token}". Expected Bigha-Biswa-Biswansi.`);
  }));
}

function parseColumnTable(text, format) {
  const lines = linesOf(text);
  const expected = format === "columns4" ? 4 : 3;
  const first = lines[0]?.split("\t") || [];
  const start = isHeader(first, format) ? 1 : 0;
  const rows = lines.slice(start).map((line, index) => {
    const sourceRow = index + start + 1;
    const cells = line.split("\t").map(cell => cell.trim());
    const reference = format === "columns4" ? cells[0] ?? "" : "";
    if (cells.length !== expected) return problem(index + 1, sourceRow, cells, `Expected ${expected} columns, found ${cells.length}.`, reference);
    return record(index + 1, sourceRow, format === "columns4" ? cells.slice(1) : cells, reference);
  });
  return result(format, rows);
}

export const parseRevenueColumnTable = text => parseColumnTable(text, "columns3");
export const parseRevenueReferenceTable = text => parseColumnTable(text, "columns4");

export function parseTransposedRevenueRows(text) {
  const lines = linesOf(text);
  if (lines.length !== 3) return result("transposed", [problem(1, 1, [], `Expected 3 unit rows, found ${lines.length}.`)]);
  const lists = lines.map(line => line.split("\t").map(cell => cell.trim()));
  const count = Math.max(...lists.map(list => list.length));
  return result("transposed", Array.from({ length: count }, (_, index) => record(index + 1, index + 1, lists.map(list => list[index] ?? ""))));
}

export function parseSeparateUnitLists(bigha, biswa, biswansi) {
  const lists = [tokenizeUnitList(bigha), tokenizeUnitList(biswa), tokenizeUnitList(biswansi)];
  const count = Math.max(...lists.map(list => list.length));
  const mismatch = lists.findIndex(list => list.length > 0 && list.length !== count);
  if (mismatch !== -1) {
    const label = ["Bigha", "Biswa", "Biswansi"][mismatch];
    return result("separate", [problem(1, 1, [], `${label} contains ${lists[mismatch].length} values but ${count} records are required.`)], false, count);
  }
  return result("separate", Array.from({ length: count }, (_, index) => record(index + 1, index + 1, lists.map(list => list.length ? list[index] : "0"))));
}

export function parseBulkRevenue(text, selectedFormat = "auto") {
  const format = selectedFormat === "auto" ? detectBulkRevenueFormat(text) : selectedFormat;
  if (!format) return result("auto", [], Boolean(String(text).trim()));
  if (format === "shorthand") return parseRevenueShorthandBulk(text);
  if (format === "columns3") return parseRevenueColumnTable(text);
  if (format === "columns4") return parseRevenueReferenceTable(text);
  if (format === "transposed") return parseTransposedRevenueRows(text);
  throw new RangeError("Unknown bulk revenue format.");
}

export function sumBulkRevenueRows(parsed) {
  if (parsed.unresolved || !parsed.rows.length || parsed.errorCount) throw new RangeError("Resolve all parsing errors before calculating a total.");
  let totalBiswansi = 0;
  for (const row of parsed.rows) {
    totalBiswansi += toTotalBiswansi(row.value);
    if (!Number.isSafeInteger(totalBiswansi)) throw new RangeError("Bulk total exceeds the exact integer range.");
  }
  const revenue = fromTotalBiswansi(totalBiswansi);
  const sqm = totalBiswansi * 2.1075;
  return { records: parsed.rows.length, totalBiswansi, revenue, totals: revenueTotals(revenue), sqm, conversions: allAreaConversions(sqm) };
}

export function normalizedRowsForExcel(parsed) {
  if (parsed.unresolved || !parsed.rows.length || parsed.errorCount) throw new RangeError("Resolve all parsing errors before copying normalized rows.");
  const withReference = parsed.rows.some(row => row.reference);
  const header = withReference ? "Reference\tBigha\tBiswa\tBiswansi\tRevenue Area" : "Bigha\tBiswa\tBiswansi\tRevenue Area";
  return [header, ...parsed.rows.map(row => {
    const { bigha, biswa, biswansi } = row.value;
    return [...(withReference ? [row.reference] : []), bigha, biswa, biswansi, `${bigha}-${biswa}-${biswansi}`].join("\t");
  })].join("\n");
}
