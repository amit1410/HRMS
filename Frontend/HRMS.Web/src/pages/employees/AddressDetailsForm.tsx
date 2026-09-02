import { useEffect, useState, type FormEvent } from 'react'
import { getAddresses, getContact, upsertAddress, upsertContact } from '../../api/employeeSubsections.ts'
import { toApiError, type ApiError } from '../../api/errors.ts'
import type { AddressType, EmployeeAddress, EmployeeAddressRequest } from '../../api/types.ts'
import { Card } from '../../components/Card.tsx'
import { CheckboxField, SearchableSelect, TextField, type SelectOption } from '../../components/fields.tsx'
import { ErrorState } from '../../components/ErrorState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { loadCountryOptions, loadStateOptions, truncationHint } from './referenceOptions.ts'

interface AddressDetailsFormProps {
  /** The employee whose address details are being viewed/edited. */
  employeeId: string
}

/** The values of one address block (current or permanent), rendered as free text plus the country/state pickers. */
interface AddressPartValues {
  addressLine1: string
  addressLine2: string
  country: string
  state: string
  district: string
  city: string
  zipCode: string
}

function emptyPart(): AddressPartValues {
  return {
    addressLine1: '',
    addressLine2: '',
    country: '',
    state: '',
    district: '',
    city: '',
    zipCode: '',
  }
}

function toPartValues(address?: EmployeeAddress | null): AddressPartValues {
  return {
    addressLine1: address?.addressLine1 ?? '',
    addressLine2: address?.addressLine2 ?? '',
    country: address?.country ?? '',
    state: address?.state ?? '',
    district: address?.district ?? '',
    city: address?.city ?? '',
    zipCode: address?.zipCode ?? '',
  }
}

function toAddressRequest(addressType: AddressType, part: AddressPartValues): EmployeeAddressRequest {
  return {
    addressType,
    addressLine1: part.addressLine1 || null,
    addressLine2: part.addressLine2 || null,
    country: part.country || null,
    state: part.state || null,
    district: part.district || null,
    city: part.city || null,
    zipCode: part.zipCode || null,
    houseNumber: null,
  }
}

/**
 * The Employee → Address Details tab, shown on the employee detail page.
 *
 * It is self-managing: it loads the existing current and permanent addresses (and the contact record's
 * "same as current" flag) on mount, keeps every value in local form state, and has its own Save/Update
 * button. The "Permanent Address same as Current Address" checkbox sits between the current and permanent
 * blocks; while it is set the permanent block mirrors the current one live and is disabled, and the save
 * persists the flag (echoing the contact's other fields so they are not wiped) alongside both addresses.
 */
