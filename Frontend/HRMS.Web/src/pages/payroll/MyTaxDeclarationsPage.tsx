import { useState } from 'react'
import { Card } from '../../components/Card.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { deleteMyTaxDeclarationLine, getMyTaxDeclaration, replaceMyTaxDeclarationProof, resubmitMyTaxDeclaration, updateMyTaxDeclarationLine, type TaxDeclarationLine } from '../../api/taxDeclarations.ts'

export function MyTaxDeclarationsPage() {
  const declaration = useApiQuery(signal => getMyTaxDeclaration(signal), [])
  const value = declaration.data
  const [editing, setEditing] = useState<string | null>(null)
  const [amount, setAmount] = useState('')
  const editable = value?.status === 'Draft' || value?.status === 'ResubmissionRequired'
  const canEditLine = (line: TaxDeclarationLine) => editable && (value?.status === 'Draft' || line.status === 'Rejected' || line.status === 'ResubmissionRequired')
  const beginEdit = (line: TaxDeclarationLine) => { setEditing(line.id); setAmount(String(line.declaredAmount)) }
  const saveEdit = async (line: TaxDeclarationLine) => { if (!value) return; await updateMyTaxDeclarationLine(value.id, line.id, { declaredAmount: Number(amount), referenceNumber: line.referenceNumber ?? undefined, declarationDate: line.declarationDate ?? undefined, notes: line.notes ?? undefined }); setEditing(null); declaration.refetch() }
  const deleteLine = async (line: TaxDeclarationLine) => { if (!value || !window.confirm(`Delete ${line.itemCode}?`)) return; await deleteMyTaxDeclarationLine(value.id, line.id); declaration.refetch() }
  const replaceProof = async (line: TaxDeclarationLine, proofId: string) => { if (!value) return; await replaceMyTaxDeclarationProof(value.id, line.id, proofId, { lineId: line.id, fileName: `replacement-${proofId}.pdf`, contentType: 'application/pdf', fileSize: 0, storageReference: `tax-proof/replacement-${proofId}`, documentType: 'InvestmentProof' }); declaration.refetch() }
  const resubmit = async () => { if (value) { await resubmitMyTaxDeclaration(value.id); declaration.refetch() } }

  return <section className="page-shell"><PageHeader title="My Tax Declaration" subtitle="Review your declaration status, declared values, approved values, and proof review feedback." />{!value ? <Card title="Tax declaration"><p>No active declaration was found.</p></Card> : <Card title={`${value.cycleCode} · FY ${value.financialYear}`}><p>Status: <strong>{value.status}</strong></p>{value.status === 'ResubmissionRequired' && <button type="button" onClick={resubmit}>Resubmit declaration</button>}<div className="table-wrap"><table className="data-table"><thead><tr><th>Category</th><th>Item</th><th>Declared</th><th>Approved</th><th>Status</th><th>Proofs</th><th>Reviewer comment</th><th>Actions</th></tr></thead><tbody>{value.lines.map(line => <tr key={line.id}><td>{line.categoryCode}</td><td>{line.itemCode}</td><td>{editing === line.id ? <input aria-label={`Declared amount ${line.itemCode}`} value={amount} onChange={event => setAmount(event.target.value)} /> : line.declaredAmount.toFixed(2)}</td><td>{(line.approvedAmount ?? 0).toFixed(2)}</td><td>{line.status}</td><td>{line.proofs.map(proof => <div key={proof.id}><span>{proof.fileName} ({proof.status})</span>{proof.reviewerComment && <small> {proof.reviewerComment}</small>}{proof.status === 'Rejected' && value.status === 'ResubmissionRequired' && <button type="button" onClick={() => replaceProof(line, proof.id)}>Replace proof</button>}</div>)}</td><td>{line.reviewerComment ?? '—'}</td><td>{canEditLine(line) && (editing === line.id ? <button type="button" onClick={() => saveEdit(line)}>Save</button> : <button type="button" onClick={() => beginEdit(line)}>Edit</button>)}{value.status === 'Draft' && <button type="button" onClick={() => deleteLine(line)}>Delete</button>}</td></tr>)}</tbody></table></div></Card>}</section>
}
