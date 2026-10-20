import type { ApiError } from '../../api/errors.ts'
import { ErrorState } from '../ErrorState.tsx'
export function DashboardErrorState({ error, onRetry }: { error: ApiError; onRetry?: () => void }) { return <div className="dashboard-error-state"><ErrorState error={error} onRetry={onRetry} /></div> }
