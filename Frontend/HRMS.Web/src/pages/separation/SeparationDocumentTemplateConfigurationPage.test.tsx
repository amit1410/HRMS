import { fireEvent, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { renderAsUser } from '../../test/renderWith.tsx'
import { SeparationDocumentTemplateConfigurationPage } from './SeparationDocumentTemplateConfigurationPage.tsx'

const api = vi.hoisted(() => ({ templates: vi.fn(), create: vi.fn(), addVersion: vi.fn(), publish: vi.fn(), preview: vi.fn() }))
vi.mock('../../api/separationDocuments.ts', () => ({ getSeparationDocumentTemplates: api.templates, createSeparationDocumentTemplate: api.create, addSeparationDocumentTemplateVersion: api.addVersion, publishSeparationDocumentTemplateVersion: api.publish, previewSeparationDocumentTemplateVersion: api.preview }))

const draft = { id: 'v-1', versionNumber: 1, status: 'Draft', effectiveFrom: '2026-01-01', effectiveTo: null, bodyTemplate: '{{Employee.FullName}}', headerTemplate: null, footerTemplate: null, publishedAtUtc: null }
const template = { id: 't-1', code: 'RL', name: 'Relieving', documentType: 'RelievingLetter', description: null, isActive: true, effectiveFrom: '2026-01-01', effectiveTo: null, requiresApproval: true, concurrencyVersion: 1, versions: [draft] }

beforeEach(() => { vi.clearAllMocks(); api.templates.mockResolvedValue([]); api.create.mockResolvedValue(template); api.addVersion.mockResolvedValue(template); api.publish.mockResolvedValue({ ...template, concurrencyVersion: 2, versions: [{ ...draft, status: 'Published' }] }); api.preview.mockResolvedValue({ fileName: 'Preview.pdf', contentType: 'application/pdf', contentHash: 'hash', contentBase64: 'JVBERi0xLjQ=', isOfficial: false }); vi.spyOn(window, 'open').mockImplementation(() => null) })

describe('Phase 8G template configuration', () => {
  it('creates a draft, inserts a whitelisted field, previews as non-official, publishes, and shows read-only state', async () => {
    renderAsUser(<SeparationDocumentTemplateConfigurationPage />)
    fireEvent.change(screen.getByLabelText('Code'), { target: { value: 'RL' } }); fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Relieving' } }); fireEvent.click(screen.getByRole('button', { name: 'Create template' }))
    await waitFor(() => expect(api.create).toHaveBeenCalled())
    fireEvent.click(screen.getByRole('button', { name: 'Employee.FullName' })); expect((screen.getByLabelText('Template body') as HTMLTextAreaElement).value).toContain('{{Employee.FullName}}')
    fireEvent.click(screen.getByRole('button', { name: 'Save draft version' })); await waitFor(() => expect(api.addVersion).toHaveBeenCalled())
    fireEvent.click(screen.getByRole('button', { name: 'Preview' })); await waitFor(() => expect(api.preview).toHaveBeenCalledWith('v-1')); expect(await screen.findByRole('status')).toHaveTextContent(/NON-OFFICIAL PREVIEW/)
    fireEvent.click(screen.getByRole('button', { name: 'Publish' })); await waitFor(() => expect(api.publish).toHaveBeenCalledWith('t-1', 'v-1', 1))
    expect(await screen.findByText(/Published versions are read-only/)).toBeInTheDocument()
  })
})
