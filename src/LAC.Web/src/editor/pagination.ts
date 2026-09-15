import { Extension, Node } from "@tiptap/core";
import { Node as ProseMirrorNode } from "@tiptap/pm/model";
import { Plugin } from "@tiptap/pm/state";
import { EditorView } from "@tiptap/pm/view";

export const DraftPage = Node.create({
  name: "draftPage",
  group: "block",
  content: "block+",
  isolating: true,
  defining: true,
  parseHTML: () => [{ tag: "section[data-draft-page]" }],
  renderHTML: () => ["section", { class: "draft-page", "data-draft-page": "true" }, 0],
});

function pageOverflowStart(view: EditorView, pagePosition: number, pageNode: ProseMirrorNode) {
  const pageElement = view.nodeDOM(pagePosition);
  if (!(pageElement instanceof HTMLElement) || pageElement.scrollHeight <= pageElement.clientHeight + 1) return null;
  const bottom = pageElement.clientHeight - Number.parseFloat(getComputedStyle(pageElement).paddingBottom || "0");
  let childPosition = pagePosition + 1;
  for (let index = 0; index < pageNode.childCount; index++) {
    const childElement = view.nodeDOM(childPosition);
    if (childElement instanceof HTMLElement && childElement.offsetTop + childElement.offsetHeight > bottom) return { index, position: childPosition };
    childPosition += pageNode.child(index).nodeSize;
  }
  return null;
}

export function matterDraftPagination() {
  return Extension.create({
    name: "matterDraftPagination",
    addProseMirrorPlugins() {
      return [new Plugin({
        view: view => {
          let frame = 0;
          const measure = () => {
            frame = 0;
            const document = view.state.doc;
            for (let pageIndex = 0, pagePosition = 0; pageIndex < document.childCount; pageIndex++) {
              const pageNode = document.child(pageIndex);
              const overflow = pageOverflowStart(view, pagePosition, pageNode);
              if (overflow && overflow.index > 0 && overflow.index < pageNode.childCount) {
                const moving = document.slice(overflow.position, pagePosition + pageNode.nodeSize - 1).content;
                const transaction = view.state.tr.delete(overflow.position, pagePosition + pageNode.nodeSize - 1);
                const nextPagePosition = pagePosition + pageNode.nodeSize - moving.size;
                if (pageIndex + 1 < document.childCount) transaction.insert(nextPagePosition + 1, moving);
                else transaction.insert(nextPagePosition, view.state.schema.nodes.draftPage.create(null, moving));
                view.dispatch(transaction);
                return;
              }
              pagePosition += pageNode.nodeSize;
            }
          };
          const schedule = () => { if (!frame) frame = window.requestAnimationFrame(measure); };
          const observer = new ResizeObserver(schedule); observer.observe(view.dom); schedule();
          return { update: schedule, destroy: () => { observer.disconnect(); if (frame) window.cancelAnimationFrame(frame); } };
        },
      })];
    },
  });
}

export function normalizeDraftPages(contentJson: string) {
  const content = JSON.parse(contentJson || '{"type":"doc","content":[]}');
  if (content.content?.every((node: { type?: string }) => node.type === "draftPage")) return content;
  return { type: "doc", content: [{ type: "draftPage", content: content.content?.length ? content.content : [{ type: "paragraph" }] }] };
}
