import { useState } from 'react'
import { getMyPayslipDocument, listMyPayslips } from '../../api/payroll.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Pagination } from '../../components/Pagination.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'
import { useListQuery } from '../../hooks/useListQuery.ts'

export function MyPayslipsPage() {
  useDocumentTitle('My Payslips'); const list = useListQuery({ sortFields: ['periodEndDate'], defaultSortBy: 'periodEndDate' }); const { data, error, isLoading } = useApiQuery(() => listMyPayslips({ ...list.pagedQuery }), [list.key]); const [actionError, setActionError] = useState<string | null>(null)
  async function print(id: string) { try { const html = await getMyPayslipDocument(id); const popup = window.open('', '_blank'); if (!popup) throw new Error('Allow pop-ups to print a payslip.'); popup.document.write(html); popup.document.close(); popup.focus(); popup.print() } catch (e) { setActionError(e instanceof Error ? e.message : 'Unable to open the payslip.') } }
  return <><PageHeader title="My Payslips" subtitle="Published payslips for your linked employee identity." />{(error || actionError) && <Notice tone="error">{actionError ?? 'Unable to load payslips.'}</Notice>}<Card isRefreshing={isLoading}>{data?.items.length === 0 && <p>No published payslips are available.</p>}<div className="table-wrap"><table className="data-table"><thead><tr><th>Period</th><th>Payslip</th><th>Gross</th><th>Deductions</th><th>Net pay</th><th>Status</th><th /></tr></thead><tbody>{data?.items.map(x => <tr key={x.id}><td>{x.periodStartDate} – {x.periodEndDate}</td><td>{x.payslipNumber}</td><td>{x.grossEarnings.toFixed(2)} {x.currencyCode}</td><td>{x.totalDeductions.toFixed(2)}</td><td>{x.netPay.toFixed(2)}</td><td>{x.status}</td><td><button className="button button-secondary" type="button" onClick={() => { void print(x.id) }}>Print</button></td></tr>)}</tbody></table></div>{data && <Pagination info={data} onPageChange={list.setPage} disabled={isLoading} />}</Card></>
}
