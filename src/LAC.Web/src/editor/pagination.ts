import { Extension } from "@tiptap/core";
import { Plugin, PluginKey } from "@tiptap/pm/state";
import { Decoration, DecorationSet, EditorView } from "@tiptap/pm/view";
import { mmToPx, type PageProfile } from "./pageProfiles";

export const paginationPluginKey = new PluginKey<PaginationPluginState>("matterDraftPagination");

export interface PageBreak {
  pos: number;
  heightPx: number;
  pageIndex: number;
  isTableBreak?: boolean;
}

export interface PaginationPluginState {
  pageCount: number;
  breaks: PageBreak[];
  decorations: DecorationSet;
  signature: string;
}

export interface PaginationStorage {
  pageCount: number;
  breaks: PageBreak[];
}

export interface MatterDraftPaginationOptions {
  getProfile: () => PageProfile;
  getZoom: () => number;
  onPageCountChange?: (pageCount: number) => void;
}

const SHEET_GAP_PX = 32;

/**
 * Creates a page-break spacer placed BETWEEN top-level ProseMirror block nodes.
 *
 * KEY FIX: Uses a <div> (not a <span>), and is placed only at inter-block positions.
 * A display:block element inside a <p> is invalid HTML and was causing:
 *   - Failure A: text on grey surface (misaligned backdrop/editor)
 *   - Failure B: text inside page gap (spacer spanning boundary)
 *   - Failure C: huge empty space (wrong geometry accumulated mid-paragraph)
 */
function createPageBreakSpacer(heightPx: number, pageNumber: number): HTMLElement {
  const spacer = document.createElement("div");
  spacer.className = "draft-page-break-spacer";
  spacer.setAttribute("data-page-break", "true");
  spacer.setAttribute("data-next-page", String(pageNumber));
  spacer.style.height = `${Math.max(0, Math.round(heightPx * 10) / 10)}px`;
  spacer.style.width = "100%";
  spacer.style.display = "block";
  spacer.style.pointerEvents = "none";
  spacer.style.userSelect = "none";
  spacer.contentEditable = "false";
  return spacer;
}

function createTableRowSpacer(heightPx: number, pageNumber: number): HTMLElement {
  const tr = document.createElement("tr");
  tr.className = "draft-table-page-break-spacer";
  tr.setAttribute("data-page-break", "true");
  tr.setAttribute("data-next-page", String(pageNumber));
  const td = document.createElement("td");
  td.colSpan = 100;
  td.style.height = `${Math.max(0, Math.round(heightPx * 10) / 10)}px`;
  td.style.padding = "0";
  td.style.border = "0";
  td.style.background = "transparent";
  td.style.pointerEvents = "none";
  tr.appendChild(td);
  return tr;
}

/**
 * Measures a block's top and bottom coordinates in the spacer-hidden layout.
 * Returns null if measurement fails.
 */
function measureBlock(
  view: EditorView,
  nodeStart: number,
  nodeSize: number,
  viewTop: number,
  safeZoom: number
): { topY: number; bottomY: number } | null {
  const nodeEnd = nodeStart + nodeSize;
  try {
    // Try to get the actual top-level DOM element for exact border-box measurements
    try {
      const dom = view.nodeDOM(nodeStart);
      const rawEl = (dom instanceof HTMLElement ? dom : null) ??
        (view.domAtPos(nodeStart + 1).node instanceof HTMLElement
          ? (view.domAtPos(nodeStart + 1).node as HTMLElement)
          : view.domAtPos(nodeStart + 1).node.parentElement);
      const blockEl = rawEl?.closest(".draft-prosemirror > *") as HTMLElement | null;
      if (blockEl && !blockEl.classList.contains("draft-page-break-spacer")) {
        const rect = blockEl.getBoundingClientRect();
        return {
          topY: (rect.top - viewTop) / safeZoom,
          bottomY: (rect.bottom - viewTop) / safeZoom,
        };
      }
    } catch {
      // fallback to coordsAtPos
    }

    const safeStart = Math.min(nodeStart + 1, nodeEnd - 1);
    const safeEnd = Math.max(nodeEnd - 1, nodeStart + 1);
    const topCoords = view.coordsAtPos(safeStart);
    const bottomCoords = view.coordsAtPos(safeEnd);
    return {
      topY: (topCoords.top - viewTop) / safeZoom,
      bottomY: (bottomCoords.bottom - viewTop) / safeZoom,
    };
  } catch {
    return null;
  }
}

