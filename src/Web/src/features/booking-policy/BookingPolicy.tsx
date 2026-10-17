import { RequestVerificationError, requestErrorMessage } from '../../app/api'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import type { postWithCsrf } from '../../app/api'
import useUnsavedChanges from '../../app/useUnsavedChanges'
import ErrorMessage from '../../components/ErrorMessage'
import { BookingPolicyError, bookingPolicyEndpoint, bookingPolicyFailure, policyFieldErrors, policyFields, policyLimits, readBookingPolicy, type BookingPolicy as Policy, type PolicyField, type PolicyErrors } from './bookingPolicyApi'
import styles from './BookingPolicy.module.css'

type Props = { post: typeof postWithCsrf; onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void }
const labels = {
  leadMinutes: { title: 'En az kaç dakika önce?', help: 'Randevu başlangıcına kalan en kısa süre.', unit: 'dakika' },
  horizonDays: { title: 'En fazla kaç gün ileriye?', help: 'Bugünden itibaren randevu alınabilecek süre.', unit: 'gün' },
  slotMinutes: { title: 'Başlangıç saatleri aralığı', help: 'Randevular bu aralıklarla başlar. Hizmet süresi değişmez.', unit: 'dakika' },
  customerCancellationLeadMinutes: { title: 'Müşteri ne zamana kadar iptal edebilir?', help: 'Müşteri, bağlantısından randevuya bu kadar süre kalana kadar iptal edebilir.', unit: 'dakika' },
}
type Draft = Record<PolicyField, string> & { requiresBusinessApproval: boolean }
function draftOf(value: Policy): Draft { return { leadMinutes: String(value.leadMinutes), horizonDays: String(value.horizonDays), slotMinutes: String(value.slotMinutes), customerCancellationLeadMinutes: String(value.customerCancellationLeadMinutes), requiresBusinessApproval: value.requiresBusinessApproval } }
function cancellationExample(value: string) {
  if (!/^\d{1,5}$/u.test(value) || Number(value) > 43200) return 'İptal süresi için 0–43200 arasında tam dakika girin.'
  const minutes = Number(value)
  if (minutes === 0) return 'Müşteri randevu başlamadan iptal edebilir.'
  const hours = Math.floor(minutes / 60), rest = minutes % 60
  return `${minutes} dakika${hours ? ` = ${hours} saat${rest ? ` ${rest} dakika` : ''}` : ''}. Sınırdan sonra işletmeyle iletişime geçilir.`
}
function slotExample(value: string) {
  if (!/^\d{1,5}$/u.test(value) || Number(value) < 1 || Number(value) > 60) return 'Aralık örneği için 1–60 dakika girin.'
  const step = Number(value), start = Math.ceil(540 / step) * step
  return 'Aralık örneği: ' + [start, start + step, start + 2 * step].map(minutes => `${String(Math.floor(minutes / 60)).padStart(2, '0')}.${String(minutes % 60).padStart(2, '0')}`).join(' · ')
}
export default function BookingPolicy({ post, onDirtyChange, onBusyChange }: Props) {
  const [original, setOriginal] = useState<Policy | null>(null)
  const [draft, setDraft] = useState<Draft>({ leadMinutes: '', horizonDays: '', slotMinutes: '', customerCancellationLeadMinutes: '', requiresBusinessApproval: true })
  const [loading, setLoading] = useState(true), [busy, setBusy] = useState(false), [revision, setRevision] = useState(0)
  const [locked, setLocked] = useState(false), [denied, setDenied] = useState(false)
  const [error, setError] = useState(''), [notice, setNotice] = useState('')
  const [errors, setErrors] = useState<PolicyErrors>({}), [retryUntil, setRetryUntil] = useState(0)
  const form = useRef<HTMLFormElement>(null), sending = useRef(false)
  const dirty = original !== null && (policyFields.some(key => draft[key] !== String(original[key])) || draft.requiresBusinessApproval !== original.requiresBusinessApproval)
  useUnsavedChanges(dirty, onDirtyChange)
  useEffect(() => { onBusyChange(loading || busy); return () => onBusyChange(false) }, [loading, busy, onBusyChange])
  useEffect(() => {
    if (!retryUntil) return
    const timer = setTimeout(() => setRetryUntil(0), Math.max(0, retryUntil - Date.now()))
    return () => clearTimeout(timer)
  }, [retryUntil])
  function respectRetry(response: Response) {
    const header = response.headers.get('Retry-After')
    if (!header) return
    const until = /^\d{1,5}$/u.test(header) ? Date.now() + Number(header) * 1000 : Date.parse(header)
    if (Number.isFinite(until) && until > Date.now() && until - Date.now() <= 86400000) setRetryUntil(until)
  }
  useEffect(() => {
    const controller = new AbortController()
    void fetch(bookingPolicyEndpoint, { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(response => { if (!controller.signal.aborted && response.status === 429) respectRetry(response); return readBookingPolicy(response) })
      .then(value => {
        if (controller.signal.aborted) return
        setOriginal(value); setDraft(draftOf(value)); setLocked(false); setDenied(false); setError(''); setErrors({})
      }).catch((problem: unknown) => {
        if (controller.signal.aborted) return
        const status = problem instanceof BookingPolicyError ? problem.status : problem instanceof RequestVerificationError ? 400 : 500
        setDenied(status === 401 || status === 403)
        setError(status >= 500 ? 'Ayarlar yüklenemedi. Bağlantınızı kontrol edip yeniden deneyin. Ekranda kalan değerler güncel olarak doğrulanmadı.' : bookingPolicyFailure(status))
      }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [revision])
  useEffect(() => {
    if (loading || busy) return
    const key = policyFields.find(field => errors[field]) ?? (errors.requiresBusinessApproval ? 'requiresBusinessApproval' : undefined)
    if (key) form.current?.querySelector<HTMLInputElement>(`[name="${key}"]`)?.focus()
  }, [errors, loading, busy])
  function reload() {
    if (sending.current || loading || retryUntil || (dirty && !window.confirm('Güncel ayarlar yüklensin ve kaydedilmemiş değişiklikler silinsin mi?'))) return
    setLoading(true); setLocked(true); setError(''); setNotice(''); setRevision(value => value + 1)
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!original || !dirty || sending.current || loading || locked || denied || retryUntil) return
    const fields: PolicyErrors = {}
    for (const key of policyFields) {
      const [min, max] = policyLimits[key]
      if (!/^\d{1,5}$/u.test(draft[key]) || Number(draft[key]) < min || Number(draft[key]) > max) fields[key] = `Geçerli bir tam sayı girin (${min}–${max}).`
    }
    if (!fields.leadMinutes && !fields.horizonDays && Number(draft.leadMinutes) >= Number(draft.horizonDays) * 1440) fields.leadMinutes = 'Ön süre ileri rezervasyon süresinden kısa olmalı.'
    if (Object.keys(fields).length) { setErrors(fields); return }
    sending.current = true; setBusy(true); setError(''); setNotice(''); setErrors({})
    try {
      const response = await post(bookingPolicyEndpoint, { version: original.version, leadMinutes: Number(draft.leadMinutes), horizonDays: Number(draft.horizonDays), slotMinutes: Number(draft.slotMinutes), customerCancellationLeadMinutes: Number(draft.customerCancellationLeadMinutes), requiresBusinessApproval: draft.requiresBusinessApproval }, AbortSignal.timeout(15000))
      if (response.status === 400) {
        const fields = await policyFieldErrors(response)
        if (Object.keys(fields).length) { setErrors(fields); return }
      }
      if (response.status === 429) respectRetry(response)
      const saved = await readBookingPolicy(response)
      setOriginal(saved); setDraft(draftOf(saved)); onDirtyChange(false); setNotice('Rezervasyon ayarları kaydedildi.')
    } catch (problem: unknown) {
      const status = problem instanceof BookingPolicyError ? problem.status : problem instanceof RequestVerificationError ? 400 : 500
      setError(requestErrorMessage(problem, bookingPolicyFailure(status))); setLocked(status !== 400); setDenied(status === 401 || status === 403)
    } finally { sending.current = false; setBusy(false) }
  }
  return <div className={styles.editor}>
    {loading && <p role="status">Rezervasyon ayarları yükleniyor…</p>}
    <ErrorMessage message={error} />
    {!loading && error && !denied && <button type="button" className={styles.reload} disabled={busy || !!retryUntil} onClick={reload}>{original ? 'Güncel ayarları yükle' : 'Yeniden dene'}</button>}
    {notice && <p role="status">{notice}</p>}
    {original && !denied && <section className={styles.paper} aria-label="Rezervasyon kuralları">
      <p className={styles.context}>Zaman ayarları yeni randevularda ve taşımada uygulanır. İptal ve onay tercihi yeni çevrim içi rezervasyonlar içindir. Mevcut randevuların koşulları korunur.</p>
      <form ref={form} className={styles.form} aria-label="Rezervasyon ayarlarını düzenle" aria-busy={loading || busy} noValidate onSubmit={event => { void save(event) }}>
        {policyFields.map(key => <div className={styles.setting} key={key}>
          <div><label htmlFor={`policy-${key}`}>{labels[key].title}</label><p id={`policy-${key}-help`}>{labels[key].help}</p></div>
          <div><div className={styles.inputGroup}><input id={`policy-${key}`} name={key} type="number" inputMode="numeric" min={policyLimits[key][0]} max={policyLimits[key][1]} step="1" required value={draft[key]} disabled={busy || loading}
            aria-invalid={!!errors[key] || undefined} aria-describedby={`policy-${key}-help${key === 'slotMinutes' ? ' policy-example' : ''}${key === 'customerCancellationLeadMinutes' ? ' policy-cancellation-example' : ''}${errors[key] ? ` policy-${key}-error` : ''}`}
            onChange={event => { setDraft(current => ({ ...current, [key]: event.target.value })); setErrors({}); setNotice('') }} /><span className={styles.unit}>{labels[key].unit}</span></div>
            {key === 'slotMinutes' && <p className={styles.example} id="policy-example">{slotExample(draft.slotMinutes)}</p>}
            {key === 'customerCancellationLeadMinutes' && <p className={styles.example} id="policy-cancellation-example">{cancellationExample(draft.customerCancellationLeadMinutes)}</p>}
            {errors[key] && <p className={styles.fieldError} id={`policy-${key}-error`}>{errors[key]}</p>}
          </div>
        </div>)}
        <div className={styles.setting}>
          <div><span className={styles.groupTitle} id="policy-approval-title">Çevrim içi rezervasyon onayı</span><p id="policy-approval-help">Telefon doğrulaması ile işletme onayı farklı adımlardır.</p></div>
          <fieldset className={styles.approval} aria-labelledby="policy-approval-title" aria-describedby={`policy-approval-help${errors.requiresBusinessApproval ? ' policy-approval-error' : ''}`} aria-invalid={!!errors.requiresBusinessApproval || undefined} disabled={busy || loading}>
            {[true, false].map(required => <label className={styles.approvalOption} key={String(required)}>
              <input type="radio" name="requiresBusinessApproval" value={String(required)} checked={draft.requiresBusinessApproval === required} onChange={() => { setDraft(current => ({ ...current, requiresBusinessApproval: required })); setErrors({}); setNotice('') }} />
              <span><strong>{required ? 'İşletme onayı gerekir' : 'Telefon doğrulamasından sonra otomatik onay'}</strong><span>{required ? 'Telefon doğrulamasından sonra talep onayınızı bekler. 30 dakika içinde onaylanmazsa veya randevu başlarsa iptal edilir; saat serbest kalır.' : 'Talep, uygunluk yeniden kontrol edilerek onaylanır.'}</span></span>
            </label>)}
            {errors.requiresBusinessApproval && <p className={styles.fieldError} id="policy-approval-error">{errors.requiresBusinessApproval}</p>}
          </fieldset>
        </div>
        <p className={styles.cancellationNote}>Yakın bir randevu, müşteri iptal sınırının dışında kalabilir. Koşul müşteriye kayıt öncesinde gösterilir.</p>
        <div className={styles.footer}><p>Saatler İstanbul saatine göre değerlendirilir.</p><div className={styles.actions}>
          <button type="submit" disabled={busy || loading || locked || !!retryUntil || !dirty}>{busy ? 'Kaydediliyor…' : 'Ayarları kaydet'}</button>
          <span>{dirty ? 'Kaydedilmemiş değişiklikler var.' : 'Kaydedilmemiş değişiklik yok.'}</span>
        </div></div>
      </form>
    </section>}
  </div>
}
