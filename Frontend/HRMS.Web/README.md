# HRMS.Web

React + TypeScript front end for the HRMS API. See the [repository README](../../README.md) for the
system as a whole, the API contract, and how to run the backend.

## Run it

```bash
npm install
npm run dev
```

The dev server binds port **5173** with `strictPort` on. That is not a preference: the API's CORS
policy allows exactly that origin, so a fallback port would make every request fail preflight.

The API base URL comes from `VITE_API_BASE_URL` (`.env.development`, copy `.env.example` for other
environments). It defaults to `http://localhost:5080`, which is what the API listens on locally — so
**start the API first**, then sign in with `DEMO01` / `hr@demo01.com` / `Passw0rd!`.

That seeded user is an **HR Manager**, which is worth knowing when a button is missing rather than broken:
they can view and edit employees but not delete them, and they can read departments and designations
without changing either. Sign in as `admin@demo01.com` (same password) for the TenantAdmin, who sees every
action.

For the design decisions behind this project — where the two tokens live and why, the single-flight
refresh, and why permission checks here are cosmetic — see
[Frontend (React + TypeScript)](../../README.md#frontend-react--typescript) in the repository README.

## Scripts

| Script | What it does |
| --- | --- |
| `npm run dev` | Vite dev server with HMR on <http://localhost:5173> |
| `npm run build` | Type-checks the project, then produces `dist/` |
| `npm run preview` | Serves the built output, to check the production bundle |
| `npm run typecheck` | `tsc -b` only — no bundle |
| `npm run lint` | oxlint |
| `npm run test` | Vitest in watch mode |
| `npm run test:run` | Vitest once (what CI would run) |
| `npm run test:coverage` | Vitest with a V8 coverage report |

## Layout

```
src/
  api/         Typed client: contract mirror, error normalization, session storage, endpoints
  auth/        Session provider, permission mirror, route guards
  components/  Presentational building blocks (card, table, fields, pagination, dialogs, states)
  hooks/       useApiQuery (abortable reads), useListQuery (URL-backed list state), useFlash,
               useDebouncedValue, useDocumentTitle
  layout/      Shell: sidebar, header, navigation model
  lib/         Formatting, local preferences, return paths
  pages/       Screens — dashboard/, employees/, lookups/ (departments and designations)
  styles/      Design tokens and application CSS
  test/        Vitest setup, fixtures and the axios adapter stub
```

## Screens

| Route | Screen | Guard |
| --- | --- | --- |
| `/login` | Sign in (organization code + email + password) | anonymous; redirects away if already signed in |
| `/` | Redirects to `/dashboard` | signed in |
| `/dashboard` | Headcount tiles, recent hires, headcount by department, CSV export | signed in; the export needs `Employee.Export` |
| `/employees` | Directory: paged, searchable, sortable, filtered by department, designation and status; CSV export; delete behind a confirmation | `Employee.View` |
| `/employees/new` | Hire someone | `Employee.Create` |
| `/employees/:id/edit` | Edit an employee | `Employee.Edit` |
| `/departments` | Departments: paged, searchable, sortable, filtered by status, with a live employee count | `Department.View` |
| `/departments/new`, `/departments/:id/edit` | Create / edit a department | `Department.Create`, `Department.Edit` |
| `/designations` | Job titles, same list contract, counting holders instead of employees | `Designation.View` |
| `/designations/new`, `/designations/:id/edit` | Create / edit a designation | `Designation.Create`, `Designation.Edit` |
| `/forbidden` | "You do not have access to that" — where `RequirePermission` sends a refusal, listing the roles the user does hold | signed in |
| anything else | "That page does not exist", inside the shell | signed in |

`RequireAuth` is a layout route rather than a wrapper repeated per screen, so a route added inside it is
protected by where it sits — forgetting the guard is not a mistake that can be made one route at a time.
The catch-all sits *inside* the shell too, so a mistyped URL keeps the navigation.

Each screen is then guarded for the *specific* permission it needs, not for the module: a create form
requires `Create`, so a URL typed by someone who can only read lands on `/forbidden` rather than on a form
whose submit was always going to be refused.

Departments and designations share one list component and one form component, configured by a module
object — they differ only in wording, endpoint and permissions. Each route element carries a `key`, which
is what makes the sharing safe: without it React would reuse the mounted component when moving between the
two routes, and its state, search box included, would carry across from one resource to the other.

A list's page, page size, search term, sort and filters all live in the query string. That makes a view
linkable and reload-proof, and it is what lets a form return to the exact list it was opened from — the
list hands its own path over in `location.state`, and `returnPath` refuses anything that does not address
that module's own list.