/**
 * Compute page breaks, inserting spacers ONLY between top-level doc blocks.
 *
 * Geometry invariants:
 * 1. Spacers are placed at block boundaries (between paragraphs/headings/lists/tables),
 *    never inside a paragraph. This eliminates display:block-in-<p> DOM anomalies.
 * 2. Spacer height = remainingPrintable + marginBottom + SHEET_GAP + marginTop
 *    This ensures the first line of the next page lands exactly at:
 *    spacer.bottom + marginTop (already encoded by the spacer height)
 *    = prev_page_top + pageHeight + SHEET_GAP + marginTop
 *    which matches the backdrop card geometry exactly.
 * 3. Coordinates are measured with spacers hidden → relative measurements are
 *    invariant to upstream spacer heights.
 * 4. pageTopY tracks the natural-layout Y of the start of the printable area on
 *    the current page. For page 1 this is coordsAtPos(1).top. For subsequent pages
 *    it is updated to the natural-layout top of the first block on that page.
 */
function computePageBreaks(
  view: EditorView,
  profile: PageProfile,
  zoom: number
): { pageCount: number; breaks: PageBreak[] } {
  const doc = view.state.doc;
  if (!doc.childCount) {
    return { pageCount: 1, breaks: [] };
  }

  const totalPageHeightPx = mmToPx(profile.heightMm);
  const marginTopPx = mmToPx(profile.marginTopMm + (profile.reservedTopMm ?? 0));
  const marginBottomPx = mmToPx(profile.marginBottomMm);
  const printableHeightPx = Math.max(120, totalPageHeightPx - marginTopPx - marginBottomPx);

  const domRect = view.dom.getBoundingClientRect();
  const viewTop = domRect.top;
  const safeZoom = Math.max(0.2, zoom || 1);

  // Hide existing spacers to measure pure natural content flow
  const existingSpacers = view.dom.querySelectorAll<HTMLElement>(
    ".draft-page-break-spacer, .draft-table-page-break-spacer"
  );
  existingSpacers.forEach(el => { el.style.display = "none"; });
  void view.dom.offsetHeight; // synchronous reflow

  const breaks: PageBreak[] = [];
  let pageIndex = 0;

  try {
    // Build array of (nodeStart, nodeSize) for all top-level blocks
    const blocks: Array<{ start: number; size: number }> = [];
    let pos = 0;
    for (let i = 0; i < doc.childCount; i++) {
      const n = doc.child(i);
      blocks.push({ start: pos, size: n.nodeSize });
      pos += n.nodeSize;
    }

    // pageTopY = natural-layout Y of the top of the printable area on the current page.
    let pageTopY: number;
    if (blocks.length > 0) {
      const firstMeasured = measureBlock(view, blocks[0].start, blocks[0].size, viewTop, safeZoom);
      pageTopY = firstMeasured ? firstMeasured.topY : marginTopPx;
    } else {
      pageTopY = marginTopPx;
    }

    // lastFitBottomY = natural-layout absolute Y of the bottom of the last block
    // that fits on the current page. Used to compute remaining space.
    let lastFitBottomY = pageTopY; // start of page = no content yet

    for (let i = 0; i < blocks.length; i++) {
      const { start: nodeStart, size: nodeSize } = blocks[i];
      const node = doc.child(i);

      const measured = measureBlock(view, nodeStart, nodeSize, viewTop, safeZoom);
      if (!measured) continue;

      const { topY: blockTopY, bottomY: blockBottomY } = measured;
      const blockHeight = blockBottomY - blockTopY;

      // Relative to current page's printable top
      const blockTopOnPage = blockTopY - pageTopY;
      const blockBottomOnPage = blockBottomY - pageTopY;

      if (blockBottomOnPage <= printableHeightPx) {
        // Block fits entirely on this page
        lastFitBottomY = blockBottomY;
        continue;
      }

      // Block does NOT fit on the current page.

      // Guard: if the block itself is taller than an entire printable page, we cannot
      // split it, so accept it across as many pages as it spans and advance pageIndex accordingly.
      if (blockHeight >= printableHeightPx - 4) {
        const pagesSpanned = Math.max(1, Math.ceil(blockBottomOnPage / printableHeightPx));
        pageIndex += pagesSpanned - 1;
        pageTopY += (pagesSpanned - 1) * printableHeightPx;
        lastFitBottomY = blockBottomY;
        continue;
      }

      // ── Choose spacer position ─────────────────────────────────────────────
      let spacerPos: number;
      let isTableBreak = false;
      let reEvaluateCurrentBlock = false; // whether to re-examine block i on next page

      if (node.type.name === "table") {
        const spaceRemaining = printableHeightPx - blockTopOnPage;
        if (spaceRemaining < mmToPx(35) && i > 0 && lastFitBottomY > pageTopY) {
          // Push entire table to next page
          spacerPos = nodeStart;
          reEvaluateCurrentBlock = true;
        } else {
          // Find the last table row that fits
          let lastFittingRowEnd = -1;
          let rowDocPos = nodeStart + 1;
          for (let r = 0; r < node.childCount; r++) {
            const rowNode = node.child(r);
            const rowEndPos = rowDocPos + rowNode.nodeSize;
            try {
              const rowBottomCoords = view.coordsAtPos(rowEndPos - 1);
              const rowBottomOnPage = (rowBottomCoords.bottom - viewTop) / safeZoom - pageTopY;
              if (rowBottomOnPage <= printableHeightPx) {
                lastFittingRowEnd = rowEndPos;
              } else {
                break;
              }
            } catch { /* skip */ }
            rowDocPos += rowNode.nodeSize;
          }

          if (lastFittingRowEnd > nodeStart) {
            spacerPos = lastFittingRowEnd;
            isTableBreak = true;
            // table continues on next page; do NOT re-evaluate (advance past table)
          } else if (i > 0 && lastFitBottomY > pageTopY) {
            // No rows fit but there's content before — push table to next page
            spacerPos = nodeStart;
            reEvaluateCurrentBlock = true;
          } else {
            // Table is first on page and no rows fit — accept entire table on this page
            lastFitBottomY = blockBottomY;
            continue;
          }
        }
      } else {
        // Paragraph, heading, list, etc.
        // Always break at a block boundary — never mid-paragraph.

        if (blockTopOnPage >= printableHeightPx - 4) {
          // This block starts at/past the printable bottom (orphaned).
          // There must be content before it (lastFitBottomY > pageTopY guaranteed by
          // the early-continue above if there's no content yet).
          spacerPos = nodeStart;
          reEvaluateCurrentBlock = true;
        } else if (lastFitBottomY > pageTopY) {
          // There's content before this block on this page.
          // Break before this block.
          spacerPos = nodeStart;
          reEvaluateCurrentBlock = true;
        } else {
          // This block is the first on this page and overflows (but blockHeight < printableHeight,
          // so it theoretically fits — this shouldn't happen if measurements are stable).
          // Accept it on this page.
          lastFitBottomY = blockBottomY;
          continue;
        }
      }

      // ── Compute spacer height ─────────────────────────────────────────────
      const contentUsedPx = Math.max(0, lastFitBottomY - pageTopY);
      const remainingPx = Math.max(0, printableHeightPx - contentUsedPx);
      const spacerHeightPx = remainingPx + marginBottomPx + SHEET_GAP_PX + marginTopPx;

      breaks.push({
        pos: spacerPos,
        heightPx: spacerHeightPx,
        pageIndex: pageIndex + 1,
        isTableBreak,
      });

      pageIndex++;

      // ── Update pageTopY for next page ────────────────────────────────────
      // The next page's first block (in natural hidden-spacer layout) tells us where
      // the new page starts. This makes all subsequent relative measurements correct.
      const nextIdx = reEvaluateCurrentBlock ? i : i + 1;
      if (nextIdx < blocks.length) {
        const { start: nextStart, size: nextSize } = blocks[nextIdx];
        const nextMeasured = measureBlock(view, nextStart, nextSize, viewTop, safeZoom);
        if (nextMeasured) {
          pageTopY = nextMeasured.topY;
        } else {
          pageTopY = lastFitBottomY + remainingPx + marginBottomPx + SHEET_GAP_PX;
        }
      }

      lastFitBottomY = pageTopY; // reset: no content consumed on new page yet

      if (reEvaluateCurrentBlock) {
        // Process block i again on the new page
        i--;
      }
    }
  } finally {
    // Restore existing spacers
    existingSpacers.forEach(el => {
      el.style.display = el.tagName === "TR" ? "" : "block";
    });
  }

  return {
    pageCount: Math.max(1, pageIndex + 1),
    breaks,
  };
}