export function AddressDetailsForm({ employeeId }: AddressDetailsFormProps) {
  const [current, setCurrent] = useState<AddressPartValues>(emptyPart())
  const [permanent, setPermanent] = useState<AddressPartValues>(emptyPart())
  const [sameAsCurrent, setSameAsCurrent] = useState(false)
  const [hasSaved, setHasSaved] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const addresses = useApiQuery((signal) => getAddresses(employeeId, signal), [employeeId])
  const contact = useApiQuery((signal) => getContact(employeeId, signal), [employeeId])

  useEffect(() => {
    if (addresses.data) {
      const currentAddress = addresses.data.find((a) => a.addressType === 'Current') ?? null
      const permanentAddress = addresses.data.find((a) => a.addressType === 'Permanent') ?? null
      setCurrent(toPartValues(currentAddress))
      setPermanent(toPartValues(permanentAddress))
      setHasSaved(currentAddress != null || permanentAddress != null)
    }
  }, [addresses.data])

  useEffect(() => {
    if (contact.data) {
      setSameAsCurrent(contact.data.sameAsCurrentAddress)
    }
  }, [contact.data])

  const countries = useApiQuery((signal) => loadCountryOptions({ activeOnly: true }, signal), [])
  const currentStates = useApiQuery(
    (signal) =>
      current.country
        ? loadStateOptions(countryIdOf(countries.data?.options, current.country), { activeOnly: true }, signal)
        : Promise.resolve({ options: [], total: 0 }),
    [current.country, countries.data],
  )
  const permanentStates = useApiQuery(
    (signal) =>
      permanent.country
        ? loadStateOptions(countryIdOf(countries.data?.options, permanent.country), { activeOnly: true }, signal)
        : Promise.resolve({ options: [], total: 0 }),
    [permanent.country, countries.data],
  )

  function setPart(which: 'current' | 'permanent', next: AddressPartValues): void {
    if (which === 'current') setCurrent(next)
    else setPermanent(next)
  }

  async function submit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    if (saving) return

    // The permanent row mirrors the current one whenever the "same as current" flag is set.
    const permanentPayload = sameAsCurrent ? current : permanent

    setSaving(true)
    setError(null)
    setSuccess(null)
    try {
      // The "same as current" flag lives on the contact record, but the checkbox is edited here. Persist
      // it by echoing the current contact fields back (so none are wiped) with only the flag changed.
      await upsertContact(employeeId, {
        officialEmail: contact.data?.officialEmail ?? null,
        personalEmail: contact.data?.personalEmail ?? null,
        alternateEmail: contact.data?.alternateEmail ?? null,
        officialPhone: contact.data?.officialPhone ?? null,
        personalPhone: contact.data?.personalPhone ?? null,
        emergencyNumber: contact.data?.emergencyNumber ?? null,
        sameAsCurrentAddress: sameAsCurrent,
      })
      await upsertAddress(employeeId, toAddressRequest('Current', current))
      await upsertAddress(employeeId, toAddressRequest('Permanent', permanentPayload))
      setHasSaved(true)
      setSuccess('Address Details updated successfully.')
    } catch (caught) {
      setError(toApiError(caught))
    } finally {
      setSaving(false)
    }
  }

  const fieldError = (field: string) => error?.fieldErrors[field]

  if (!addresses.data && addresses.isLoading) {
    return (
      <Card className="form-card employee-detail-form-card">
        <div className="table-loading">
          <Spinner label="Loading address details…" />
        </div>
      </Card>
    )
  }

  if (addresses.error && !addresses.data) {
    return (
      <Card className="form-card employee-detail-form-card">
        <ErrorState error={addresses.error} onRetry={addresses.refetch} />
      </Card>
    )
  }

  const countryOptions: SelectOption[] = [{ value: '', label: '— None —' }, ...(countries.data?.options ?? [])]

  return (
    <Card className="form-card employee-detail-form-card">
      {error && <Notice tone="error">{error.message}</Notice>}
      {success && <Notice tone="success">{success}</Notice>}

      <form onSubmit={submit} noValidate>
        <AddressPartBlock
          legend="Current Address"
          part={current}
          setPart={(next) => setPart('current', next)}
          countryOptions={countryOptions}
          countriesLoading={countries.isLoading}
          countriesTotal={countries.data?.total ?? 0}
          states={[{ value: '', label: '— None —' }, ...(currentStates.data?.options ?? [])]}
          statesLoading={currentStates.isLoading}
          statesTotal={currentStates.data?.total ?? 0}
          fieldError={fieldError}
        />

        <div className="form-grid same-as-row">
          <CheckboxField
            id="permanentSameAsCurrent"
            label="Permanent Address same as Current Address"
            checked={sameAsCurrent}
            onChange={(checked) => {
              setSameAsCurrent(checked)
              if (checked) setPermanent(current)
            }}
          />
        </div>

        <AddressPartBlock
          legend="Permanent Address"
          part={sameAsCurrent ? current : permanent}
          setPart={(next) => setPart('permanent', next)}
          countryOptions={countryOptions}
          countriesLoading={countries.isLoading}
          countriesTotal={countries.data?.total ?? 0}
          states={[{ value: '', label: '— None —' }, ...(permanentStates.data?.options ?? [])]}
          statesLoading={permanentStates.isLoading}
          statesTotal={permanentStates.data?.total ?? 0}
          fieldError={fieldError}
          disabled={sameAsCurrent}
        />

        {sameAsCurrent && (
          <Notice tone="info">Permanent Address is kept the same as Current Address.</Notice>
        )}

        <div className="form-actions">
          <button type="submit" className="button button-primary" disabled={saving}>
            {saving ? <Spinner size={14} label="Saving…" /> : hasSaved ? 'UPDATE' : 'SAVE'}
          </button>
        </div>
      </form>
    </Card>
  )
}

