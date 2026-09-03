import { useState } from 'react'

type ExportMenuProps = {
  baseUrl: string
  query: string
}

const formats = [
  ['xlsx', 'Excel'],
  ['csv', 'CSV'],
  ['pdf', 'PDF'],
  ['docx', 'Word'],
] as const

export function ExportMenu({ baseUrl, query }: ExportMenuProps) {
  const [open, setOpen] = useState(false)

  return <div className="export-dropdown">
    <button className="secondary-button" onClick={() => setOpen(value => !value)} aria-expanded={open} aria-haspopup="menu">
      Export <span aria-hidden="true">⌄</span>
    </button>
    {open && <div className="export-options" role="menu">
      {formats.map(([format, label]) => <a key={format} role="menuitem" href={`${baseUrl}/${format}?q=${encodeURIComponent(query)}`} onClick={() => setOpen(false)}>{label}</a>)}
    </div>}
  </div>
}
