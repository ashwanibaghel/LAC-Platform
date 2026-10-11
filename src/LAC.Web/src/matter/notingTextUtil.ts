import type { NoteAnchor } from "./notingTypes";

/**
 * Computes uppercase SHA-256 hex string matching C#'s Convert.ToHexString(SHA256.HashData(...))
 */
export async function computeSha256Hex(text: string): Promise<string> {
  const buffer = new TextEncoder().encode(text);
  const digest = await window.crypto.subtle.digest("SHA-256", buffer);
  const bytes = new Uint8Array(digest);
  let hex = "";
  for (let i = 0; i < bytes.length; i++) {
    hex += bytes[i].toString(16).padStart(2, "0");
  }
  return hex.toUpperCase();
}

/**
 * Generates canonical text from rich text JSON structure exactly matching C# NotingText.Canonicalize
 */
export function canonicalizeRichTextDoc(docNode: any): string {
  if (!docNode || typeof docNode !== "object") return "";
  const parts: string[] = [];

  function visit(node: any) {
    if (!node || typeof node !== "object") return;
    const type = node.type;

    if (type === "text") {
      if (typeof node.text === "string") {
        parts.push(node.text);
      }
    } else if (type === "hardBreak") {
      parts.push("\n");
    } else {
      if (Array.isArray(node.content)) {
        for (const child of node.content) {
          visit(child);
        }
      }
      if (type === "paragraph" || type === "heading") {
        parts.push("\n");
      }
    }
  }

  if (docNode.type === "doc" && Array.isArray(docNode.content)) {
    for (const child of docNode.content) {
      visit(child);
    }
  } else {
    visit(docNode);
  }

  return parts.join("");
}

/**
 * Sanitizes a ProseMirror / TipTap doc JSON to ensure only backend-allowed nodes, marks, and attributes exist.
 * Backend allowlist:
 * Nodes: doc, paragraph, heading, bulletList, orderedList, listItem, blockquote, text, hardBreak
 * Marks: bold, italic, underline, strike
 * Attributes: only heading may have { level: 1..6 }
 */
export function sanitizeTipTapDoc(node: any): any {
  if (!node || typeof node !== "object") return null;

  const type = node.type;
  if (!type) return null;

  const allowedNodes = new Set([
    "doc",
    "paragraph",
    "heading",
    "bulletList",
    "orderedList",
    "listItem",
    "blockquote",
    "text",
    "hardBreak"
  ]);

  if (!allowedNodes.has(type)) {
    // If unsupported container, convert to paragraph or strip
    return null;
  }

  const cleanNode: Record<string, any> = { type };

  if (type === "heading" && node.attrs && typeof node.attrs.level === "number") {
    cleanNode.attrs = { level: Math.max(1, Math.min(6, node.attrs.level)) };
  }

  if (type === "text") {
    cleanNode.text = typeof node.text === "string" ? node.text : "";
    if (Array.isArray(node.marks)) {
      const allowedMarks = new Set(["bold", "italic", "underline", "strike"]);
      const cleanMarks = node.marks
        .filter((m: any) => m && typeof m.type === "string" && allowedMarks.has(m.type))
        .map((m: any) => ({ type: m.type }));
      if (cleanMarks.length > 0) {
        cleanNode.marks = cleanMarks;
      }
    }
    return cleanNode;
  }

  if (Array.isArray(node.content)) {
    const cleanContent: any[] = [];
    for (const child of node.content) {
      const cleaned = sanitizeTipTapDoc(child);
      if (cleaned) {
        cleanContent.push(cleaned);
      }
    }
    if (cleanContent.length > 0 || type === "doc") {
      cleanNode.content = cleanContent.length > 0 ? cleanContent : [{ type: "paragraph" }];
    }
  } else if (type === "doc") {
    cleanNode.content = [{ type: "paragraph" }];
  }

  return cleanNode;
}

/**
 * Creates an authoritative NoteAnchor matching C# NoteAnchor validation.
 */
export async function createNoteAnchor(
  canonicalText: string,
  start: number,
  end: number
): Promise<NoteAnchor> {
  const cleanStart = Math.max(0, Math.min(start, end));
  const cleanEnd = Math.min(canonicalText.length, Math.max(start, end));
  const quote = canonicalText.substring(cleanStart, cleanEnd);
  const quoteHash = await computeSha256Hex(quote);

  return {
    version: 1,
    start: cleanStart,
    end: cleanEnd,
    quote,
    quoteHash
  };
}