export function matterDraftPagination(options: MatterDraftPaginationOptions) {
  let isMeasuring = false;
  let rafId = 0;
  let debounceTimer: ReturnType<typeof setTimeout> | null = null;

  return Extension.create<MatterDraftPaginationOptions, PaginationStorage>({
    name: "matterDraftPagination",

    addOptions() {
      return options;
    },

    addStorage() {
      return {
        pageCount: 1,
        breaks: [],
      };
    },

    addProseMirrorPlugins() {
      const extensionOptions = this.options;
      const storage = this.storage;

      return [
        new Plugin<PaginationPluginState>({
          key: paginationPluginKey,

          state: {
            init() {
              return {
                pageCount: 1,
                breaks: [],
                decorations: DecorationSet.empty,
                signature: "",
              };
            },

            apply(tr, prevState) {
              const meta = tr.getMeta(paginationPluginKey);
              if (meta) {
                return meta as PaginationPluginState;
              }
              if (tr.docChanged) {
                return {
                  ...prevState,
                  decorations: prevState.decorations.map(tr.mapping, tr.doc),
                };
              }
              return prevState;
            },
          },

          props: {
            decorations(state) {
              return paginationPluginKey.getState(state)?.decorations ?? DecorationSet.empty;
            },
          },

          view(view) {
            const runMeasurement = () => {
              if (rafId) {
                cancelAnimationFrame(rafId);
                rafId = 0;
              }
              if (isMeasuring || !view.dom.isConnected) return;
              isMeasuring = true;

              try {
                const profile = extensionOptions.getProfile();
                const zoom = extensionOptions.getZoom();
                const { pageCount, breaks } = computePageBreaks(view, profile, zoom);

                const signature = breaks
                  .map(b => `${b.pos}:${Math.round(b.heightPx * 10)}`)
                  .join("|");
                const currentState = paginationPluginKey.getState(view.state);

                if (
                  currentState?.signature !== signature ||
                  currentState?.pageCount !== pageCount
                ) {
                  const decorations = DecorationSet.create(
                    view.state.doc,
                    breaks.map(b =>
                      Decoration.widget(
                        b.pos,
                        b.isTableBreak
                          ? () => createTableRowSpacer(b.heightPx, b.pageIndex + 1)
                          : () => createPageBreakSpacer(b.heightPx, b.pageIndex + 1),
                        {
                          // side: -1 → widget appears BEFORE the node at `pos`
                          // This ensures the spacer is between blocks, not inside one.
                          side: -1,
                          key: `page-break-${b.pageIndex}-${b.pos}`,
                        }
                      )
                    )
                  );

                  const newState: PaginationPluginState = {
                    pageCount,
                    breaks,
                    decorations,
                    signature,
                  };

                  storage.pageCount = pageCount;
                  storage.breaks = breaks;
                  if (extensionOptions.onPageCountChange) {
                    extensionOptions.onPageCountChange(pageCount);
                  }

                  const tr = view.state.tr.setMeta(paginationPluginKey, newState);
                  view.dispatch(tr);
                }
              } finally {
                isMeasuring = false;
              }
            };

            const scheduleMeasurement = (delayMs = 25) => {
              if (debounceTimer) clearTimeout(debounceTimer);
              if (rafId) cancelAnimationFrame(rafId);

              debounceTimer = setTimeout(() => {
                rafId = requestAnimationFrame(() => {
                  requestAnimationFrame(() => {
                    runMeasurement();
                  });
                });
              }, delayMs);
            };

            // Observe ONLY container WIDTH changes (ignore height changes from spacers)
            let lastObservedWidth = view.dom.clientWidth;
            const resizeObserver = new ResizeObserver(entries => {
              for (const entry of entries) {
                const width = entry.contentRect.width;
                if (Math.abs(width - lastObservedWidth) > 3) {
                  lastObservedWidth = width;
                  scheduleMeasurement(50);
                }
              }
            });
            resizeObserver.observe(view.dom);

            // Initial layout calculation
            scheduleMeasurement(0);

            return {
              update(view, prevState) {
                const docChanged = !view.state.doc.eq(prevState.doc);
                const layoutChanged = !!view.state.tr.getMeta("layoutChanged");

                if (!docChanged && !layoutChanged) {
                  return;
                }

                // Prevent grey-surface flash on large paste:
                // Instantly expand the backdrop deck while precise calculation is pending.
                if (docChanged && extensionOptions.onPageCountChange) {
                  const profile = extensionOptions.getProfile();
                  const totalPageHeightPx = mmToPx(profile.heightMm);
                  const approxHeight = view.dom.scrollHeight || 0;
                  const approxPages = Math.max(
                    1,
                    Math.ceil(approxHeight / Math.max(100, totalPageHeightPx))
                  );
                  const currentPages = storage.pageCount || 1;
                  if (approxPages > currentPages) {
                    extensionOptions.onPageCountChange(approxPages);
                  }
                }

                scheduleMeasurement(docChanged ? 30 : 0);
              },

              destroy() {
                resizeObserver.disconnect();
                if (debounceTimer) clearTimeout(debounceTimer);
                if (rafId) cancelAnimationFrame(rafId);
              },
            };
          },
        }),
      ];
    },
  });
}

