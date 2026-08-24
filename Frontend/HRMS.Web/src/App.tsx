import { Navigate, Route, Routes } from 'react-router-dom'
import { AuthProvider } from './auth/AuthProvider.tsx'
import { Permissions } from './auth/permissions.ts'
import { RequireAuth } from './auth/RequireAuth.tsx'
import { RequirePermission } from './auth/RequirePermission.tsx'
import { ErrorBoundary } from './components/ErrorBoundary.tsx'
import { AppLayout } from './layout/AppLayout.tsx'
import { DashboardPage } from './pages/DashboardPage.tsx'
import { EmployeeFormPage } from './pages/employees/EmployeeFormPage.tsx'
import { EmployeesPage } from './pages/employees/EmployeesPage.tsx'
import { ForbiddenPage } from './pages/ForbiddenPage.tsx'
import { LoginPage } from './pages/LoginPage.tsx'
import { departmentsModule, designationsModule } from './pages/lookups/lookupModules.ts'
import { LookupFormPage } from './pages/lookups/LookupFormPage.tsx'
import { LookupListPage } from './pages/lookups/LookupListPage.tsx'
import { NotFoundPage } from './pages/NotFoundPage.tsx'

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
  return (
    <ErrorBoundary>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />

          <Route element={<RequireAuth />}>
            <Route element={<AppLayout />}>
              <Route index element={<Navigate to="/dashboard" replace />} />
              <Route path="dashboard" element={<DashboardPage />} />

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
                  path=":id/edit"
                  element={
                    <RequirePermission permission={Permissions.employee.edit}>
                      <EmployeeFormPage />
                    </RequirePermission>
                  }
                />
              </Route>

              {[departmentsModule, designationsModule].map((module) => (
                <Route key={module.key} path={module.key}>
                  <Route
                    index
                    element={
                      <RequirePermission permission={module.permissions.view}>
                        <LookupListPage key={module.key} module={module} />
                      </RequirePermission>
                    }
                  />
                  <Route
                    path="new"
                    element={
                      <RequirePermission permission={module.permissions.create}>
                        <LookupFormPage key={module.key} module={module} />
                      </RequirePermission>
                    }
                  />
                  <Route
                    path=":id/edit"
                    element={
                      <RequirePermission permission={module.permissions.edit}>
                        <LookupFormPage key={module.key} module={module} />
                      </RequirePermission>
                    }
                  />
                </Route>
              ))}

              <Route path="forbidden" element={<ForbiddenPage />} />
              <Route path="*" element={<NotFoundPage />} />
            </Route>
          </Route>
        </Routes>
      </AuthProvider>
    </ErrorBoundary>
  )
}
