import type { NavItem, NavModule } from './navigation.ts'

export interface NavigationSearchResult {
  id: string
  item: NavItem
  module: NavModule
  aliases: readonly string[]
}

const SEARCH_ALIASES: Readonly<Record<string, readonly string[]>> = {
  '/employees': ['people', 'staff', 'employee directory'],
  '/leave-management/approvals': ['leave approval', 'approve leave', 'manager approvals'],
  '/payroll/runs': ['payroll processing', 'salary run'],
  '/confirmation/review-cases': ['confirmation', 'probation review'],
  '/role-management': ['role management', 'user roles', 'access roles'],
}

export function navigationSearchResults(modules: readonly NavModule[], query: string): NavigationSearchResult[] {
  const normalizedQuery = normalize(query)
  const results = modules.flatMap(module => module.items
    .filter(item => item.available)
    .map(item => ({ id: `${module.id}:${item.to}`, item, module, aliases: SEARCH_ALIASES[item.to] ?? [] }))
    .filter(result => normalizedQuery === '' || matches(result, normalizedQuery)))

  return results.sort((left, right) => {
    if (normalizedQuery !== '') {
      const scoreDifference = score(right, normalizedQuery) - score(left, normalizedQuery)
      if (scoreDifference !== 0) return scoreDifference
    }
    return left.id.localeCompare(right.id)
  })
}

function matches(result: NavigationSearchResult, query: string) {
  const searchable = [result.item.label, result.module.label, result.item.to, ...result.aliases]
    .map(normalize)
    .join(' ')
  return query.split(' ').every(token => searchable.includes(token))
}

function score(result: NavigationSearchResult, query: string) {
  const label = normalize(result.item.label)
  const module = normalize(result.module.label)
  const aliases = result.aliases.map(normalize)
  if (label === query) return 400
  if (aliases.includes(query)) return 350
  if (label.startsWith(query)) return 300
  if (aliases.some(alias => alias.startsWith(query))) return 250
  if (module.startsWith(query)) return 200
  return 100
}

function normalize(value: string) {
  return value.trim().toLocaleLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').replace(/\s+/g, ' ')
}
