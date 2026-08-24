import { existsSync, readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { ALL_PERMISSIONS } from './permissions.ts'

/**
 * Keeps the TypeScript permission list honest against the C# one.
 *
 * `src/auth/permissions.ts` is a hand-copied mirror of a file in another language, which is exactly the
 * kind of duplication that rots quietly: adding `Employee.Approve` server-side would leave the UI unable
 * to render anything for it, and nothing would complain. So the source of truth is parsed here.
 *
 * Reading a backend file from a frontend test is unusual, and deliberate — the two halves live in one
 * repository and are released together, so the coupling already exists. This makes it visible.
 */

const PERMISSIONS_CS = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../../../Backend/HRMS.Domain/Authorization/Permissions.cs',
)

/** Every `public const string X = "…";` value, in declaration order. */
function permissionsDeclaredInCSharp(): string[] {
  const source = readFileSync(PERMISSIONS_CS, 'utf8')
  const matches = source.matchAll(/public const string \w+\s*=\s*"([^"]+)";/g)
  return [...matches].map((match) => match[1]).filter((value): value is string => Boolean(value))
}

describe('permission mirror', () => {
  it('can find the C# source of truth', () => {
    expect(
      existsSync(PERMISSIONS_CS),
      `Expected the backend permission constants at ${PERMISSIONS_CS}.`,
    ).toBe(true)
  })

  it('declares exactly the permissions the backend does', () => {
    const fromCSharp = permissionsDeclaredInCSharp()

    expect(fromCSharp.length).toBeGreaterThan(0)
    expect([...ALL_PERMISSIONS].sort()).toEqual([...fromCSharp].sort())
  })

  it('lists them in the same order, so the two files stay readable side by side', () => {
    expect([...ALL_PERMISSIONS]).toEqual(permissionsDeclaredInCSharp())
  })
})
