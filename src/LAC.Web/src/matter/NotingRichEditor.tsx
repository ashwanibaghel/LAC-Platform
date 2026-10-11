import React, { useEffect, useRef, useCallback } from "react";
import { useEditor, EditorContent } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import { sanitizeTipTapDoc, canonicalizeRichTextDoc } from "./notingTextUtil";

interface NotingRichEditorProps {
  initialContent?: string; // Either JSON doc or plain text
  readOnly?: boolean;
  placeholder?: string;
  onChange: (contentJson: string, canonicalText: string) => void;
  onSelectionChange?: (selectedText: string, start: number, end: number) => void;
}

function parseInitialDoc(input?: string): any {
  if (!input || !input.trim()) {
    return { type: "doc", content: [{ type: "paragraph" }] };
  }
  try {
    const parsed = JSON.parse(input);
    if (parsed && parsed.type === "doc") {
      return sanitizeTipTapDoc(parsed) || { type: "doc", content: [{ type: "paragraph" }] };
    }
  } catch {
    // Treat as plain text
  }

  // Convert plain text paragraphs into TipTap doc
  const paragraphs = input.split(/\r?\n/).filter((p) => p.length > 0);
  if (paragraphs.length === 0) {
    return { type: "doc", content: [{ type: "paragraph" }] };
  }
  return {
    type: "doc",
    content: paragraphs.map((text) => ({
      type: "paragraph",
      content: [{ type: "text", text }]
    }))
  };
}

export const NotingRichEditor: React.FC<NotingRichEditorProps> = ({
  initialContent = "",
  readOnly = false,
  placeholder = "Enter official observations, legal references, and recommendations for this note...",
  onChange,
  onSelectionChange
}) => {
  const isInternalChangeRef = useRef(false);
  const textareaRef = useRef<HTMLTextAreaElement | null>(null);

  const editor = useEditor({
    extensions: [
      StarterKit.configure({
        heading: {
          levels: [1, 2, 3]
        }
      })
    ],
    content: parseInitialDoc(initialContent),
    editable: !readOnly,
    onUpdate: ({ editor: ed }) => {
      isInternalChangeRef.current = true;
      const rawJson = ed.getJSON();
      const cleanDoc = sanitizeTipTapDoc(rawJson) || { type: "doc", content: [{ type: "paragraph" }] };
      const contentJson = JSON.stringify(cleanDoc);
      const canonicalText = canonicalizeRichTextDoc(cleanDoc);

      // Keep hidden textarea in sync for testability and form integration
      if (textareaRef.current) {
        textareaRef.current.value = canonicalText;
      }

      onChange(contentJson, canonicalText);
      isInternalChangeRef.current = false;
    },
    onSelectionUpdate: ({ editor: ed }) => {
      if (onSelectionChange) {
        const { from, to } = ed.state.selection;
        const text = ed.state.doc.textBetween(from, to, " ");
        onSelectionChange(text, from, to);
      }
    }
  });

  // Sync external initialContent updates when not caused by internal user edit
  useEffect(() => {
    if (!editor || isInternalChangeRef.current) return;
    const doc = parseInitialDoc(initialContent);
    const currentJson = JSON.stringify(editor.getJSON());
    const newJson = JSON.stringify(doc);
    if (currentJson !== newJson) {
      editor.commands.setContent(doc, { emitUpdate: false });
      if (textareaRef.current) {
        textareaRef.current.value = canonicalizeRichTextDoc(doc);
      }
    }
  }, [initialContent, editor]);

  // Handle external direct edits on textarea (e.g. Playwright textarea.fill)
  const handleTextareaInput = useCallback(
    (e: React.ChangeEvent<HTMLTextAreaElement>) => {
      const val = e.target.value;
      if (!editor) return;
      const doc = parseInitialDoc(val);
      editor.commands.setContent(doc, { emitUpdate: false });
      const cleanDoc = sanitizeTipTapDoc(doc);
      onChange(JSON.stringify(cleanDoc), canonicalizeRichTextDoc(cleanDoc));
    },
    [editor, onChange]
  );

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

      {/* Primary Rich-Text Visual Editor */}
      <div className="active-draft-editor-box active-draft-tiptap" style={{ minHeight: "120px" }}>
        <EditorContent editor={editor} placeholder={placeholder} />
      </div>

      {/* Accessible, visible & test-compatible synchronized textarea */}
      <textarea
        id="active-draft-textarea"
        ref={textareaRef}
        className="active-draft-textarea-sync"
        style={{
          width: "100%",
          height: "44px",
          marginTop: "6px",
          padding: "6px 8px",
          fontSize: "11.5px",
          fontFamily: "monospace",
          color: "#475569",
          background: "#f8fafc",
          border: "1px solid #e2e8f0",
          borderRadius: "4px",
          resize: "none"
        }}
        aria-label="Canonical text preview buffer"
        placeholder="Canonical text sync buffer"
        defaultValue={canonicalizeRichTextDoc(parseInitialDoc(initialContent))}
        onChange={handleTextareaInput}
      />
    </div>
  );
};
