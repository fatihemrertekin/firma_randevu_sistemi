import { useCallback, useEffect, useRef, useState } from 'react'
import { RequestVerificationError, type postWithCsrf } from '../../app/api'
import { formatBusinessDateTime } from '../../app/format'
import ErrorMessage from '../../components/ErrorMessage'
import { appointmentEndpoint, AppointmentError, readReminder, readReminderDisabled, signal, type Reminder } from './appointmentsApi'
import styles from './Appointments.module.css'

type Props = { id: string; version: string; disabled: boolean; post: typeof postWithCsrf;
  onBusyChange: (value: boolean) => void; onUnauthorized: () => void; onReload: () => void }

export default function AppointmentReminder({ id, version, disabled, post, onBusyChange, onUnauthorized, onReload }: Props) {
  const [data, setData] = useState<Reminder | null>(null), [loading, setLoading] = useState(true), [busy, setBusy] = useState(false)
  const [error, setError] = useState(''), [notice, setNotice] = useState(''), [selected, setSelected] = useState(false)
  const [uncertain, setUncertain] = useState(false), [blocked, setBlocked] = useState(false), [retryUntil, setRetryUntil] = useState(0)
  const sending = useRef(false), lifetime = useRef(new AbortController()), trigger = useRef<HTMLButtonElement>(null)
  const confirmation = useRef<HTMLParagraphElement>(null), status = useRef<HTMLParagraphElement>(null)
  const returningFocus = useRef(false)
  const read = useCallback(async (requestSignal: AbortSignal) => {
    const value = await readReminder(await fetch(`${appointmentEndpoint}${id}/reminder`, { cache: 'no-store', signal: requestSignal }))
    if (value.appointmentVersion !== version) throw new AppointmentError(409)
    return value
  }, [id, version])
  const failure = useCallback((problem: unknown, verifying = false) => {
    const value = problem instanceof AppointmentError ? problem : new AppointmentError(500)
    setError(problem instanceof RequestVerificationError ? problem.message : verifying && value.status >= 500
      ? 'Sonuç doğrulanamadı. Hatırlatma durumunu yeniden yükleyin; tercih kaydedilmiş olabilir.'
      : value.status >= 500 ? 'Hatırlatma bilgisi alınamadı. Yeniden yükleyin.' : value.message)
    setBlocked([401, 403, 404, 409].includes(value.status)); setRetryUntil(value.retryAfter)
    if (value.status === 401) onUnauthorized()
  }, [onUnauthorized])
  useEffect(() => {
    const controller = new AbortController(); lifetime.current = controller
    void read(signal(controller)).then(value => { if (!controller.signal.aborted) setData(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) failure(problem) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [read, failure])
  useEffect(() => { onBusyChange(busy); return () => onBusyChange(false) }, [busy, onBusyChange])
  useEffect(() => {
    if (selected) confirmation.current?.focus()
    else if (returningFocus.current) { trigger.current?.focus(); returningFocus.current = false }
  }, [selected])
  useEffect(() => { if (notice) status.current?.focus() }, [notice])
  useEffect(() => { if (!retryUntil) return; const timer = setTimeout(() => setRetryUntil(0), Math.max(0, retryUntil - Date.now())); return () => clearTimeout(timer) }, [retryUntil])

  async function run(write: boolean) {
    if (sending.current || disabled || retryUntil || blocked && write) return
    const controller = lifetime.current
    sending.current = true; setBusy(true); setError(''); setNotice('')
    let verifying = uncertain
    try {
      if (write) {
        try { await readReminderDisabled(await post(`${appointmentEndpoint}${id}/reminder/disable`, { version }, signal(controller))) }
        catch (problem: unknown) {
          if (problem instanceof RequestVerificationError || problem instanceof AppointmentError && problem.status < 500) throw problem
          // A lost response may follow a committed write; read the preference before offering the same request again.
        }
        verifying = true
      }
      const value = await read(signal(controller))
      if (controller.signal.aborted) return
      setData(value); setBlocked(false); setSelected(false); setUncertain(verifying && !value.suppressed)
      if (verifying && !value.suppressed) setError('Sonuç doğrulanamadı. Hatırlatma durumunu yeniden yükleyin veya aynı kapatma isteğini yeniden deneyin.')
      if (verifying && value.suppressed) setNotice('Bu randevunun hatırlatması kapalı.')
    } catch (problem: unknown) {
      if (controller.signal.aborted) return
      setData(null); setUncertain(verifying); failure(problem, verifying)
    } finally {
      sending.current = false
      if (!controller.signal.aborted) { setBusy(false); setLoading(false) }
    }
  }
  const current = data?.appointmentVersion === version ? data : null
  const locked = disabled || busy || loading || !!retryUntil
  function cancel() { returningFocus.current = true; setSelected(false) }
  return <section aria-labelledby="appointment-reminder-title" aria-busy={loading || busy} onKeyDown={event => {
    if (event.key === 'Escape' && selected && !busy) { event.stopPropagation(); cancel() }
  }}>
    <h3 id="appointment-reminder-title" className={styles.sectionTitle}>SMS hatırlatması</h3>
    {loading && <p role="status">Hatırlatma bilgisi yükleniyor…</p>}
    {busy && <p role="status">Hatırlatma bilgisi doğrulanıyor…</p>}
    <ErrorMessage message={error} />
    {notice && <p role="status" ref={status} tabIndex={-1}>{notice}</p>}
    {current && <>
      {!current.dispatchEnabled && <p>Gönderim bu kurulumda kapalı.</p>}
      {current.hasStarted && <p>Gönderim başlatılmış. Telefona teslim durumu bu ekranda doğrulanmıyor; başlamış SMS geri alınamayabilir.</p>}
      {current.suppressed ? !notice && <p role="status">Bu randevunun hatırlatması kapalı.</p> : <>
        {current.scheduledFor && <p>Planlanan gönderim: {formatBusinessDateTime(current.scheduledFor)}. Gönderim koşulları işlem anında doğrulanır.</p>}
        <p>Bu işlem yalnız bu randevuyu etkiler.</p>
        {!selected && !uncertain && <button type="button" ref={trigger} disabled={locked || blocked} onClick={() => setSelected(true)}>Bu randevuda hatırlatmayı kapat</button>}
      </>}
    </>}
    {selected && !uncertain && <div className={styles.confirmation}>
      <p ref={confirmation} tabIndex={-1}>Bu randevunun SMS hatırlatması kapatılsın mı?</p>
      <p>Randevu ve diğer randevular değişmez. Gönderim başladıysa SMS geri alınamayabilir. Bu ekrandan yeniden açılamaz.</p>
      <div className={styles.actions}><button type="button" className={styles.primary} disabled={locked || blocked} onClick={() => { void run(true) }}>Hatırlatmayı kapat</button>
        <button type="button" disabled={busy} onClick={cancel}>Vazgeç</button></div>
    </div>}
    {(error || uncertain) && <div className={styles.actions}>
      <button type="button" disabled={locked} onClick={() => { void run(false) }}>Hatırlatma durumunu yeniden yükle</button>
      {blocked && <button type="button" disabled={locked} onClick={onReload}>Güncel randevuyu yükle</button>}
      {uncertain && !blocked && <button type="button" disabled={locked} onClick={() => { void run(true) }}>Aynı kapatma isteğini yeniden dene</button>}
    </div>}
  </section>
}
