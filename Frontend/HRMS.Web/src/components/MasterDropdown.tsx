import { useEffect, useRef, useState } from 'react'
import { useApiQuery } from '../hooks/useApiQuery.ts'
import type { MasterLookup } from '../api/types.ts'
import type { SelectOption } from './fields.tsx'

// ---------------------------------------------------------------------------------------------
// MasterDropdown — fetches master data from the API and renders as a searchable dropdown
// ---------------------------------------------------------------------------------------------

export interface MasterDropdownProps {
  id: string
  label: string
  value: string
  /** Persisted code used as a non-blank display fallback while options are loading. */
  valueLabel?: string
  onChange: (value: string) => void
  onOptionSelected?: (item: MasterLookup) => void
  /** The API function to fetch master data (e.g., listDepartments, listSubDepartments). */
  fetcher: (query?: { parentId?: string; isActive?: boolean }, signal?: AbortSignal) => Promise<MasterLookup[]>
  /** For hierarchical masters: the parent entity's ID to filter children. Undefined = no filter. */
  parentId?: string
  /** Display format. Defaults to "{code} - {name}". */
  formatOption?: (item: MasterLookup) => string
  placeholder?: string
  hint?: string
  error?: string
  required?: boolean
  disabled?: boolean
  /** Include inactive saved values in the lookup while preventing new inactive selection. */
  includeInactive?: boolean
  allowInactiveSelection?: boolean
}

function defaultFormat(item: MasterLookup): string {
  return `${item.code} - ${item.name}`
}

export function MasterDropdown({
  id,
  label,
  value,
  valueLabel,
  onChange,
  onOptionSelected,
  fetcher,
  parentId,
  formatOption = defaultFormat,
  placeholder,
  hint,
  error,
  required,
  disabled,
  includeInactive = false,
  allowInactiveSelection = false,
}: MasterDropdownProps) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [highlightIndex, setHighlightIndex] = useState(-1)
  const inputRef = useRef<HTMLInputElement>(null)
  const listRef = useRef<HTMLDivElement>(null)

  const { data: items, isLoading } = useApiQuery(
    (signal) => fetcher({ parentId, isActive: includeInactive ? false : true }, signal),
    [fetcher, parentId, includeInactive],
  )

  const options: SelectOption[] = (items ?? []).map((item) => ({
    value: item.id,
    label: formatOption(item),
  }))

  const selectedLabel = options.find((o) => o.value === value)?.label ?? valueLabel ?? ''

  const filtered = query.trim() === ''
    ? options
    : options.filter((o) => o.label.toLowerCase().includes(query.toLowerCase()))

  const visible = filtered.slice(0, 100)

  function handleInputChange(e: React.ChangeEvent<HTMLInputElement>): void {
    setQuery(e.target.value)
    setOpen(true)
    setHighlightIndex(-1)
  }

  function handleKeyDown(e: React.KeyboardEvent): void {
    if (!open) {
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault()
        setOpen(true)
      }
      return
    }
    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault()
        setHighlightIndex((prev) => Math.min(prev + 1, visible.length - 1))
        break
      case 'ArrowUp':
        e.preventDefault()
        setHighlightIndex((prev) => Math.max(prev - 1, 0))
        break
      case 'Enter':
        e.preventDefault()
        if (highlightIndex >= 0 && highlightIndex < visible.length && visible[highlightIndex]) {
          selectOption(visible[highlightIndex])
        }
        break
      case 'Escape':
        setOpen(false)
        inputRef.current?.blur()
        break
    }
  }

  function selectOption(option: SelectOption): void {
    const item = items?.find((candidate) => candidate.id === option.value)
    if (item && !item.isActive && !allowInactiveSelection && option.value !== value) return
    onChange(option.value)
    if (item) onOptionSelected?.(item)
    setQuery('')
    setOpen(false)
    setHighlightIndex(-1)
  }

  function clearSelection(): void {
    onChange('')
    setQuery('')
    setOpen(false)
  }

  // Reset a child only after the parent changes during an interaction. The initial parent value is
  // often hydrated from an existing rule and must not erase the saved child selection.
  const previousParentId = useRef(parentId)
  useEffect(() => {
    if (previousParentId.current !== parentId && previousParentId.current !== undefined && value) {
      onChange('')
    }
    previousParentId.current = parentId
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [parentId])

  const displayValue = open ? query : selectedLabel
  const hintId = hint ? `${id}-hint` : undefined
  const errorId = error ? `${id}-error` : undefined
  const describedBy = [errorId, hintId].filter(Boolean).join(' ') || undefined

  return (
    <div className="field">
      <label htmlFor={id}>
        {label}
        {required && (
          <span className="field-required" aria-hidden="true">*</span>
        )}
      </label>
      <div className="supervisor-field">
        <input
          ref={inputRef}
          id={id}
          name={id}
          type="text"
          className={error ? 'input has-error' : 'input'}
          value={displayValue}
          placeholder={isLoading ? 'Loading...' : (placeholder ?? 'Search...')}
          onChange={handleInputChange}
          onFocus={() => setOpen(true)}
          onBlur={() => setTimeout(() => setOpen(false), 150)}
          onKeyDown={handleKeyDown}
          required={required}
          disabled={disabled}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy}
          aria-expanded={open}
          aria-autocomplete="list"
          autoComplete="off"
        />
        {selectedLabel && !open && (
          <button
            type="button"
            className="supervisor-field-clear"
            onClick={clearSelection}
            aria-label="Clear selection"
          >
            &times;
          </button>
        )}
        {open && visible.length > 0 && (
          <div ref={listRef} className="supervisor-field-list" role="listbox">
            {visible.map((option, index) => (
              <div
                key={option.value}
                role="option"
                aria-selected={option.value === value}
                className={`supervisor-field-option${index === highlightIndex ? ' highlighted' : ''}${option.value === value ? ' selected' : ''}`}
                onMouseDown={(e) => e.preventDefault()}
                onMouseEnter={() => setHighlightIndex(index)}
                onClick={() => selectOption(option)}
              >
                <span className="supervisor-field-option-name">{option.label}</span>
                {items?.find((item) => item.id === option.value)?.isActive === false ? <span className="supervisor-field-option-meta">Inactive</span> : null}
              </div>
            ))}
            {filtered.length > 100 && (
              <div className="supervisor-field-option">
                <span className="supervisor-field-option-meta">
                  Showing first 100 of {filtered.length} matches.
                </span>
              </div>
            )}
          </div>
        )}
      </div>
      {error !== undefined && (
        <p className="field-error" id={errorId}>
          {error}
        </p>
      )}
      {hint !== undefined && (
        <p className="field-hint" id={hintId}>
          {hint}
        </p>
      )}
    </div>
  )
}
