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

function createPageBreakSpacer(heightPx: number, pageNumber: number): HTMLElement {
  const spacer = document.createElement("span");
  spacer.className = "draft-page-break-spacer";
  spacer.setAttribute("data-page-break", "true");
  spacer.setAttribute("data-next-page", String(pageNumber));
  spacer.style.display = "block";
  spacer.style.height = `${Math.max(0, Math.round(heightPx * 10) / 10)}px`;
  spacer.style.width = "100%";
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

  // Temporarily collapse existing spacers to measure pure natural content flow
  const existingSpacers = view.dom.querySelectorAll<HTMLElement>(".draft-page-break-spacer, .draft-table-page-break-spacer");
  existingSpacers.forEach(el => { el.style.display = "none"; });

  // Force synchronous reflow so subsequent coordsAtPos reflect unspaced geometry
  void view.dom.offsetHeight;

  const breaks: PageBreak[] = [];
  let pageIndex = 0;
  let currentRangeStart = 0;

  try {
    let pageTopY = 0;
    try {
      pageTopY = (view.coordsAtPos(1).top - viewTop) / safeZoom;
    } catch {
      pageTopY = 0;
    }

    let blockPos = 0;
    for (let i = 0; i < doc.childCount; i++) {
      const node = doc.child(i);
      const nodeSize = node.nodeSize;
      const nodeStart = blockPos;
      const nodeEnd = blockPos + nodeSize;

      // Skip blocks already completely placed on previous pages
      if (nodeEnd <= currentRangeStart) {
        blockPos += nodeSize;
        continue;
      }

      let blockTopY = 0;
      let blockBottomY = 0;
      try {
        const topCoords = view.coordsAtPos(nodeStart + 1);
        const bottomCoords = view.coordsAtPos(nodeEnd - 1);
        blockTopY = (topCoords.top - viewTop) / safeZoom;
        blockBottomY = (bottomCoords.bottom - viewTop) / safeZoom;
      } catch {
        blockPos += nodeSize;
        continue;
      }

      // Check if this block fits within the remaining printable budget of the current page
      const blockBottomOnPage = blockBottomY - pageTopY;

      if (blockBottomOnPage <= printableHeightPx) {
        blockPos += nodeSize;
        continue;
      }

      // Block crosses page boundary
      let splitPos = nodeStart;
      let isTableBreak = false;

      if (node.type.name === "heading") {
        // Orphan prevention: push heading if it's not at the very top of the page
        if (nodeStart > currentRangeStart) {
          splitPos = nodeStart;
        } else {
          blockPos += nodeSize;
          continue;
        }
      } else if (node.type.name === "table") {
        const tableTopOnPage = blockTopY - pageTopY;
        if (printableHeightPx - tableTopOnPage < mmToPx(35) && nodeStart > currentRangeStart) {
          splitPos = nodeStart;
        } else {
          let rowPos = nodeStart + 1;
          let foundRowBreak = false;
          for (let r = 0; r < node.childCount; r++) {
            const rowNode = node.child(r);
            try {
              const rowBottom = (view.coordsAtPos(rowPos + rowNode.nodeSize - 1).bottom - viewTop) / safeZoom;
              if (rowBottom - pageTopY > printableHeightPx && rowPos > nodeStart + 1) {
                splitPos = rowPos;
                isTableBreak = true;
                foundRowBreak = true;
                break;
              }
            } catch {
              // fallback
            }
            rowPos += rowNode.nodeSize;
          }
          if (!foundRowBreak) {
            splitPos = nodeStart > currentRangeStart ? nodeStart : nodeEnd;
          }
        }
      } else {
        // Paragraph or list item: test if at least 1 line fits
        let line1BottomY = blockTopY;
        try {
          const line1Coords = view.coordsAtPos(nodeStart + 1);
          line1BottomY = (line1Coords.bottom - viewTop) / safeZoom;
        } catch {
          line1BottomY = blockTopY;
        }

        if (line1BottomY - pageTopY > printableHeightPx - 4 && nodeStart > currentRangeStart) {
          // Not even 1 line fits on current page; push the whole block
          splitPos = nodeStart;
        } else {
          // Binary search for the last fitting word boundary on this page
          let low = nodeStart + 1;
          let high = nodeEnd - 1;
          let best = low;

          while (low <= high) {
            const mid = Math.floor((low + high) / 2);
            try {
              const coords = view.coordsAtPos(mid);
              const midBottomY = (coords.bottom - viewTop) / safeZoom;
              if (midBottomY - pageTopY <= printableHeightPx) {
                best = mid;
                low = mid + 1;
              } else {
                high = mid - 1;
              }
            } catch {
              high = mid - 1;
            }
          }

          let snapped = best;
          while (snapped > nodeStart + 1) {
            try {
              const char = doc.textBetween(snapped - 1, snapped);
              if (char === " " || char === "\n" || char === "\t") {
                break;
              }
            } catch {
              break;
            }
            snapped--;
          }

          if (snapped > nodeStart + 1) {
            splitPos = snapped;
          } else {
            splitPos = best;
          }
        }
      }

      if (splitPos <= currentRangeStart) {
        blockPos += nodeSize;
        continue;
      }

      let lastFitBottomY = blockTopY;
      try {
        const lastFitCoords = view.coordsAtPos(Math.max(1, splitPos - 1));
        lastFitBottomY = (lastFitCoords.bottom - viewTop) / safeZoom;
      } catch {
        lastFitBottomY = blockTopY;
      }

      const contentHeightOnThisPage = Math.max(0, lastFitBottomY - pageTopY);
      const remainingSpace = Math.max(0, printableHeightPx - contentHeightOnThisPage);
      const heightPx = remainingSpace + marginBottomPx + SHEET_GAP_PX + marginTopPx;

      breaks.push({
        pos: splitPos,
        heightPx,
        pageIndex: pageIndex + 1,
        isTableBreak,
      });

      // Advance to next page
      pageIndex++;
      currentRangeStart = splitPos;

      try {
        const nextLineCoords = view.coordsAtPos(splitPos);
        pageTopY = (nextLineCoords.top - viewTop) / safeZoom;
      } catch {
        pageTopY = lastFitBottomY;
      }

      // Re-evaluate remainder of this block on the new page if split was inside the block
      if (splitPos < nodeEnd && splitPos > nodeStart) {
        i--;
      } else {
        blockPos += nodeSize;
      }
    }
  } finally {
    // Restore existing spacers display
    existingSpacers.forEach(el => { el.style.display = el.tagName === "TR" ? "" : "block"; });
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

                const signature = breaks.map(b => `${b.pos}:${Math.round(b.heightPx * 10)}`).join("|");
                const currentState = paginationPluginKey.getState(view.state);

                if (currentState?.signature !== signature || currentState?.pageCount !== pageCount) {
                  const decorations = DecorationSet.create(
                    view.state.doc,
                    breaks.map(b =>
                      Decoration.widget(
                        b.pos,
                        b.isTableBreak
                          ? () => createTableRowSpacer(b.heightPx, b.pageIndex + 1)
                          : () => createPageBreakSpacer(b.heightPx, b.pageIndex + 1),
                        { side: 0, key: `page-break-${b.pageIndex}-${b.pos}` }
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

            // Observe ONLY meaningful container WIDTH changes (ignore height changes from spacers)
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
                // Ignore pagination's own metadata transactions (prevents loop!)
                const docChanged = !view.state.doc.eq(prevState.doc);
                const layoutChanged = !!view.state.tr.getMeta("layoutChanged");

                if (!docChanged && !layoutChanged) {
                  return;
                }

                // Prevent grey-surface content flash on large paste:
                // Instantly expand visual sheet capacity while precise calculation is pending
                if (docChanged && extensionOptions.onPageCountChange) {
                  const profile = extensionOptions.getProfile();
                  const totalPageHeightPx = mmToPx(profile.heightMm);
                  const approxHeight = view.dom.scrollHeight || 0;
                  const approxPages = Math.max(1, Math.ceil(approxHeight / Math.max(100, totalPageHeightPx)));
                  const currentPages = storage.pageCount || 1;
                  if (approxPages > currentPages) {
                    extensionOptions.onPageCountChange(approxPages);
                  }
                }

                // Schedule coalesced measurement pass
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
export function normalizeToFlatContent(contentJson: string | Record<string, unknown>): Record<string, unknown> {
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

  const hasDraftPage = docObj.content.some((node: unknown) => (node as { type?: string })?.type === "draftPage");
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
