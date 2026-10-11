import { useEffect, useRef, useImperativeHandle, forwardRef } from "react";
import { useEditor, EditorContent } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import {
  sanitizeTipTapDoc,
  canonicalizeRichTextDoc,
  parseDocFromInput,
  proseMirrorPosToCanonicalOffset
} from "./notingTextUtil";

export interface NotingRichEditorHandle {
  setContent: (content: any) => void;
  getEditor: () => any;
  setSelection: (from: number, to: number) => void;
}

interface NotingRichEditorProps {
  initialContent?: string; // Either JSON doc or plain text
  readOnly?: boolean;
  placeholder?: string;
  onChange: (contentJson: string, canonicalText: string) => void;
  onSelectionChange?: (selectedText: string, start: number, end: number) => void;
}

export const NotingRichEditor = forwardRef<NotingRichEditorHandle, NotingRichEditorProps>(({
  initialContent = "",
  readOnly = false,
  placeholder = "Enter official observations, legal references, and recommendations for this note...",
  onChange,
  onSelectionChange
}, ref) => {
  const isInternalChangeRef = useRef(false);

  const editor = useEditor({
    extensions: [
      StarterKit.configure({
        heading: {
          levels: [1, 2, 3]
        }
      })
    ],
    content: parseDocFromInput(initialContent),
    editable: !readOnly,
    onUpdate: ({ editor: ed }) => {
      isInternalChangeRef.current = true;
      const rawJson = ed.getJSON();
      const cleanDoc = sanitizeTipTapDoc(rawJson) || { type: "doc", content: [{ type: "paragraph" }] };
      const contentJson = JSON.stringify(cleanDoc);
      const canonicalText = canonicalizeRichTextDoc(cleanDoc);

      onChange(contentJson, canonicalText);
      isInternalChangeRef.current = false;
    },
    onSelectionUpdate: ({ editor: ed }) => {
      if (onSelectionChange) {
        const { from, to, empty } = ed.state.selection;
        if (empty) {
          onSelectionChange("", 0, 0);
        } else {
          const selectedText = ed.state.doc.textBetween(from, to, "\n");
          const startCanon = proseMirrorPosToCanonicalOffset(ed.state.doc, from);
          const endCanon = proseMirrorPosToCanonicalOffset(ed.state.doc, to);
          onSelectionChange(selectedText, startCanon, endCanon);
        }
      }
    }
  });

  useImperativeHandle(ref, () => ({
    setContent: (content: any) => {
      if (editor) {
        const doc = parseDocFromInput(content);
        editor.commands.setContent(doc, { emitUpdate: false });
        const cleanDoc = sanitizeTipTapDoc(doc) || { type: "doc", content: [{ type: "paragraph" }] };
        onChange(JSON.stringify(cleanDoc), canonicalizeRichTextDoc(cleanDoc));
      }
    },
    getEditor: () => editor,
    setSelection: (from: number, to: number) => {
      if (editor) {
        editor.commands.setTextSelection({ from, to });
      }
    }
  }), [editor, onChange]);

  // Sync external initialContent updates when not caused by internal user edit
  useEffect(() => {
    if (!editor || isInternalChangeRef.current) return;
    const doc = parseDocFromInput(initialContent);
    const currentJson = JSON.stringify(editor.getJSON());
    const newJson = JSON.stringify(doc);
    if (currentJson !== newJson) {
      editor.commands.setContent(doc, { emitUpdate: false });
    }
  }, [initialContent, editor]);

  if (!editor) {
    return (
      <div style={{ padding: "12px", color: "#64748b", fontSize: "12px" }}>
        Loading rich text noting editor…
      </div>
    );
  }

  return (
    <div className="noting-rich-editor-wrapper">
      {/* Functional Formatting Toolbar */}
      {!readOnly && (
        <div className="active-draft-toolbar" role="toolbar" aria-label="Formatting options">
          <span style={{ fontSize: "11px", color: "#64748b", marginRight: "4px", fontWeight: 600 }}>Format:</span>
          
          <button
            type="button"
            className={`active-draft-btn-tool ${editor.isActive("bold") ? "active" : ""}`}
            onClick={() => editor.chain().focus().toggleBold().run()}
            title="Bold (Ctrl+B)"
            aria-pressed={editor.isActive("bold")}
          >
            <strong>B</strong>
          </button>

          <button
            type="button"
            className={`active-draft-btn-tool ${editor.isActive("italic") ? "active" : ""}`}
            onClick={() => editor.chain().focus().toggleItalic().run()}
            title="Italic (Ctrl+I)"
            aria-pressed={editor.isActive("italic")}
          >
            <em>I</em>
          </button>

          <button
            type="button"
            className={`active-draft-btn-tool ${editor.isActive("underline") ? "active" : ""}`}
            onClick={() => editor.chain().focus().toggleUnderline().run()}
            title="Underline (Ctrl+U)"
            aria-pressed={editor.isActive("underline")}
          >
            <u>U</u>
          </button>

          <button
            type="button"
            className={`active-draft-btn-tool ${editor.isActive("strike") ? "active" : ""}`}
            onClick={() => editor.chain().focus().toggleStrike().run()}
            title="Strikethrough"
            aria-pressed={editor.isActive("strike")}
          >
            <s>S</s>
          </button>

          <span style={{ color: "#cbd5e1", margin: "0 2px" }}>|</span>

          <button
            type="button"
            className={`active-draft-btn-tool ${editor.isActive("heading", { level: 2 }) ? "active" : ""}`}
            onClick={() => editor.chain().focus().toggleHeading({ level: 2 }).run()}
            title="Heading 2"
          >
            H2
          </button>

          <button
            type="button"
            className={`active-draft-btn-tool ${editor.isActive("heading", { level: 3 }) ? "active" : ""}`}
            onClick={() => editor.chain().focus().toggleHeading({ level: 3 }).run()}
            title="Heading 3"
          >
            H3
          </button>

          <span style={{ color: "#cbd5e1", margin: "0 2px" }}>|</span>

          <button
            type="button"
            className={`active-draft-btn-tool ${editor.isActive("bulletList") ? "active" : ""}`}
            onClick={() => editor.chain().focus().toggleBulletList().run()}
            title="Bullet list"
            aria-pressed={editor.isActive("bulletList")}
          >
            • List
          </button>

          <button
            type="button"
            className={`active-draft-btn-tool ${editor.isActive("orderedList") ? "active" : ""}`}
            onClick={() => editor.chain().focus().toggleOrderedList().run()}
            title="Numbered list"
            aria-pressed={editor.isActive("orderedList")}
          >
            1. List
          </button>

          <button
            type="button"
            className={`active-draft-btn-tool ${editor.isActive("blockquote") ? "active" : ""}`}
            onClick={() => editor.chain().focus().toggleBlockquote().run()}
            title="Blockquote citation"
            aria-pressed={editor.isActive("blockquote")}
          >
            ❝ Quote
          </button>
        </div>
      )}

      {/* Primary Rich-Text Visual Editor Surface (Sole Editing Buffer) */}
      <div
        id="active-draft-editor-container"
        data-testid="active-draft-editor-container"
        className="active-draft-editor-box active-draft-tiptap"
        style={{ minHeight: "130px" }}
      >
        <EditorContent editor={editor} placeholder={placeholder} />
      </div>
    </div>
  );
});

NotingRichEditor.displayName = "NotingRichEditor";
