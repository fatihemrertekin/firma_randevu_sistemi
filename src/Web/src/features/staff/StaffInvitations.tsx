import ErrorMessage from '../../components/ErrorMessage'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import ManualDeliveryCode from '../../components/ManualDeliveryCode'
import { formatBusinessDateTime } from '../../app/format'
import styles from './StaffAccounts.module.css'

type Invitation = { id: string; email: string; expiresAt: string }
type Issued = { id: string; token: string; expiresAt: string }
type Props = { post: (path: string, body: object) => Promise<Response> }

export default function StaffInvitations({ post }: Props) {
  const [email, setEmail] = useState('')
  const [verified, setVerified] = useState(false)
  const [issued, setIssued] = useState<Issued | null>(null)
  const [list, setList] = useState<Invitation[]>([])
  const [busy, setBusy] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const pending = useRef(false)

  async function load() {
    const response = await fetch('/api/staff-invitations/', { cache: 'no-store' })
    if (!response.ok) throw new Error('Davet listesi alınamadı. Oturumunuzu kontrol edip yeniden deneyin.')
    setList((await response.json()) as Invitation[])
  }
  useEffect(() => {
    let active = true
    void fetch('/api/staff-invitations/', { cache: 'no-store' }).then(async response => {
      if (!response.ok) throw new Error('Davet listesi alınamadı. Oturumunuzu kontrol edip yeniden deneyin.')
      const items = (await response.json()) as Invitation[]
      if (active) setList(items)
    }).catch((failure: unknown) => { if (active) setError(failure instanceof Error ? failure.message : 'Davet listesi alınamadı.') })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [])

  async function issue(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending.current || !verified) return
    pending.current = true
    setBusy(true)
    setError('')
    setNotice('')
    setIssued(null)
    try {
      const response = await post('/api/staff-invitations/', { email, verifiedRecipient: verified })
      if (response.ok) {
        setIssued((await response.json()) as Issued)
        setEmail('')
        setVerified(false)
        try { await load() } catch { setError('Kod oluşturuldu; davet listesi yenilenemedi.') }
      } else if ([400, 409].includes(response.status)) {
        const problem = (await response.json()) as { title?: string }
        setError(problem.title ?? 'Davet bilgilerini kontrol edin.')
      } else if (response.status === 429) {
        setError('Çok fazla deneme. Daha sonra tekrar deneyin.')
      } else {
        setError('Davet sonucu doğrulanamadı. Listeyi yenileyin; gerekirse daveti iptal edip yeniden üretin. Otomatik tekrar yapılmadı.')
      }
    } catch {
      setError('Davet sonucu doğrulanamadı. Listeyi yenileyin; gerekirse daveti iptal edip yeniden üretin. Otomatik tekrar yapılmadı.')
    } finally { pending.current = false; setBusy(false) }
  }
  async function revoke(id: string) {
    if (pending.current) return
    pending.current = true
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const response = await post(`/api/staff-invitations/${id}/revoke`, {})
      if (!response.ok) throw new Error('Davet iptali doğrulanamadı. Listeyi yenileyip kontrol edin.')
      if (issued?.id === id) setIssued(null)
      setNotice('Davet iptal edildi. Bu işlem kullanılmış bir davetten açılan hesabı kapatmaz.')
      await load()
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Davet iptali doğrulanamadı.') }
    finally { pending.current = false; setBusy(false) }
  }

  return <section aria-labelledby="staff-invitation-title">
    <h2 id="staff-invitation-title">Çalışan daveti</h2>
    <p>Çalışan kendi parolasını belirler. Kod 24 saat geçerli; yalnız doğruladığınız çalışana özel kanaldan teslim edin.</p>
    <form onSubmit={issue} aria-label="Çalışan daveti oluştur" aria-busy={busy}>
      <label htmlFor="invite-email">Çalışanın e-postası</label>
      <input id="invite-email" type="email" required maxLength={256} autoComplete="off" disabled={busy}
        value={email} onChange={event => setEmail(event.target.value)} />
      <label><input type="checkbox" checked={verified} disabled={busy}
        onChange={event => setVerified(event.target.checked)} /> E-postanın çalışana ait olduğunu doğruladım; kodu yalnız kendisine teslim edeceğim.</label>
      <button type="submit" disabled={busy || !verified}>{busy ? 'İşlem sürüyor…' : 'Davet oluştur'}</button>
    </form>
    {issued && <ManualDeliveryCode key={issued.token} inputId="issued-invitation"
      label="Davet kodu — yalnız bu ekranda gösterilir" token={issued.token}
      expiresAt={issued.expiresAt} busy={busy} onClear={() => setIssued(null)} />}
    <h3>Bekleyen davetler</h3>
    {loading ? <p role="status">Davetler yükleniyor…</p> : list.length === 0 ? <p>Geçerli bekleyen davet yok.</p> : <ul className={styles.invitationList}>
      {list.map(item => <li key={item.id} className={styles.invitationRow}><span className={styles.account}>{item.email} — {formatBusinessDateTime(item.expiresAt)}</span>
        <button type="button" disabled={busy} onClick={() => revoke(item.id)} aria-label={`${item.email} davetini iptal et`}>Daveti iptal et</button>
      </li>)}
    </ul>}
    <button type="button" disabled={busy || loading} onClick={async () => {
      setLoading(true)
      setError('')
      try { await load() } catch (failure) { setError(failure instanceof Error ? failure.message : 'Davet listesi alınamadı.') }
      finally { setLoading(false) }
    }}>Davetleri yenile</button>
    <ErrorMessage message={error} />{notice && <p role="status">{notice}</p>}
  </section>
}
