import { useCallback, useEffect, useRef, useState } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import { formatBusinessDateTime } from '../../app/format'
import useNavigationGuard from '../../app/useNavigationGuard'
import { RequestVerificationError } from '../../app/api'
import { BookingError, errorMessage, get, isAccess, isResult, post, priceLabel, type Access, type Receipt, type Result } from './publicBookingApi'
import { useClock } from './BookingWizard'
import styles from './PublicBooking.module.css'

const labels = { Pending: 'İşletme onayı bekleniyor', Confirmed: 'Randevunuz onaylandı', Cancelled: 'Randevu iptal edildi', Completed: 'Tamamlandı', NoShow: 'Gelinmedi' }
export default function BookingResult({ id, initialResult, initialToken, receipt }: { id: string; initialResult?: Result; initialToken?: string; receipt?: Receipt }) {
  const [result, setResult] = useState(initialResult), [access, setAccess] = useState<Access | null>(null), [token, setToken] = useState(initialToken ?? '')
  const [busy, setBusy] = useState(false), [reading, setReading] = useState(!!initialToken), [failure, setFailure] = useState<unknown>(null), [notice, setNotice] = useState(''), [confirm, setConfirm] = useState(false), [rotate, setRotate] = useState(false), [showLink, setShowLink] = useState(false), [retryAt, setRetryAt] = useState(0)
  const cancelCommand = useRef<{ version: string; requestKey: string } | null>(null), pending = useRef(false), readPending = useRef(false), automaticIssue = useRef(false), epoch = useRef(0)
  const guard = useRef({ dirty: false, profileDirty: false, busy: false })
  const heading = useRef<HTMLHeadingElement>(null)
  const cancelTrigger = useRef<HTMLButtonElement>(null), returningFocus = useRef(false)
  const now = useClock(), [unknownCancel, setUnknownCancel] = useState(false)
  useEffect(() => { guard.current = { dirty: !!receipt && !token || unknownCancel, profileDirty: false, busy } }, [receipt, token, unknownCancel, busy])
  useNavigationGuard(guard, () => {})
  const [revision, setRevision] = useState(0)
  function reload() { if (!token || pending.current || readPending.current || Date.now() < retryAt) return; setReading(true); setRevision(v => v + 1) }
  useEffect(() => {
    if (!token) return
    const controller = new AbortController(), generation = epoch.current
    readPending.current = true
    void get(`/appointments/${id}`, isResult, AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]), token).then(value => {
      if (controller.signal.aborted || generation !== epoch.current) return
      if (value.id !== id) throw new Error('invalid-response')
      setResult(value); setFailure(null)
    }).catch((e: unknown) => {
      if (controller.signal.aborted || generation !== epoch.current) return
      setFailure(e); if (e instanceof BookingError && e.status === 429) setRetryAt(e.retryAt)
    }).finally(() => { if (!controller.signal.aborted) { readPending.current = false; setReading(false) } })
    return () => { controller.abort(); readPending.current = false }
  }, [id, token, revision])
  useEffect(() => {
    const focus = () => { if (document.visibilityState === 'visible' && token && !pending.current && !readPending.current && Date.now() >= retryAt) { setReading(true); setRevision(v => v + 1) } }
    document.addEventListener('visibilitychange', focus)
    return () => document.removeEventListener('visibilitychange', focus)
  }, [token, retryAt])
  useEffect(() => { heading.current?.focus() }, [result?.status])
  useEffect(() => {
    if (!confirm && returningFocus.current) { cancelTrigger.current?.focus(); returningFocus.current = false }
  }, [confirm])
  const issue = useCallback(async (replaceExisting: boolean) => {
    if (!receipt || pending.current || Date.now() < retryAt) return
    pending.current = true; epoch.current++; setBusy(true); setFailure(null); setNotice('')
    try {
      const value = await post(`/appointments/${id}/access`, { ...receipt, replaceExisting }, isAccess)
      setAccess(value); setToken(value.token); setRotate(false); setNotice('Sonuç bağlantınız hazır. Bağlantıyı kaydedin.')
    } catch (e) { setFailure(e); setRotate(true); if (e instanceof BookingError && e.status === 429) setRetryAt(e.retryAt) }
    finally { pending.current = false; setBusy(false) }
  }, [id, receipt, retryAt])
  useEffect(() => {
    if (!receipt || automaticIssue.current || token) return
    automaticIssue.current = true
    void issue(false)
  }, [issue, receipt, token])
  async function cancel() {
    if (!token || !result || pending.current || now < retryAt) return
    pending.current = true; epoch.current++; setBusy(true); setFailure(null); setNotice('')
    cancelCommand.current ??= { version: result.version, requestKey: crypto.randomUUID() }
    setUnknownCancel(true)
    try {
      const value = await post(`/appointments/${id}/cancel`, cancelCommand.current, isResult, token)
      if (value.id !== id) throw new Error('invalid-response')
      setResult(value); cancelCommand.current = null; setUnknownCancel(false); setConfirm(false); setNotice('Randevu iptal edildi.')
    } catch (e) {
      setFailure(e)
      if (!unknownCancel && e instanceof RequestVerificationError) { cancelCommand.current = null; setUnknownCancel(false); setConfirm(false) }
      if (e instanceof BookingError) {
        if (e.status === 429) setRetryAt(e.retryAt)
        if (e.status === 409) { setReading(true); setRevision(v => v + 1) }
        if (!unknownCancel && [400,403,409,429].includes(e.status)) { cancelCommand.current = null; setUnknownCancel(false); setConfirm(false) }
      }
    } finally { pending.current = false; setBusy(false) }
  }
  const expired = access && now >= Date.parse(access.expiresAt)
  const canCancel = result && token && !expired && ['Pending','Confirmed'].includes(result.status) && now < Date.parse(result.startsAt) && now <= Date.parse(result.cancellationDeadline) && (result.status !== 'Pending' || !!result.pendingExpiresAt && now < Date.parse(result.pendingExpiresAt))
  const link = token ? `${window.location.origin}/randevu/sonuc/${id}#erisim=${token}` : ''
  async function copyLink() {
    try { await navigator.clipboard.writeText(link); setNotice('Sonuç bağlantısı kopyalandı. Güvenli bir yerde saklayın.'); setShowLink(false) }
    catch { setNotice('Bağlantı kopyalanamadı. Aşağıdaki bağlantıyı seçerek kendiniz kopyalayın.'); setShowLink(true) }
  }
  const noAuthority = failure instanceof BookingError && failure.status === 403
  return <section aria-label="Randevu sonucu">
    <h1 tabIndex={-1} ref={heading}>{result ? labels[result.status] : 'Randevu sonucu'}</h1>
    <ErrorMessage message={noAuthority ? 'Bağlantı geçersiz veya süresi dolmuş. Kaydettiğiniz güncel bağlantıyı kullanın; açılmıyorsa işletmeyle iletişime geçin.' : failure ? errorMessage(failure) : ''}/>
    {notice && <p role="status">{notice}</p>}
    {!token && !receipt && <p>Bu randevuyu açmak için kaydettiğiniz tam sonuç bağlantısını kullanın. Yalnız randevu numarası erişim sağlamaz.</p>}
    {reading && <p role="status">Güncel sonuç yükleniyor…</p>}
    {result && <div className={styles.layout}><div className={styles.form}>
      {result.status === 'Pending' && <p>Talebiniz kaydedildi. Henüz onaylanmadı.</p>}
      {result.status === 'Cancelled' && <p>Bu randevu artık geçerli değil.</p>}
      {result.pendingExpiresAt && result.status === 'Pending' && <p>{now >= Date.parse(result.pendingExpiresAt) ? 'Onay süresi doldu; güncel durumu kontrol edin.' : `Onay son anı: ${formatBusinessDateTime(result.pendingExpiresAt)}`}</p>}
      <p>İptal son anı: {formatBusinessDateTime(result.cancellationDeadline)}</p>
      {!canCancel && ['Pending','Confirmed'].includes(result.status) && <p>Bu görünümden şu anda iptal edilemiyor. Güncel durumu kontrol edin; gerekirse işletmeyle iletişime geçin.</p>}
      <div className={styles.actions}>
        {token && <button disabled={busy || reading || now < retryAt} onClick={() => { setReading(true); void reload() }}>Durumu güncelle</button>}
        {token && !expired && <button disabled={busy || noAuthority} onClick={() => void copyLink()}>Sonuç bağlantısını kopyala</button>}
      </div>
      {token && <p>Bu bağlantıyı kaydedin. Bağlantıya sahip olan kişi bu randevuyu görüntüleyebilir ve süre uygunsa iptal edebilir. Sayfayı yenilerseniz kaydettiğiniz bağlantıyı yeniden açmanız gerekir.</p>}
      {access && <p>Bağlantı son anı: {formatBusinessDateTime(access.expiresAt)}</p>}
      {showLink && <><label htmlFor="result-link">Kopyalanacak sonuç bağlantısı</label><input id="result-link" readOnly value={link} onFocus={e => e.target.select()}/></>}
      {receipt && !token && <><p>Randevunuz kaydedildi; sonuç bağlantısı henüz hazırlanamadı. Sayfayı kapatmadan bağlantıyı hazırlayın. Oluşturma yetkisi en fazla 24 saat geçerlidir.</p><button disabled={busy || now < retryAt} onClick={() => { setRotate(false); void issue(false) }}>Bağlantıyı yeniden hazırla</button></>}
      {receipt && rotate && <div className={styles.note}><p>İlk bağlantı hazırlanmış olabilir. Yeni bağlantı oluşturursanız önceki bağlantı geçersiz olacak; randevunuz değişmez.</p><div className={styles.actions}><button disabled={busy} onClick={() => setRotate(false)}>Vazgeç</button><button disabled={busy || now < retryAt} onClick={() => void issue(true)}>Yeni bağlantı oluştur</button></div></div>}
      {canCancel && !confirm && !unknownCancel && <button ref={cancelTrigger} className={styles.danger} disabled={busy || reading || noAuthority} onClick={() => setConfirm(true)}>Randevuyu iptal et</button>}
      {(confirm || unknownCancel) && <div className={styles.note}><h2>{busy ? 'İptal işleniyor…' : unknownCancel ? 'İptal sonucu doğrulanamadı' : 'Bu randevuyu iptal etmek istiyor musunuz?'}</h2><p>{result.serviceName} · {formatBusinessDateTime(result.startsAt)}</p><div className={styles.actions}>{!unknownCancel && <button disabled={busy} onClick={() => { returningFocus.current = true; setConfirm(false) }}>Vazgeç</button>}<button className={styles.danger} disabled={busy || now < retryAt} onClick={() => void cancel()}>{busy ? 'İşleniyor…' : unknownCancel ? 'Aynı iptali yeniden dene' : 'Evet, randevuyu iptal et'}</button></div></div>}
    </div><aside className={styles.summary} aria-label="Randevu özeti"><h2>Randevunuz</h2><div><span>Hizmet</span><strong>{result.serviceName}</strong><p>Hizmet tutarı: {priceLabel(result.price, result.currency)}</p></div><div><span>Çalışan</span><strong>{result.staffName}</strong></div><div><span>Tarih ve saat · İstanbul saati</span><strong>{formatBusinessDateTime(result.startsAt)}–{formatBusinessDateTime(result.endsAt)}</strong></div></aside></div>}
    {!result && token && <button disabled={reading || now < retryAt} onClick={() => { setReading(true); void reload() }}>Yeniden dene</button>}
    {now < retryAt && <p role="status">Yaklaşık {Math.ceil((retryAt - now) / 1000)} saniye bekleyin.</p>}
  </section>
}
