import { StatTile } from '../../components/StatTile.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import type { PagedResult } from '../../api/types.ts'

/**
 * A count taken from a list endpoint.
 *
 * `pageSize: 1` is deliberate: what is wanted is `totalCount`, which every `PagedResult` carries next to
 * its items, and asking for one row is the cheapest way to get it. There is no separate count endpoint,
 * and adding one to serve four tiles would not earn its keep.
 *
 * The count is also already tenant-scoped — the same global query filter that limits the list limits the
 * total — so no tenant id is passed, and none could be.
 */
export function CountTile({
  label,
  hint,
  load,
  icon,
}: {
  label: string
  hint?: string
  load: (signal: AbortSignal) => Promise<PagedResult<unknown>>
  icon?: string
}) {
  const { data, error, isLoading } = useApiQuery(load, [])

  return (
    <StatTile
      label={label}
      hint={hint}
      value={data?.totalCount ?? null}
      isLoading={isLoading}
      error={error}
      icon={icon}
    />
  )
}