/**
 * Maps a ProseMirror position (within ed.state.doc) to the exact UTF-16 code unit offset
 * in the server canonical text (NotingText.Canonicalize).
 */
export function proseMirrorPosToCanonicalOffset(docNode: any, targetPmPos: number): number {
  if (!docNode || targetPmPos <= 0) return 0;

  let currentPmPos = 0;
  let currentCanonOffset = 0;
  let foundOffset = -1;

  function walk(node: any) {
    if (foundOffset !== -1) return;

    if (node.isText) {
      const textLen = node.text ? node.text.length : 0;
      if (targetPmPos >= currentPmPos && targetPmPos <= currentPmPos + textLen) {
        foundOffset = currentCanonOffset + (targetPmPos - currentPmPos);
        return;
      }
      currentPmPos += textLen;
      currentCanonOffset += textLen;
      return;
    }

    const typeName = node.type ? (typeof node.type === "string" ? node.type : node.type.name) : "";

    if (typeName === "hardBreak") {
      if (targetPmPos === currentPmPos) {
        foundOffset = currentCanonOffset;
        return;
      }
      currentPmPos += 1;
      currentCanonOffset += 1; // '\n' in canonical text
      return;
    }

    const isRoot = typeName === "doc";
    if (!isRoot) {
      if (targetPmPos === currentPmPos) {
        foundOffset = currentCanonOffset;
        return;
      }
      currentPmPos += 1; // opening tag
    }

    if (node.content) {
      if (Array.isArray(node.content)) {
        for (const child of node.content) {
          walk(child);
          if (foundOffset !== -1) return;
        }
      } else if (typeof node.content.forEach === "function") {
        node.content.forEach((child: any) => {
          walk(child);
        });
        if (foundOffset !== -1) return;
      }
    }

    if (!isRoot) {
      if (targetPmPos === currentPmPos) {
        foundOffset = currentCanonOffset;
        return;
      }
      currentPmPos += 1; // closing tag
      if (typeName === "paragraph" || typeName === "heading") {
        currentCanonOffset += 1; // '\n' in canonical text
      }
    }
  }

  walk(docNode);
  return foundOffset !== -1 ? foundOffset : currentCanonOffset;
}

/**
 * Parses either a JSON string, a JSON object, or plain text into a sanitized TipTap doc structure.
 */
export function parseDocFromInput(input?: string | any): any {
  if (!input) {
    return { type: "doc", content: [{ type: "paragraph" }] };
  }
  if (typeof input === "object") {
    return sanitizeTipTapDoc(input) || { type: "doc", content: [{ type: "paragraph" }] };
  }
  const trimmed = typeof input === "string" ? input.trim() : "";
  if (!trimmed) {
    return { type: "doc", content: [{ type: "paragraph" }] };
  }
  try {
    const parsed = JSON.parse(trimmed);
    if (parsed && parsed.type === "doc") {
      const clean = sanitizeTipTapDoc(parsed);
      if (clean) return clean;
    }
  } catch {
    // Treat as plain text
  }

  const lines = trimmed.split(/\r?\n/).filter((l) => l.length > 0);
  if (lines.length === 0) {
    return { type: "doc", content: [{ type: "paragraph" }] };
  }
  return {
    type: "doc",
    content: lines.map((text) => ({
      type: "paragraph",
      content: [{ type: "text", text }]
    }))
  };
}

/**
 * Merges two TipTap documents into a single valid TipTap doc with server-compatible paragraphs.
 */
export function mergeTipTapDocs(serverInput: any, localInput: any): any {
  const serverDoc = parseDocFromInput(serverInput);
  const localDoc = parseDocFromInput(localInput);

  const serverContent = Array.isArray(serverDoc.content) ? serverDoc.content : [];
  const localContent = Array.isArray(localDoc.content) ? localDoc.content : [];

  const merged = {
    type: "doc",
    content: [...serverContent, ...localContent]
  };

  return sanitizeTipTapDoc(merged) || { type: "doc", content: [{ type: "paragraph" }] };
}
