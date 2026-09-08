import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { ok, installStubAdapter, type StubAdapter } from '../test/stubAdapter.ts'
import { renderAsUser } from '../test/renderWith.tsx'
import { ForgotPasswordPage } from './ForgotPasswordPage.tsx'

describe('ForgotPasswordPage', () => {
  let stub: StubAdapter

  beforeEach(() => {
    stub = installStubAdapter()
    stub.on('get', '/api/tenants/current/branding', () => ({ data: ok({ displayName: 'Demo HRMS', passwordRecoveryEnabled: true }) }))
  })

  afterEach(() => stub.restore())

  it('identifies an account, sends an OTP, and keeps recovery state in memory', async () => {
    stub.on('post', '/api/auth/forgot-password', () => ({ data: ok({ challengeId: 'challenge-1', availableChannels: [{ channel: 'Email', maskedDestination: '' }, { channel: 'Sms', maskedDestination: '' }], message: 'If eligible' }) }))
    stub.on('post', '/api/auth/forgot-password/send-otp', () => ({ data: ok({ challengeId: 'challenge-1', channel: 'Email', maskedDestination: 'e***t@example.test', developmentOtp: '123456', message: 'Verification code sent.' }) }))

    renderAsUser(<ForgotPasswordPage />)
    const identifier = await screen.findByLabelText('Email or Employee Code')
    expect(document.querySelector('.recovery-page')).toBeInTheDocument()
    expect(document.querySelector('.recovery-layout')).toBeInTheDocument()
    expect(document.querySelector('.recovery-brand-panel')).toBeInTheDocument()
    expect(document.querySelector('.recovery-form-panel')).toBeInTheDocument()
    fireEvent.change(identifier, { target: { value: 'employee@example.test' } })
    fireEvent.click(screen.getByRole('button', { name: /Send Verification Code/ }))

    expect(await screen.findByRole('heading', { name: 'Choose verification method' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('radio', { name: /Email/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Send OTP' }))

    expect(await screen.findByText('Development verification code: 123456')).toBeInTheDocument()
    expect(stub.callsTo('post', '/api/auth/forgot-password')[0]?.body).toEqual({ identifier: 'employee@example.test' })
    expect(stub.callsTo('post', '/api/auth/forgot-password/send-otp')[0]?.body).toEqual({ challengeId: 'challenge-1', channel: 'Email' })
    expect(window.localStorage.length).toBe(0)
    expect(window.sessionStorage.length).toBe(0)
  })

  it('selects Mobile and sends the unwrapped challenge id with the Sms channel', async () => {
    stub.on('post', '/api/auth/forgot-password', () => ({ data: ok({ challengeId: 'challenge-2', availableChannels: [{ channel: 'Email', maskedDestination: '' }, { channel: 'Sms', maskedDestination: '' }], message: 'If eligible' }) }))
    stub.on('post', '/api/auth/forgot-password/send-otp', () => ({ data: ok({ challengeId: 'challenge-2', channel: 'Sms', maskedDestination: '', message: 'Verification code sent.' }) }))

    renderAsUser(<ForgotPasswordPage />)
    fireEvent.change(await screen.findByLabelText('Email or Employee Code'), { target: { value: 'ANV_1001' } })
    fireEvent.click(screen.getByRole('button', { name: /Send Verification Code/ }))

    expect(await screen.findByRole('heading', { name: 'Choose verification method' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /Email/ })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('radio', { name: /Mobile/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Send OTP' }))

    await waitFor(() => expect(stub.callsTo('post', '/api/auth/forgot-password/send-otp')[0]?.body).toEqual({ challengeId: 'challenge-2', channel: 'Sms' }))
  })

  it('shows the generic safe message when no channels are returned', async () => {
    stub.on('post', '/api/auth/forgot-password', () => ({ data: ok({ challengeId: 'synthetic-challenge', availableChannels: [], message: 'If the account is eligible for password recovery, you can continue with the available verification method.' }) }))

    renderAsUser(<ForgotPasswordPage />)
    fireEvent.change(await screen.findByLabelText('Email or Employee Code'), { target: { value: 'unknown@example.test' } })
    fireEvent.click(screen.getByRole('button', { name: /Send Verification Code/ }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'If the account is eligible for password recovery, you can continue with the available verification method.',
    )
    expect(screen.queryByRole('button', { name: 'Send OTP by Email' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Send OTP by Mobile' })).not.toBeInTheDocument()
  })

  it('does not render the recovery form when the tenant disables recovery', async () => {
    stub.on('get', '/api/tenants/current/branding', () => ({ data: ok({ displayName: 'Demo HRMS', passwordRecoveryEnabled: false }) }))
    renderAsUser(<ForgotPasswordPage />)

    await waitFor(() => expect(screen.queryByLabelText('Email or Employee Code')).not.toBeInTheDocument())
  })
})
