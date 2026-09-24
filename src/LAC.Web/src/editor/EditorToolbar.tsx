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
  showRulers: boolean;
  onToggleRulers: () => void;
  pageCount?: number;
};

function RulerIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M21.3 15.3a2.4 2.4 0 0 1 0 3.4l-2.6 2.6a2.4 2.4 0 0 1-3.4 0L2.7 8.7a2.4 2.4 0 0 1 0-3.4l2.6-2.6a2.4 2.4 0 0 1 3.4 0l12.6 12.6z" />
      <line x1="14.5" y1="12.5" x2="17" y2="10" />
      <line x1="11.5" y1="9.5" x2="14" y2="7" />
      <line x1="8.5" y1="6.5" x2="11" y2="4" />
    </svg>
  );
}

function TableMenuPopover({
  editor,
  onClose,
}: {
  editor: Editor;
  onClose: () => void;
}) {
  const [hoverRows, setHoverRows] = useState(3);
  const [hoverCols, setHoverCols] = useState(3);

  const insertWithPreset = (styleName: string) => {
    editor
      .chain()
      .focus()
      .insertTable({ rows: hoverRows, cols: hoverCols, withHeaderRow: true })
      .updateAttributes("table", { tableStyle: styleName })
      .run();
    onClose();
  };

  return (
    <div className="table-menu-popover" onMouseDown={e => e.stopPropagation()}>
      <div className="popover-section">
        <span className="popover-title">Insert Table ({hoverRows} × {hoverCols})</span>
        <div className="grid-picker">
          {Array.from({ length: 6 }).map((_, r) => (
            <div key={r} className="grid-picker-row">
              {Array.from({ length: 6 }).map((_, c) => (
                <button
                  key={c}
                  type="button"
                  aria-label={`Grid ${r + 1} rows by ${c + 1} columns`}
                  className={`grid-cell ${r < hoverRows && c < hoverCols ? "active" : ""}`}
                  onMouseEnter={() => {
                    setHoverRows(r + 1);
                    setHoverCols(c + 1);
                  }}
                  onClick={() => insertWithPreset("table-style-legal")}
                />
              ))}
            </div>
          ))}
        </div>
      </div>

      <div className="popover-divider" />

      <div className="popover-section">
        <span className="popover-title">Table Format Presets</span>
        <div className="table-preset-options">
          <button
            type="button"
            className="preset-btn preset-legal"
            onClick={() => insertWithPreset("table-style-legal")}
          >
            <span className="preset-icon">🏛️</span>
            <div>
              <strong>Legal / Revenue</strong>
              <small>Dark header, clean bottom borders</small>
            </div>
          </button>

          <button
            type="button"
            className="preset-btn preset-noting"
            onClick={() => insertWithPreset("table-style-noting")}
          >
            <span className="preset-icon">📗</span>
            <div>
              <strong>Government Noting</strong>
              <small>Emerald green theme & borders</small>
            </div>
          </button>

          <button
            type="button"
            className="preset-btn preset-executive"
            onClick={() => insertWithPreset("table-style-executive")}
          >
            <span className="preset-icon">💼</span>
            <div>
              <strong>Executive Dark</strong>
              <small>Navy header with striped rows</small>
            </div>
          </button>

          <button
            type="button"
            className="preset-btn preset-minimal"
            onClick={() => insertWithPreset("table-style-minimal")}
          >
            <span className="preset-icon">📊</span>
            <div>
              <strong>Minimal Grid</strong>
              <small>Subtle light gray grid</small>
            </div>
          </button>

          <button
            type="button"
            className="preset-btn preset-borderless"
            onClick={() => insertWithPreset("table-style-borderless")}
          >
            <span className="preset-icon">📄</span>
            <div>
              <strong>Borderless Layout</strong>
              <small>Side-by-side text columns</small>
            </div>
          </button>
        </div>
      </div>
    </div>
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
  showRulers,
  onToggleRulers,
  pageCount = 1,
}: Props) {
  const colorInputRef = useRef<HTMLInputElement>(null);
  const [tableMenuOpen, setTableMenuOpen] = useState(false);

  const command = (action: () => boolean) => (e: MouseEvent) => {
    e.preventDefault();
    action();
  };

  const handleColorChange = (e: ChangeEvent<HTMLInputElement>) => {
    editor.chain().focus().setColor(e.target.value).run();
  };

  const isTableActive = editor.isActive("table");

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

      {/* Group: View, Scale & Setup */}
      <div className="toolbar-section toolbar-view-group">
        <button
          type="button"
          className={`toolbar-btn toolbar-setup-btn ${showRulers ? "active" : ""}`}
          title="Show or hide page rulers / scales"
          onClick={onToggleRulers}
        >
          <RulerIcon />
          <span>Scale</span>
        </button>
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

      {/* Group: Table Tools & Format Presets */}
      <div className="toolbar-section toolbar-table-group" style={{ position: "relative" }}>
        <button
          type="button"
          className={`toolbar-btn toolbar-btn-labeled ${tableMenuOpen ? "active" : ""}`}
          title="Insert table & format presets"
          onClick={() => setTableMenuOpen(open => !open)}
        >
          <TableIcon />
          <span>+ Table</span>
        </button>

        {tableMenuOpen && (
          <TableMenuPopover editor={editor} onClose={() => setTableMenuOpen(false)} />
        )}

        {isTableActive && (
          <>
            <select
              aria-label="Table format preset"
              className="toolbar-select table-style-select"
              title="Change table style preset"
              onChange={e => editor.chain().focus().updateAttributes("table", { tableStyle: e.target.value }).run()}
              defaultValue="table-style-legal"
            >
              <option value="table-style-legal">🏛️ Legal Format</option>
              <option value="table-style-noting">📗 Revenue Noting</option>
              <option value="table-style-executive">💼 Executive Dark</option>
              <option value="table-style-minimal">📊 Minimal Grid</option>
              <option value="table-style-borderless">📄 Borderless</option>
            </select>

            <button
              type="button"
              className="toolbar-btn toolbar-btn-sub"
              title="Add row after"
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
              title="Add column after"
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
              className="toolbar-btn toolbar-btn-sub"
              title="Merge or split cells"
              onClick={command(() => editor.chain().focus().mergeOrSplit().run())}
            >
              Merge/Split
            </button>

            <button
              type="button"
              className="toolbar-btn toolbar-btn-danger"
              title="Delete entire table"
              onClick={command(() => editor.chain().focus().deleteTable().run())}
            >
              Delete
            </button>
          </>
        )}
      </div>
    </div>
  );
}

