import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { RequirePlatformAuth } from './RequirePlatformAuth.tsx'

const { usePlatformAuth } = vi.hoisted(() => ({ usePlatformAuth: vi.fn() }))
vi.mock('./PlatformAuthProvider.tsx', () => ({ usePlatformAuth }))

function renderGuard(status: 'restoring' | 'authenticated' | 'anonymous') {
  usePlatformAuth.mockReturnValue({ status })
  return render(<MemoryRouter initialEntries={['/platform/tenants']}><Routes><Route element={<RequirePlatformAuth />}><Route path="/platform/tenants" element={<p>tenant page</p>} /></Route><Route path="/platform/login" element={<p>platform login</p>} /></Routes></MemoryRouter>)
}

describe('RequirePlatformAuth', () => {
  it('waits for restoration before rendering or redirecting', () => {
    renderGuard('restoring')
    expect(screen.getByRole('status')).toHaveTextContent('Restoring platform session')
    expect(screen.queryByText('platform login')).not.toBeInTheDocument()
  })

  it('renders protected content after restoration', () => {
    renderGuard('authenticated')
    expect(screen.getByText('tenant page')).toBeInTheDocument()
  })

  it('redirects an anonymous platform session to login', () => {
    renderGuard('anonymous')
    expect(screen.getByText('platform login')).toBeInTheDocument()
  })
})
