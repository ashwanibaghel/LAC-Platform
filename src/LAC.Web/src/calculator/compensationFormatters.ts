const ONES = [
  "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
  "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen",
  "Seventeen", "Eighteen", "Nineteen"
];

const TENS = [
  "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
];

function convertUnderThousand(n: number): string {
  if (n === 0) return "";
  if (n < 20) return ONES[n];
  if (n < 100) {
    const rem = n % 10;
    return TENS[Math.floor(n / 10)] + (rem ? ` ${ONES[rem]}` : "");
  }
  const hundreds = Math.floor(n / 100);
  const rem = n % 100;
  return `${ONES[hundreds]} Hundred` + (rem ? ` ${convertUnderThousand(rem)}` : "");
}

function numberToIndianWordsInteger(n: number): string {
  if (n < 1000) return convertUnderThousand(n);
  const crores = Math.floor(n / 10000000);
  const rem = n % 10000000;
  const parts: string[] = [];
  if (crores > 0) parts.push(`${numberToIndianWordsInteger(crores)} Crore`);
  const lakhs = Math.floor(rem / 100000);
  const rem2 = rem % 100000;
  if (lakhs > 0) parts.push(`${convertUnderThousand(lakhs)} Lakh`);
  const thousands = Math.floor(rem2 / 1000);
  const rem3 = rem2 % 1000;
  if (thousands > 0) parts.push(`${convertUnderThousand(thousands)} Thousand`);
  if (rem3 > 0) parts.push(convertUnderThousand(rem3));
  return parts.join(" ");
}

/**
 * Formats a number to words in Indian numbering system (Crores, Lakhs, Thousands, Rupees and Paise).
 * Example: 79568513.75 -> "Rupees Seven Crore Ninety Five Lakh Sixty Eight Thousand Five Hundred Thirteen and Seventy Five Paise Only"
 */
export function numberToIndianWords(amount: MoneyInput): string {
  if (amount === null || amount === undefined) return "";
  let num: number;
  if (typeof amount === "object") {
    if (amount.display !== undefined) {
      num = Number(amount.display);
    } else if (amount.precise !== undefined) {
      num = Number(amount.precise);
    } else {
      return "";
    }
  } else if (typeof amount === "string") {
    num = Number(amount.replace(/[₹,\s]/g, ""));
  } else {
    num = amount;
  }
  if (!Number.isFinite(num)) return "";
  if (num === 0) return "Rupees Zero Only";
  const abs = Math.abs(num);
  const wholePart = Math.floor(abs);
  const paise = Math.round((abs - wholePart) * 100);

  let remaining = wholePart;
  const parts: string[] = [];

  const crores = Math.floor(remaining / 10000000);
  remaining %= 10000000;
  if (crores > 0) parts.push(`${numberToIndianWordsInteger(crores)} Crore`);

  const lakhs = Math.floor(remaining / 100000);
  remaining %= 100000;
  if (lakhs > 0) parts.push(`${convertUnderThousand(lakhs)} Lakh`);

  const thousands = Math.floor(remaining / 1000);
  remaining %= 1000;
  if (thousands > 0) parts.push(`${convertUnderThousand(thousands)} Thousand`);

  if (remaining > 0) parts.push(convertUnderThousand(remaining));

  let words = "Rupees " + (parts.length > 0 ? parts.join(" ") : "Zero");

  if (paise > 0) {
    words += ` and ${convertUnderThousand(paise)} Paise Only`;
  } else {
    words += " Only";
  }

  return words;
}

export type MoneyInput =
  | number
  | string
  | { precise?: string; display?: string }
  | null
  | undefined;

/**
 * Formats a numeric amount or backend MoneyValue to INR currency string with Indian comma grouping.
 * Accepts numbers, strings, or { precise, display } objects.
 * Uses display value for officer-facing money presentation.
 * Example: 79568513.75 -> "₹7,95,68,513.75"
 * Example: { display: "19843200.00" } -> "₹1,98,43,200"
 * Example: 0 -> "₹0"
 */
export function formatInr(val: MoneyInput): string {
  if (val === null || val === undefined) return "—";
  let num: number;
  if (typeof val === "object") {
    if (val.display !== undefined) {
      num = Number(val.display);
    } else if (val.precise !== undefined) {
      num = Number(val.precise);
    } else {
      return "—";
    }
  } else if (typeof val === "string") {
    num = Number(val.replace(/[₹,\s]/g, ""));
  } else {
    num = val;
  }

  if (!Number.isFinite(num)) return "—";

  const sign = num < 0 ? "-" : "";
  const abs = Math.abs(num);
  const parts = abs.toFixed(2).split(".");
  let intPart = parts[0];
  const decPart = parts[1];

  if (intPart.length > 3) {
    const last3 = intPart.slice(-3);
    const rest = intPart.slice(0, -3);
    const groupedRest = rest.replace(/\B(?=(\d{2})+(?!\d))/g, ",");
    intPart = `${groupedRest},${last3}`;
  }

  const decimalStr = Number(decPart) > 0 ? `.${decPart}` : "";
  return `${sign}₹${intPart}${decimalStr}`;
}

/**
 * Parses user input currency/amount string into a clean numeric value.
 * Strips '₹', commas, spaces. Preserves decimal point and minus sign.
 */
export function parseNumericInput(value: string | number | null | undefined): number {
  if (value === null || value === undefined) return 0;
  if (typeof value === "number") return Number.isFinite(value) ? value : 0;
  const clean = String(value || "")
    .replace(/[₹,\s]/g, "")
    .trim();
  const num = Number(clean);
  return Number.isFinite(num) ? num : 0;
}
