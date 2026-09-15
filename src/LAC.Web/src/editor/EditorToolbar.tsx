import { useRef } from "react";
import type { ChangeEvent, MouseEvent } from "react";
import type { Editor } from "@tiptap/react";

type Props = {
  editor: Editor;
  onPrint: () => void;
  zoom: number;
  onZoomChange: (delta: number) => void;
  onResetZoom: () => void;
  pageSetupExpanded: boolean;
  onTogglePageSetup: () => void;
  pageCount?: number;
};

// --- Modern Crisp SVG Icons ---
function UndoIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M3 7v6h6" />
      <path d="M21 17a9 9 0 0 0-9-9 9 9 0 0 0-6 2.3L3 13" />
    </svg>
  );
}

function RedoIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M21 7v6h-6" />
      <path d="M3 17a9 9 0 0 1 9-9 9 9 0 0 1 6 2.3l3 2.7" />
    </svg>
  );
}

function PrintIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="6 9 6 2 18 2 18 9" />
      <path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2" />
      <rect x="6" y="14" width="12" height="8" />
    </svg>
  );
}

function PageSetupIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
      <polyline points="14 2 14 8 20 8" />
      <line x1="16" y1="13" x2="8" y2="13" />
      <line x1="16" y1="17" x2="8" y2="17" />
      <polyline points="10 9 9 9 8 9" />
    </svg>
  );
}

function AlignLeftIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="17" y1="10" x2="3" y2="10" />
      <line x1="21" y1="6" x2="3" y2="6" />
      <line x1="21" y1="14" x2="3" y2="14" />
      <line x1="17" y1="18" x2="3" y2="18" />
    </svg>
  );
}

function AlignCenterIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="18" y1="10" x2="6" y2="10" />
      <line x1="21" y1="6" x2="3" y2="6" />
      <line x1="21" y1="14" x2="3" y2="14" />
      <line x1="18" y1="18" x2="6" y2="18" />
    </svg>
  );
}

function AlignRightIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="21" y1="10" x2="7" y2="10" />
      <line x1="21" y1="6" x2="3" y2="6" />
      <line x1="21" y1="14" x2="3" y2="14" />
      <line x1="21" y1="18" x2="7" y2="18" />
    </svg>
  );
}

function AlignJustifyIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="21" y1="10" x2="3" y2="10" />
      <line x1="21" y1="6" x2="3" y2="6" />
      <line x1="21" y1="14" x2="3" y2="14" />
      <line x1="21" y1="18" x2="3" y2="18" />
    </svg>
  );
}

function BulletListIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="9" y1="6" x2="20" y2="6" />
      <line x1="9" y1="12" x2="20" y2="12" />
      <line x1="9" y1="18" x2="20" y2="18" />
      <circle cx="4" cy="6" r="2" fill="currentColor" />
      <circle cx="4" cy="12" r="2" fill="currentColor" />
      <circle cx="4" cy="18" r="2" fill="currentColor" />
    </svg>
  );
}

function NumberedListIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="10" y1="6" x2="21" y2="6" />
      <line x1="10" y1="12" x2="21" y2="12" />
      <line x1="10" y1="18" x2="21" y2="18" />
      <path d="M4 6h1v4" />
      <path d="M4 10h2" />
      <path d="M6 18H4c0-1 2-2 2-3s-1-1.5-2-1" />
    </svg>
  );
}

function TableIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="3" width="18" height="18" rx="2" />
      <line x1="3" y1="9" x2="21" y2="9" />
      <line x1="3" y1="15" x2="21" y2="15" />
      <line x1="9" y1="3" x2="9" y2="21" />
      <line x1="15" y1="3" x2="15" y2="21" />
    </svg>
  );
}