/**
 * Losslessly unwraps legacy { type: "draftPage", content: [...] } structures into flat
 * TipTap { type: "doc", content: [...] } AST without mutating or losing marks/attributes.
 */
export function normalizeToFlatContent(
  contentJson: string | Record<string, unknown>
): Record<string, unknown> {
  let docObj: Record<string, unknown>;
  if (typeof contentJson === "string") {
    try {
      docObj = JSON.parse(contentJson || '{"type":"doc","content":[]}');
    } catch {
      return { type: "doc", content: [{ type: "paragraph" }] };
    }
  } else {
    docObj = contentJson;
  }

  if (docObj?.type !== "doc" || !Array.isArray(docObj.content)) {
    return { type: "doc", content: [{ type: "paragraph" }] };
  }

  const hasDraftPage = docObj.content.some(
    (node: unknown) => (node as { type?: string })?.type === "draftPage"
  );
  if (!hasDraftPage) {
    return docObj.content.length > 0 ? docObj : { type: "doc", content: [{ type: "paragraph" }] };
  }

  const flattened: unknown[] = [];
  for (const item of docObj.content) {
    const node = item as { type?: string; content?: unknown[] };
    if (node?.type === "draftPage" && Array.isArray(node.content)) {
      flattened.push(...node.content);
    } else if (node) {
      flattened.push(node);
    }
  }

  return {
    ...docObj,
    content: flattened.length > 0 ? flattened : [{ type: "paragraph" }],
  };
}

export const normalizeDraftPages = normalizeToFlatContent;