function AddressPartBlock({
  legend,
  part,
  setPart,
  countryOptions,
  countriesLoading,
  countriesTotal,
  states,
  statesLoading,
  statesTotal,
  fieldError,
  disabled = false,
}: {
  legend: string
  part: AddressPartValues
  setPart: (next: AddressPartValues) => void
  countryOptions: SelectOption[]
  countriesLoading: boolean
  countriesTotal: number
  states: SelectOption[]
  statesLoading: boolean
  statesTotal: number
  fieldError: (field: string) => string | undefined
  disabled?: boolean
}) {
  const isCurrent = legend === 'Current Address'
  const prefix = isCurrent ? 'current' : 'permanent'
  const update = (field: keyof AddressPartValues, value: string) => setPart({ ...part, [field]: value })

  function onCountry(name: string): void {
    // Picking a different country invalidates the previously chosen state for this block.
    setPart({ ...part, country: name, state: '' })
  }

  return (
    <fieldset className="form-section">
      <legend>{legend}</legend>
      <div className="form-grid">
        <TextField
          id={`${prefix}-addressLine1`}
          label="Address Line 1"
          value={part.addressLine1}
          onChange={(value) => update('addressLine1', value)}
          maxLength={500}
          disabled={disabled}
          error={fieldError(`${prefix}Address.addressLine1`)}
        />
        <TextField
          id={`${prefix}-addressLine2`}
          label="Address Line 2"
          value={part.addressLine2}
          onChange={(value) => update('addressLine2', value)}
          maxLength={500}
          hint="Optional."
          disabled={disabled}
        />
      </div>
      <div className="form-grid">
        <SearchableSelect
          id={`${prefix}-country`}
          label="Country"
          value={part.country}
          onChange={onCountry}
          options={countryOptions}
          placeholder="Search countries…"
          loading={countriesLoading}
          disabled={disabled}
          hint={truncationHint(countryOptions.length - 1, countriesTotal, 'countries')}
        />
        <SearchableSelect
          id={`${prefix}-state`}
          label="State"
          value={part.state}
          onChange={(name) => update('state', name)}
          options={states}
          placeholder={part.country ? 'Search states…' : 'Select a country first'}
          loading={statesLoading}
          disabled={disabled || !part.country}
          hint={truncationHint(states.length - 1, statesTotal, 'states')}
        />
        <TextField
          id={`${prefix}-district`}
          label="District"
          value={part.district}
          onChange={(value) => update('district', value)}
          maxLength={100}
          hint="Optional."
          disabled={disabled}
        />
      </div>
      <div className="form-grid">
        <TextField
          id={`${prefix}-city`}
          label="City / Town"
          value={part.city}
          onChange={(value) => update('city', value)}
          maxLength={100}
          disabled={disabled}
        />
        <TextField
          id={`${prefix}-zipCode`}
          label="Postal Code / Pincode"
          value={part.zipCode}
          onChange={(value) => update('zipCode', value)}
          maxLength={20}
          autoComplete="postal-code"
          disabled={disabled}
        />
      </div>
    </fieldset>
  )
}

function countryIdOf(options: SelectOption[] | undefined, name: string): string {
  if (!name) return ''
  return options?.find((option) => option.label === name)?.value ?? ''
}