export function EditorToolbar({
  editor,
  onPrint,
  zoom,
  onZoomChange,
  onResetZoom,
  pageSetupExpanded,
  onTogglePageSetup,
  pageCount = 1,
}: Props) {
  const colorInputRef = useRef<HTMLInputElement>(null);

  const command = (action: () => boolean) => (e: MouseEvent) => {
    e.preventDefault();
    action();
  };

  const handleColorChange = (e: ChangeEvent<HTMLInputElement>) => {
    editor.chain().focus().setColor(e.target.value).run();
  };

  return (
    <div
      className="draft-toolbar"
      aria-label="Editor formatting tools"
      onMouseDown={e => {
        const tag = (e.target as HTMLElement).tagName;
        if (tag !== "SELECT" && tag !== "INPUT") {
          e.preventDefault();
        }
      }}
    >
      {/* Group: History */}
      <div className="toolbar-section">
        <button
          type="button"
          className="toolbar-btn"
          title="Undo"
          aria-label="Undo"
          disabled={!editor.can().undo()}
          onClick={command(() => editor.chain().focus().undo().run())}
        >
          <UndoIcon />
        </button>
        <button
          type="button"
          className="toolbar-btn"
          title="Redo"
          aria-label="Redo"
          disabled={!editor.can().redo()}
          onClick={command(() => editor.chain().focus().redo().run())}
        >
          <RedoIcon />
        </button>
        <button
          type="button"
          className="toolbar-btn toolbar-print"
          title="Print"
          aria-label="Print"
          onClick={onPrint}
        >
          <PrintIcon />
        </button>
      </div>

      <div className="toolbar-divider" />

      {/* Group: View & Setup */}
      <div className="toolbar-section toolbar-view-group">
        <button
          type="button"
          className={`toolbar-btn toolbar-setup-btn ${pageSetupExpanded ? "active" : ""}`}
          title="Show or hide page setup"
          onClick={onTogglePageSetup}
        >
          <PageSetupIcon />
          <span>Page setup</span>
        </button>
        <div className="toolbar-zoom-group">
          <button
            type="button"
            className="toolbar-btn toolbar-zoom-btn"
            aria-label="Zoom out"
            title="Zoom out"
            onClick={() => onZoomChange(-0.1)}
          >
            −
          </button>
          <button
            type="button"
            className="toolbar-btn toolbar-zoom-val"
            aria-label="Reset zoom"
            title="Reset zoom"
            onClick={onResetZoom}
          >
            {Math.round(zoom * 100)}%
          </button>
          <button
            type="button"
            className="toolbar-btn toolbar-zoom-btn"
            aria-label="Zoom in"
            title="Zoom in"
            onClick={() => onZoomChange(0.1)}
          >
            +
          </button>
        </div>
        <span className="toolbar-page-counter" title="Total pages">
          {pageCount} {pageCount === 1 ? "page" : "pages"}
        </span>
      </div>

      <div className="toolbar-divider" />

      {/* Group: Typography */}
      <div className="toolbar-section toolbar-font-group">
        <select
          aria-label="Font family"
          className="toolbar-select font-family-select"
          onChange={e => editor.chain().focus().setMark("textStyle", { fontFamily: e.target.value }).run()}
          defaultValue=""
        >
          <option value="">Font</option>
          <option value="Arial, Helvetica, sans-serif">Arial</option>
          <option value="Calibri, Arial, sans-serif">Calibri</option>
          <option value="Georgia, serif">Georgia</option>
          <option value="'Times New Roman', Times, serif">Times New Roman</option>
        </select>

        <select
          aria-label="Font size"
          className="toolbar-select font-size-select"
          onChange={e => editor.chain().focus().setMark("textStyle", { fontSize: e.target.value }).run()}
          defaultValue=""
        >
          <option value="">Size</option>
          {[10, 11, 12, 14, 16, 18, 20].map(size => (
            <option value={`${size}pt`} key={size}>
              {size} pt
            </option>
          ))}
        </select>

        <button
          type="button"
          title="Bold"
          aria-label="Bold"
          className={`toolbar-btn text-format-btn ${editor.isActive("bold") ? "active" : ""}`}
          onClick={command(() => editor.chain().focus().toggleBold().run())}
        >
          <b>B</b>
        </button>

        <button
          type="button"
          title="Italic"
          aria-label="Italic"
          className={`toolbar-btn text-format-btn ${editor.isActive("italic") ? "active" : ""}`}
          onClick={command(() => editor.chain().focus().toggleItalic().run())}
        >
          <i>I</i>
        </button>

        <button
          type="button"
          title="Underline"
          aria-label="Underline"
          className={`toolbar-btn text-format-btn ${editor.isActive("underline") ? "active" : ""}`}
          onClick={command(() => editor.chain().focus().toggleUnderline().run())}
        >
          <u>U</u>
        </button>

        {/* Custom Color Button */}
        <button
          type="button"
          className="toolbar-btn toolbar-color-btn"
          title="Text color"
          aria-label="Text color"
          onClick={() => colorInputRef.current?.click()}
        >
          <span className="color-icon-a">A</span>
          <span className="color-icon-bar" />
          <input
            ref={colorInputRef}
            aria-label="Text color"
            type="color"
            className="hidden-color-picker"
            onChange={handleColorChange}
          />
        </button>
      </div>

      <div className="toolbar-divider" />

      {/* Group: Paragraph & Alignment */}
      <div className="toolbar-section toolbar-paragraph-group">
        <button
          type="button"
          title="Align left"
          aria-label="Align left"
          className={`toolbar-btn ${editor.isActive({ textAlign: "left" }) ? "active" : ""}`}
          onClick={command(() => editor.chain().focus().setTextAlign("left").run())}
        >
          <AlignLeftIcon />
        </button>
        <button
          type="button"
          title="Align center"
          aria-label="Align center"
          className={`toolbar-btn ${editor.isActive({ textAlign: "center" }) ? "active" : ""}`}
          onClick={command(() => editor.chain().focus().setTextAlign("center").run())}
        >
          <AlignCenterIcon />
        </button>
        <button
          type="button"
          title="Align right"
          aria-label="Align right"
          className={`toolbar-btn ${editor.isActive({ textAlign: "right" }) ? "active" : ""}`}
          onClick={command(() => editor.chain().focus().setTextAlign("right").run())}
        >
          <AlignRightIcon />
        </button>
        <button
          type="button"
          title="Align justify"
          aria-label="Align justify"
          className={`toolbar-btn ${editor.isActive({ textAlign: "justify" }) ? "active" : ""}`}
          onClick={command(() => editor.chain().focus().setTextAlign("justify").run())}
        >
          <AlignJustifyIcon />
        </button>

        <button
          type="button"
          title="Bullet list"
          aria-label="Bullet list"
          className={`toolbar-btn ${editor.isActive("bulletList") ? "active" : ""}`}
          onClick={command(() => editor.chain().focus().toggleBulletList().run())}
        >
          <BulletListIcon />
        </button>
        <button
          type="button"
          title="Numbered list"
          aria-label="Numbered list"
          className={`toolbar-btn ${editor.isActive("orderedList") ? "active" : ""}`}
          onClick={command(() => editor.chain().focus().toggleOrderedList().run())}
        >
          <NumberedListIcon />
        </button>
      </div>

      <div className="toolbar-divider" />

      {/* Group: Table Tools */}
      <div className="toolbar-section toolbar-table-group">
        <button
          type="button"
          className="toolbar-btn toolbar-btn-labeled"
          title="Insert table"
          onClick={command(() => editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run())}
        >
          <TableIcon />
          <span>+ Table</span>
        </button>

        <button
          type="button"
          className="toolbar-btn toolbar-btn-sub"
          title="Add table row"
          onClick={command(() => editor.chain().focus().addRowAfter().run())}
        >
          + Row
        </button>

        <button
          type="button"
          className="toolbar-btn toolbar-btn-sub"
          title="Delete table row"
          onClick={command(() => editor.chain().focus().deleteRow().run())}
        >
          − Row
        </button>

        <button
          type="button"
          className="toolbar-btn toolbar-btn-sub"
          title="Add table column"
          onClick={command(() => editor.chain().focus().addColumnAfter().run())}
        >
          + Col
        </button>

        <button
          type="button"
          className="toolbar-btn toolbar-btn-sub"
          title="Delete table column"
          onClick={command(() => editor.chain().focus().deleteColumn().run())}
        >
          − Col
        </button>

        <button
          type="button"
          className="toolbar-btn toolbar-btn-danger"
          title="Delete table"
          onClick={command(() => editor.chain().focus().deleteTable().run())}
        >
          Delete
        </button>
      </div>
    </div>
  );
}

