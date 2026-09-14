import type { Editor } from "@tiptap/react";

type Props = { editor: Editor; onPrint: () => void };

export function EditorToolbar({ editor, onPrint }: Props) {
  const command = (action: () => boolean) => () => action();
  return <div className="draft-toolbar" aria-label="Editor formatting tools">
    <select aria-label="Font family" onChange={e => editor.chain().focus().setMark("textStyle", { fontFamily: e.target.value }).run()} defaultValue="">
      <option value="">Font</option><option value="Arial, Helvetica, sans-serif">Arial</option><option value="Calibri, Arial, sans-serif">Calibri</option><option value="Georgia, serif">Georgia</option><option value="'Times New Roman', Times, serif">Times New Roman</option>
    </select>
    <select aria-label="Font size" onChange={e => editor.chain().focus().setMark("textStyle", { fontSize: e.target.value }).run()} defaultValue="">
      <option value="">Size</option>{[10, 11, 12, 14, 16, 18, 20].map(size => <option value={`${size}pt`} key={size}>{size} pt</option>)}
    </select>
    <button title="Bold" className={editor.isActive("bold") ? "active" : ""} onClick={command(() => editor.chain().focus().toggleBold().run())}><b>B</b></button>
    <button title="Italic" className={editor.isActive("italic") ? "active" : ""} onClick={command(() => editor.chain().focus().toggleItalic().run())}><i>I</i></button>
    <button title="Underline" className={editor.isActive("underline") ? "active" : ""} onClick={command(() => editor.chain().focus().toggleUnderline().run())}><u>U</u></button>
    <input aria-label="Text color" title="Text color" type="color" onChange={e => editor.chain().focus().setColor(e.target.value).run()} />
    <span className="toolbar-divider" />
    {(["left", "center", "right", "justify"] as const).map(align => <button key={align} title={`Align ${align}`} className={editor.isActive({ textAlign: align }) ? "active" : ""} onClick={command(() => editor.chain().focus().setTextAlign(align).run())}>{align[0].toUpperCase()}</button>)}
    <button title="Bullet list" className={editor.isActive("bulletList") ? "active" : ""} onClick={command(() => editor.chain().focus().toggleBulletList().run())}>• List</button>
    <button title="Numbered list" className={editor.isActive("orderedList") ? "active" : ""} onClick={command(() => editor.chain().focus().toggleOrderedList().run())}>1. List</button>
    <button title="Undo" disabled={!editor.can().undo()} onClick={command(() => editor.chain().focus().undo().run())}>Undo</button>
    <button title="Redo" disabled={!editor.can().redo()} onClick={command(() => editor.chain().focus().redo().run())}>Redo</button>
    <span className="toolbar-divider" />
    <button title="Insert table" onClick={command(() => editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run())}>+ Table</button>
    <button title="Add table row" onClick={command(() => editor.chain().focus().addRowAfter().run())}>+ Row</button>
    <button title="Delete table row" onClick={command(() => editor.chain().focus().deleteRow().run())}>− Row</button>
    <button title="Add table column" onClick={command(() => editor.chain().focus().addColumnAfter().run())}>+ Column</button>
    <button title="Delete table column" onClick={command(() => editor.chain().focus().deleteColumn().run())}>− Column</button>
    <button title="Delete table" onClick={command(() => editor.chain().focus().deleteTable().run())}>Delete table</button>
    <button className="quiet-button" onClick={onPrint}>Print</button>
  </div>;
}
