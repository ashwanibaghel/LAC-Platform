import { Extension } from "@tiptap/core";
import { Plugin, PluginKey } from "@tiptap/pm/state";
import { Decoration, DecorationSet } from "@tiptap/pm/view";

type PaginationSettings = { contentHeightPx: number; pageGapPx: number; onPageCount: (count: number) => void };
type Break = { position: number; height: number };
const paginationKey = new PluginKey("matterDraftPagination");

export function matterDraftPagination(getSettings: () => PaginationSettings) {
  return Extension.create({
    name: "matterDraftPagination",
    addProseMirrorPlugins() {
      return [new Plugin({
        key: paginationKey,
        state: {
          init: () => DecorationSet.empty,
          apply: (transaction, decorations) => transaction.getMeta(paginationKey) ?? decorations.map(transaction.mapping, transaction.doc),
        },
        props: { decorations: state => paginationKey.getState(state) },
        view: view => {
          let frame = 0; let lastSignature = "";
          const measure = () => {
            frame = 0;
            const settings = getSettings();
            if (!Number.isFinite(settings.contentHeightPx) || settings.contentHeightPx <= 0) return;
            const blocks: Array<{ position: number; height: number }> = [];
            view.state.doc.forEach((_, offset) => {
              const position = offset + 1;
              const dom = view.nodeDOM(position);
              if (dom instanceof HTMLElement) blocks.push({ position, height: Math.max(dom.offsetHeight, 1) });
            });
            let used = 0; let pageCount = 1; const breaks: Break[] = [];
            for (const block of blocks) {
              if (used > 0 && used + block.height > settings.contentHeightPx) {
                breaks.push({ position: block.position, height: Math.max(0, settings.contentHeightPx - used) + settings.pageGapPx });
                pageCount++; used = 0;
              }
              used += block.height;
              if (block.height > settings.contentHeightPx) { pageCount++; used = 0; }
            }
            const signature = breaks.map(item => `${item.position}:${Math.round(item.height)}`).join(",");
            const decorations = DecorationSet.create(view.state.doc, breaks.map(item => Decoration.widget(item.position, () => {
              const gap = document.createElement("div"); gap.className = "draft-page-break-gap"; gap.style.height = `${item.height}px`; return gap;
            }, { key: `draft-page-${item.position}-${Math.round(item.height)}`, side: -1 })));
            if (lastSignature !== signature) { lastSignature = signature; view.dispatch(view.state.tr.setMeta(paginationKey, decorations)); }
            settings.onPageCount(Math.max(1, pageCount));
          };
          const schedule = () => { if (!frame) frame = window.requestAnimationFrame(measure); };
          const observer = new ResizeObserver(schedule); observer.observe(view.dom); schedule();
          window.addEventListener("resize", schedule);
          return { update: schedule, destroy: () => { observer.disconnect(); window.removeEventListener("resize", schedule); if (frame) window.cancelAnimationFrame(frame); } };
        },
      })];
    },
  });
}
