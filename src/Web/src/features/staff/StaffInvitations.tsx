import ErrorMessage from '../../components/ErrorMessage'
import { useEffect, useEffectEvent, useRef, useState, type FormEvent } from 'react'
import ManualDeliveryCode from '../../components/ManualDeliveryCode'
import { formatBusinessDateTime } from '../../app/format'
import { isManualDeliveryCode } from '../../app/manualDeliveryCode'
import styles from './StaffAccounts.module.css'

type Invitation = { id: string; email: string; expiresAt: string }
type Issued = { id: string; token: string; expiresAt: string }
type Props = { post: (path: string, body: object) => Promise<Response>; disabled: boolean; onBusyChange: (busy: boolean) => void }

function parseInvitations(value: unknown): Invitation[] {
  if (!Array.isArray(value) || value.length > 20 || !value.every(item =>
    typeof item === 'object' && item !== null && typeof item.id === 'string' && item.id.length > 0 &&
    typeof item.email === 'string' && item.email.length > 0 && typeof item.expiresAt === 'string' &&
    Number.isFinite(Date.parse(item.expiresAt)))) throw new Error('Davet listesi doğrulanamadı. Yeniden deneyin.')
  return value
}
async function fetchInvitations(signal: AbortSignal) {
  const response = await fetch('/api/staff-invitations/', { cache: 'no-store', signal })
  if (!response.ok) throw new Error('Davet listesi alınamadı. Oturumunuzu kontrol edip yeniden deneyin.')
  return parseInvitations(await response.json())
}

export default function StaffInvitations({ post, disabled, onBusyChange }: Props) {
  const [email, setEmail] = useState('')
  const [verified, setVerified] = useState(false)
  const [issued, setIssued] = useState<Issued | null>(null)
  const [list, setList] = useState<Invitation[]>([])
  const [listLoaded, setListLoaded] = useState(false)
  const [busy, setBusy] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const pending = useRef(false)
  const lastAutomaticRefresh = useRef(0)
  const refreshOnReturn = useEffectEvent(() => {
    if (document.visibilityState !== 'visible' || disabled || busy || loading || error ||
      Date.now() - lastAutomaticRefresh.current < 15000) return
    lastAutomaticRefresh.current = Date.now(); void reload()
  })
  useEffect(() => {
    const refresh = () => refreshOnReturn()
    window.addEventListener('focus', refresh); document.addEventListener('visibilitychange', refresh)
    return () => { window.removeEventListener('focus', refresh); document.removeEventListener('visibilitychange', refresh) }
  }, [])
  useEffect(() => { if (busy) { onBusyChange(true); return () => onBusyChange(false) } }, [busy, onBusyChange])

  async function load(signal = AbortSignal.timeout(15000)) {
    const items = await fetchInvitations(signal)
    if (!signal.aborted) { setList(items); setListLoaded(true) }
  }
  async function reload() {
    setLoading(true); setError('')
    try { await load() } catch (failure) { setError(failure instanceof Error ? failure.message : 'Davet listesi alınamadı.') }
    finally { setLoading(false) }
  }
  useEffect(() => {
    const controller = new AbortController()
    void fetchInvitations(AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]))
      .then(items => { if (!controller.signal.aborted) { setList(items); setListLoaded(true) } })
      .catch((failure: unknown) => { if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : 'Davet listesi alınamadı.') })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [])

  async function issue(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending.current || disabled || !verified) return
    pending.current = true
    setBusy(true)
    setError('')
    setNotice('')
    setIssued(null)
    try {
      const response = await post('/api/staff-invitations/', { email, verifiedRecipient: verified })
      if (response.ok) {
        const body: unknown = await response.json()
        if (!isManualDeliveryCode(body) || !('id' in body) || typeof body.id !== 'string' || !body.id) throw new Error('code')
        setIssued({ id: body.id, token: body.token, expiresAt: body.expiresAt })
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
    if (pending.current || disabled) return
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
      <input id="invite-email" type="email" required maxLength={256} autoComplete="off" disabled={disabled || busy}
        value={email} onChange={event => setEmail(event.target.value)} />
      <label><input type="checkbox" checked={verified} disabled={disabled || busy}
        onChange={event => setVerified(event.target.checked)} /> E-postanın çalışana ait olduğunu doğruladım; kodu yalnız kendisine teslim edeceğim.</label>
      <button type="submit" disabled={disabled || busy || !verified}>{busy ? 'İşlem sürüyor…' : 'Davet oluştur'}</button>
    </form>
    {issued && <ManualDeliveryCode key={issued.token} inputId="issued-invitation"
      label="Davet kodu — yalnız bu ekranda gösterilir" token={issued.token}
      expiresAt={issued.expiresAt} busy={disabled || busy} onClear={() => setIssued(null)} />}
    <h3>Bekleyen davetler</h3>
    {loading ? <p role="status">Davetler yükleniyor…</p> : listLoaded && list.length === 0 ? <p>Geçerli bekleyen davet yok.</p> : list.length > 0 && <ul className={styles.invitationList}>
      {list.map(item => <li key={item.id} className={styles.invitationRow}><span className={styles.account}>{item.email} — {formatBusinessDateTime(item.expiresAt)}</span>
        <button type="button" disabled={disabled || busy} onClick={() => revoke(item.id)} aria-label={`${item.email} davetini iptal et`}>Daveti iptal et</button>
      </li>)}
    </ul>}
    {error && <button type="button" disabled={disabled || busy || loading} onClick={() => { void reload() }}>Davetleri yenile</button>}
    <ErrorMessage message={error} />{notice && <p role="status">{notice}</p>}
  </section>
}
