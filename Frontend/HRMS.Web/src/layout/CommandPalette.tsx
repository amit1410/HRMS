import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/useAuth.ts'
import { navigationSearchResults, type NavigationSearchResult } from './navigationSearch.ts'
import { visibleNavSearchModules } from './navigation.ts'

interface CommandPaletteProps {
  open: boolean
  onClose: () => void
  triggerRef: React.RefObject<HTMLButtonElement | null>
}

export function CommandPalette({ open, onClose, triggerRef }: CommandPaletteProps) {
  const { can, user } = useAuth()
  const navigate = useNavigate()
  const inputRef = useRef<HTMLInputElement>(null)
  const dialogRef = useRef<HTMLDivElement>(null)
  const [query, setQuery] = useState('')
  const [selectedId, setSelectedId] = useState<string | undefined>()
  const modules = useMemo(() => visibleNavSearchModules(can, user), [can, user])
  const results = useMemo(() => navigationSearchResults(modules, query), [modules, query])
  const effectiveSelectedId = results.some(result => result.id === selectedId) ? selectedId : results[0]?.id
  const selectedIndex = Math.max(0, results.findIndex(result => result.id === effectiveSelectedId))
  const selected = results[selectedIndex]

  const close = useCallback(() => {
    setQuery('')
    setSelectedId(undefined)
    onClose()
    window.requestAnimationFrame(() => triggerRef.current?.focus())
  }, [onClose, triggerRef])

  const openResult = useCallback((result: NavigationSearchResult) => {
    close()
    navigate(result.item.to)
  }, [close, navigate])

  useEffect(() => {
    if (!open) return
    const frame = window.requestAnimationFrame(() => inputRef.current?.focus())
    return () => window.cancelAnimationFrame(frame)
  }, [open])

  useEffect(() => {
    if (!open) return
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.preventDefault()
        close()
      } else if (event.key === 'ArrowDown') {
        event.preventDefault()
        if (results.length) setSelectedId(results[(selectedIndex + 1) % results.length]?.id)
      } else if (event.key === 'ArrowUp') {
        event.preventDefault()
        if (results.length) setSelectedId(results[(selectedIndex - 1 + results.length) % results.length]?.id)
      } else if (event.key === 'Enter' && selected) {
        event.preventDefault()
        openResult(selected)
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [close, open, openResult, results, selected, selectedIndex])

  useEffect(() => {
    if (!open) return
    function trapFocus(event: KeyboardEvent) {
      if (event.key !== 'Tab' || !dialogRef.current) return
      const focusable = Array.from(dialogRef.current.querySelectorAll<HTMLElement>('button, input, [tabindex]:not([tabindex="-1"])'))
      if (!focusable.length) return
      const first = focusable[0]!
      const last = focusable[focusable.length - 1]!
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
    }
    document.addEventListener('keydown', trapFocus)
    return () => document.removeEventListener('keydown', trapFocus)
  }, [open])

  if (!open) return null

  const grouped = modules.map(module => ({ module, items: results.filter(result => result.module.id === module.id) })).filter(group => group.items.length > 0)

  return <div className="command-palette-layer" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget) close() }}>
    <div ref={dialogRef} className="command-palette" role="dialog" aria-modal="true" aria-labelledby="command-palette-title">
      <div className="command-palette-header">
        <div><span className="command-palette-eyebrow">Navigate</span><h2 id="command-palette-title">Search HRMS</h2></div>
        <button type="button" className="command-palette-close" onClick={close} aria-label="Close search">×</button>
      </div>
      <label className="command-palette-input-wrap">
        <span className="command-palette-search-icon" aria-hidden="true">⌕</span>
        <span className="sr-only">Search pages and actions</span>
        <input ref={inputRef} className="command-palette-input" value={query} onChange={event => setQuery(event.target.value)} placeholder="Search pages, modules, or tasks…" autoComplete="off" role="combobox" aria-label="Search pages and actions" aria-expanded="true" aria-controls="command-palette-results" aria-activedescendant={selected ? `command-result-${selected.id.replace(/[^a-zA-Z0-9_-]/g, '-')}` : undefined} />
        <kbd>Esc</kbd>
      </label>
      <div id="command-palette-results" className="command-palette-results" role="listbox" aria-label="Navigation results">
        {grouped.length === 0 ? <div className="command-palette-empty" role="status"><strong>No matching pages</strong><span>Try a module, page name, or task such as “leave approval”.</span></div> : grouped.map(group => <section key={group.module.id} className="command-palette-group" aria-labelledby={`command-group-${group.module.id}`}>
          <h3 id={`command-group-${group.module.id}`}>{group.module.label}</h3>
          {group.items.map(result => { const isSelected = result.id === selected?.id; return <button key={result.id} id={`command-result-${result.id.replace(/[^a-zA-Z0-9_-]/g, '-')}`} type="button" role="option" aria-selected={isSelected} className={`command-palette-result${isSelected ? ' is-selected' : ''}`} onMouseEnter={() => setSelectedId(result.id)} onClick={() => openResult(result)}><span className="command-palette-result-icon" aria-hidden="true">↗</span><span><strong>{result.item.label}</strong>{result.aliases.length > 0 && <small>{result.aliases[0]}</small>}</span><span className="command-palette-result-enter" aria-hidden="true">↵</span></button> })}
        </section>)}
      </div>
      <div className="command-palette-footer"><span><kbd>↑</kbd><kbd>↓</kbd> to navigate</span><span><kbd>Enter</kbd> to open</span><span><kbd>Esc</kbd> to close</span></div>
    </div>
  </div>
}
