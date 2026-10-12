/**
 * Canonical Text Selection & Anchor Extraction Helper
 * Conforms to LAC Platform Noting Contract (NotingText.cs)
 */

export interface NoteAnchorDto {
  version: number;
  start: number;
  end: number;
  quote: string;
  quoteHash: string;
}

export interface CanonicalSelectionResult {
  success: boolean;
  anchor: NoteAnchorDto | null;
  errorMessage?: string;
}

export async function computeSha256Hex(text: string): Promise<string> {
  const enc = new TextEncoder().encode(text);
  const buf = await crypto.subtle.digest("SHA-256", enc);
  return Array.from(new Uint8Array(buf))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("")
    .toUpperCase();
}

export function splitsSurrogate(text: string, index: number): boolean {
  return (
    index > 0 &&
    index < text.length &&
    text.charCodeAt(index - 1) >= 0xd800 &&
    text.charCodeAt(index - 1) <= 0xdbff &&
    text.charCodeAt(index) >= 0xdc00 &&
    text.charCodeAt(index) <= 0xdfff
  );
}

/**
 * Derives an exact NoteAnchor from an officer's selected text in an official note's canonical text.
 */
export async function deriveExactAnchor(
  canonicalText: string,
  start: number,
  end: number,
  expectedQuote?: string
): Promise<CanonicalSelectionResult> {
  if (start < 0 || end <= start || end > canonicalText.length) {
    return {
      success: false,
      anchor: null,
      errorMessage: `Offsets [${start}..${end}] are out of range for note text length ${canonicalText.length}.`
    };
  }

  if (splitsSurrogate(canonicalText, start)) {
    return {
      success: false,
      anchor: null,
      errorMessage: `Offset start (${start}) splits a Unicode surrogate pair.`
    };
  }

  if (splitsSurrogate(canonicalText, end)) {
    return {
      success: false,
      anchor: null,
      errorMessage: `Offset end (${end}) splits a Unicode surrogate pair.`
    };
  }

  const quote = canonicalText.substring(start, end);
  if (expectedQuote !== undefined && quote !== expectedQuote) {
    return {
      success: false,
      anchor: null,
      errorMessage: `Extracted text "${quote}" does not match selected excerpt "${expectedQuote}".`
    };
  }

  if (!quote.trim()) {
    return {
      success: false,
      anchor: null,
      errorMessage: "Cannot attach remark to whitespace-only selection."
    };
  }

  const quoteHash = await computeSha256Hex(quote);

  return {
    success: true,
    anchor: {
      version: 1,
      start,
      end,
      quote,
      quoteHash
    }
  };
}

/**
 * Extracts selection from a DOM element containing canonical text
 */
export async function extractAnchorFromDomSelection(
  containerElement: HTMLElement,
  canonicalText: string
): Promise<CanonicalSelectionResult> {
  const selection = window.getSelection();
  if (!selection || selection.rangeCount === 0 || selection.isCollapsed) {
    return { success: false, anchor: null, errorMessage: "No text selected in note." };
  }

  const range = selection.getRangeAt(0);
  if (!containerElement.contains(range.commonAncestorContainer)) {
    return {
      success: false,
      anchor: null,
      errorMessage: "Selection must be entirely within this official note."
    };
  }

  const preRange = document.createRange();
  preRange.selectNodeContents(containerElement);
  preRange.setEnd(range.startContainer, range.startOffset);
  const start = preRange.toString().length;

  const selText = range.toString();
  const end = start + selText.length;

  return deriveExactAnchor(canonicalText, start, end, selText);
}
