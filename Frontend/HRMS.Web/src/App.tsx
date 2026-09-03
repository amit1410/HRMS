import { type ReactNode } from 'react'
import { Navigate, Route, Routes, useLocation, useParams } from 'react-router-dom'
import { AuthProvider } from './auth/AuthProvider.tsx'
import { Permissions } from './auth/permissions.ts'
import { RequireAuth } from './auth/RequireAuth.tsx'
import { RequirePermission } from './auth/RequirePermission.tsx'
import { ErrorBoundary } from './components/ErrorBoundary.tsx'
import { AppLayout } from './layout/AppLayout.tsx'
import { DashboardPage } from './pages/DashboardPage.tsx'
import { EmployeeDetailPage } from './pages/employees/EmployeeDetailPage.tsx'
import { EmployeeFormPage } from './pages/employees/EmployeeFormPage.tsx'
import { EmployeesPage } from './pages/employees/EmployeesPage.tsx'
import { ForbiddenPage } from './pages/ForbiddenPage.tsx'
import { LoginPage } from './pages/LoginPage.tsx'
import { WorkspacePickerPage } from './pages/WorkspacePickerPage.tsx'
import { NotFoundPage } from './pages/NotFoundPage.tsx'
import { EmployeeCodeConfigurationPage } from './pages/EmployeeCodeConfigurationPage.tsx'
import { isApexHost } from './lib/isApexHost.ts'
import { MasterManagementPage } from './pages/masters/MasterManagementPage.tsx'

/**
 * Resets the ErrorBoundary on every route change. Without this, a render-time crash on
 * /employees would leave the "Something went wrong" fallback stuck on screen even after
 * the user navigates to /dashboard — because ErrorBoundary only clears its state when it
 * receives new *props*, and a route change inside <Routes> does not re-render <ErrorBoundary>
 * itself.
 *
 * This component exists only to hold `useLocation`, which requires being inside a Router
 * context. It cannot live inside <Routes>, so we key the ErrorBoundary from here instead.
 */
function KeyedErrorBoundary({ children }: { children: ReactNode }) {
  const location = useLocation()
  return <ErrorBoundary key={location.pathname}>{children}</ErrorBoundary>
}

/**
 * The route table.
 *
 * `RequireAuth` is a layout route rather than a wrapper repeated on each element, so a screen added
 * inside it is protected by where it sits — forgetting the guard is not a mistake that can be made one
 * route at a time. Each module then sits behind `RequirePermission` for the *specific* permission its
 * screen needs: a create form requires `Create`, not `View`, so a URL typed by someone who can only read
 * lands on the "no access" page instead of a form whose submit was always going to be refused.
 *
 * Departments and designations share one list component and one form component, configured by a module
 * object. The `key` on each element is what makes that safe: without it React would reuse the mounted
 * `LookupListPage` when moving between the two routes, and its state — including the search box — would
 * carry across from one resource to the other.
 *
 * The catch-all lives *inside* the shell so a mistyped URL keeps the navigation, letting the user click
 * their way out instead of landing on a bare page.
 */
export function App() {
  const apex = isApexHost(window.location.hostname)

  return (
    <ErrorBoundary>
      <AuthProvider>
        <KeyedErrorBoundary>
        <Routes>
          {apex ? (
            <Route path="/" element={<WorkspacePickerPage />} />
          ) : (
            <>
              <Route path="/login" element={<LoginPage />} />

              <Route element={<RequireAuth />}>
                <Route element={<AppLayout />}>
                  <Route index element={<Navigate to="/dashboard" replace />} />
                  <Route path="dashboard" element={<DashboardPage />} />
                  <Route path="masters/:kind" element={<MasterManagementPage />} />
                  <Route
                    path="configuration/employee-code"
                    element={
                      <RequirePermission permission={Permissions.employeeCodeConfiguration.view}>
                        <EmployeeCodeConfigurationPage />
                      </RequirePermission>
                    }
                  />

              <Route path="employees">
                <Route
                  index
                  element={
                    <RequirePermission permission={Permissions.employee.view}>
                      <EmployeesPage />
                    </RequirePermission>
                  }
                />
                <Route
                  path="new"
                  element={
                    <RequirePermission permission={Permissions.employee.create}>
                      <EmployeeFormPage />
                    </RequirePermission>
                  }
                />
                <Route
                  path=":id"
                  element={
                    <RequirePermission permission={Permissions.employee.view}>
                      <EmployeeDetailPage />
                    </RequirePermission>
                  }
                />
                <Route
                  path=":id/edit"
                  element={
                    <RequirePermission permission={Permissions.employee.edit}>
                      <EmployeeFormPage />
                    </RequirePermission>
                  }
                />
              </Route>

              <Route path="departments/*" element={<LegacyMasterRedirect kind="departments" />} />
              <Route path="designations/*" element={<LegacyMasterRedirect kind="designations" />} />

              <Route path="forbidden" element={<ForbiddenPage />} />
              <Route path="*" element={<NotFoundPage />} />
            </Route>
          </Route>
          </>
          )}
        </Routes>
        </KeyedErrorBoundary>
      </AuthProvider>
    </ErrorBoundary>
  )
}

function LegacyMasterRedirect({ kind }: { kind: 'departments' | 'designations' }) {
  const { '*': suffix = '' } = useParams()
  const location = useLocation()
  const parts = suffix.split('/').filter(Boolean)
  const legacyId = parts[0] ?? ''
  const query = legacyId === 'new' ? '?add=1' : parts.length >= 2 && parts[1] === 'edit' ? `?edit=${encodeURIComponent(legacyId)}` : location.search
  return <Navigate replace to={`/masters/${kind}${query}`} />
}
