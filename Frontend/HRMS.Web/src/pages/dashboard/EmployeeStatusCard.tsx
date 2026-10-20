import { Card } from '../../components/Card.tsx'

/**
 * The employee list is paginated and does not expose a complete status aggregate. Keeping this
 * state explicit prevents a single page of rows from being presented as an organisation-wide donut.
 * The card is intentionally shaped as the future chart slot so the Option 2 grid remains stable
 * when a verified aggregate API becomes available.
 */
export function EmployeeStatusCard() {
  return (
    <Card title="Employee status" subtitle="Verified status distribution">
      <div className="dashboard-chart-unavailable" role="status">
        <div className="dashboard-status-placeholder" aria-hidden="true">
          <span />
          <strong>N/A</strong>
        </div>
        <div>
          <p className="dashboard-chart-unavailable-title">Distribution unavailable</p>
          <p className="dashboard-muted">The authorized employee list is paginated. A complete status population is not available from the current contract.</p>
        </div>
      </div>
    </Card>
  )
}
