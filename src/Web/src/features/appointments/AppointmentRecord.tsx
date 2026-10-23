import { RequestVerificationError } from '../../app/api'
import { useCallback, useEffect, useRef, useState, type RefObject } from 'react'
import type { postWithCsrf } from '../../app/api'
import { formatTryPrice } from '../../app/money'
import { formatBusinessDateTime } from '../../app/format'
import ErrorMessage from '../../components/ErrorMessage'
import NavigationLink from '../../app/NavigationLink'
import AppointmentEditor from './AppointmentEditor'
import AppointmentReminder from './AppointmentReminder'
import { appointmentEndpoint, AppointmentError, businessDay, formatDay, formatTime, readAppointment, signal, statusLabels, type Appointment, type AppointmentStatus } from './appointmentsApi'
import styles from './Appointments.module.css'

type Props = { id: string; moving: boolean; initialDay: string; search: string; post: typeof postWithCsrf;
  onDirtyChange: (value: boolean) => void; onBusyChange: (value: boolean) => void; onUnauthorized: () => void;
  onSaved: (value: Appointment) => void; onCancel: () => void; headingRef?: RefObject<HTMLHeadingElement | null> }
type Action = { endpoint: string; label: string; target: Exclude<AppointmentStatus, 'Pending'> }
const actionDetails = {
  Confirmed: { label: 'Randevuyu onayla', message: 'Randevu onaylanacak. Ayrılan saat dolu kalır.' },
  Cancelled: { label: 'Randevuyu iptal et', message: 'Randevu iptal edilecek ve ayırdığı saat serbest kalacak. İptal edilen randevu yeniden açılamaz.' },
  Completed: { label: 'Tamamlandı olarak kaydet', message: 'Randevu tamamlandı olarak kaydedilecek. Geçmişteki zaman kaydı korunur; bu ekrandan durum yeniden değiştirilemez.' },
  NoShow: { label: 'Gelmedi olarak kaydet', message: 'Randevu gelmedi olarak kaydedilecek. Geçmişteki zaman kaydı korunur; bu ekrandan durum yeniden değiştirilemez.' },
}
type Attempt = { action: Action; payload: { version: string; requestKey: string } }
export default function AppointmentRecord({ id, moving, initialDay, search, post, onDirtyChange, onBusyChange, onUnauthorized, onSaved, onCancel, headingRef }: Props) {
  const [data, setData] = useState<Appointment | null>(null), [loading, setLoading] = useState(true), [revision, setRevision] = useState(0)
  const [error, setError] = useState(''), [notice, setNotice] = useState(''), [locked, setLocked] = useState(false)
  const [busy, setBusy] = useState(false), [editorBusy, setEditorBusy] = useState(false), [retryUntil, setRetryUntil] = useState(0)
  const [reminderBusy, setReminderBusy] = useState(false)
  const [selected, setSelected] = useState<Action | null>(null), [attempt, setAttempt] = useState<Attempt | null>(null), [now, setNow] = useState(Date.now)
  const sending = useRef(false)
  useEffect(() => { document.title = `${moving ? 'Randevuyu taşı' : data?.customerName ?? 'Randevu ayrıntısı'} · Randevu` }, [data?.customerName, moving])
  useEffect(() => { const timer = setInterval(() => setNow(Date.now()), 15000); return () => clearInterval(timer) }, [])
  useEffect(() => { onBusyChange(busy || editorBusy || reminderBusy); return () => onBusyChange(false) }, [busy, editorBusy, reminderBusy, onBusyChange])
  useEffect(() => { if (!retryUntil) return; const timer = setTimeout(() => setRetryUntil(0), Math.max(0, retryUntil - Date.now())); return () => clearTimeout(timer) }, [retryUntil])
  useEffect(() => {
    const controller = new AbortController()
    void fetch(appointmentEndpoint + id, { cache: 'no-store', signal: signal(controller) }).then(readAppointment).then(value => {
      if (value.id !== id) throw new AppointmentError(500)
      if (!controller.signal.aborted) { setData(value); setLocked(false); setSelected(null); setError('') }
    }).catch((problem: unknown) => {
      if (controller.signal.aborted) return
      const failure = problem instanceof AppointmentError ? problem : problem instanceof RequestVerificationError ? new AppointmentError(400, {}, 0, problem.message) : new AppointmentError(500)
      setError(failure.message); setLocked(!(problem instanceof RequestVerificationError)); setRetryUntil(failure.retryAfter)
      if (failure.status === 401 || failure.status === 403 || failure.status === 404) setData(null)
      if (failure.status === 401) onUnauthorized()
    }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [id, revision, onUnauthorized])
  const reload = useCallback(() => { setLoading(true); setLocked(true); setError(''); setNotice(''); setRevision(value => value + 1) }, [])
  async function change(command: Attempt) {
    if (!data || sending.current || retryUntil) return
    sending.current = true; setBusy(true); setError(''); setNotice(''); setAttempt(command)
    try {
      const saved = await readAppointment(await post(`${appointmentEndpoint}${id}/${command.action.endpoint}`, command.payload, AbortSignal.timeout(15000)))
      if (saved.id !== id) throw new AppointmentError(500)
      setData(saved); setLocked(false); setSelected(null); setAttempt(null)
      setNotice(`Randevu güncellendi. Güncel durum: ${statusLabels[saved.status]}.`)
    } catch (problem: unknown) {
      const failure = problem instanceof AppointmentError ? problem : problem instanceof RequestVerificationError ? new AppointmentError(400, {}, 0, problem.message) : new AppointmentError(500)
      setError(failure.message); setLocked(!(problem instanceof RequestVerificationError)); setRetryUntil(failure.retryAfter)
      if (failure.status < 500) setAttempt(null)
      if (failure.status === 401) onUnauthorized()
    } finally { sending.current = false; setBusy(false) }
  }
  const actions: Action[] = data?.status === 'Pending' && data.pendingExpiresAt && Date.parse(data.pendingExpiresAt) > now && Date.parse(data.startsAt) > now
    ? [{ endpoint: 'confirm', label: 'Onayla', target: 'Confirmed' }, { endpoint: 'cancel', label: 'İptal et', target: 'Cancelled' }]
    : data?.status === 'Confirmed' ? Date.parse(data.startsAt) > now ? [{ endpoint: 'cancel', label: 'İptal et', target: 'Cancelled' }]
      : [{ endpoint: 'complete', label: 'Tamamlandı', target: 'Completed' }, { endpoint: 'no-show', label: 'Gelmedi', target: 'NoShow' }] : []
  return <>
    <h1 ref={headingRef} tabIndex={-1}>{moving ? 'Randevuyu taşı' : data?.customerName ?? 'Randevu ayrıntısı'}</h1>
    {loading && <p role="status">Randevu bilgileri yükleniyor…</p>}
    <ErrorMessage message={error} />
    {notice && <p role="status">{notice}</p>}
    {!moving && (error || locked || data?.status === 'Pending' && data.pendingExpiresAt && Date.parse(data.pendingExpiresAt) <= now) &&
      <button type="button" disabled={busy || editorBusy || reminderBusy || loading || !!retryUntil} onClick={reload}>Güncel randevuyu yükle</button>}
    {moving && data ? <AppointmentEditor appointment={data} initialDay={initialDay} post={post} onSaved={onSaved} onDirtyChange={onDirtyChange}
      onBusyChange={setEditorBusy} onUnauthorized={onUnauthorized} onReload={reload} onCancel={onCancel} /> : data && <div className={styles.columns}>
      <section className={styles.paper} aria-label="Randevu bilgileri"><span className={styles.status} data-status={data.status}>{statusLabels[data.status]}</span>
        <dl className={styles.detailGrid}>
          <div><dt>Hizmet</dt><dd>{data.serviceName}</dd></div><div><dt>Çalışan</dt><dd>{data.staffName}</dd></div>
          <div><dt>Tarih</dt><dd>{formatDay(businessDay(new Date(data.startsAt)))}</dd></div><div><dt>Saat</dt><dd>{formatTime(data.startsAt)}–{formatTime(data.endsAt)} · {data.durationMinutes} dk</dd></div>
          <div><dt>Telefon</dt><dd>{data.customerPhone}</dd></div><div><dt>Hizmet bedeli</dt><dd>{formatTryPrice(data.price.toFixed(2))}</dd></div>
          {data.status === 'Pending' && data.pendingExpiresAt && <div><dt>Onay için son an</dt><dd>{formatBusinessDateTime(data.pendingExpiresAt)}</dd></div>}
        </dl>
      </section>
      <section className={styles.paper} aria-label="Randevu işlemleri"><h2>Randevu işlemleri</h2>
        {data.status === 'Confirmed' && Date.parse(data.startsAt) > now && <p>Randevu başlamadan önce zamanını değiştirebilir veya iptal edebilirsiniz.</p>}
        {!actions.length && <p>{data.status === 'Pending' ? 'Bekleme süresi dolmuş olabilir. Güncel randevuyu yükleyin.' : 'Bu randevuda durum değişikliği yapılamaz.'}</p>}
        <div className={styles.actions}>
          {data.status === 'Confirmed' && Date.parse(data.startsAt) > now && <NavigationLink to={`/yonetim/randevular/${id}/tasi${search}`} disabled={busy || reminderBusy || loading || locked || !!selected || !!attempt}>Randevuyu taşı</NavigationLink>}
          {actions.map(action => <button key={action.endpoint} type="button" className={action.endpoint === 'cancel' ? styles.danger : undefined}
            disabled={busy || reminderBusy || loading || locked || !!selected || !!attempt || !!retryUntil} onClick={() => setSelected(action)}>{action.label}</button>)}
        </div>
        {selected && !attempt && <div className={styles.confirmation}><p>{data.customerName} · {formatTime(data.startsAt)} randevusu “{statusLabels[selected.target]}” olarak kaydedilsin mi?</p>
          <p>{actionDetails[selected.target].message}</p>
          <div className={styles.actions}><button type="button" className={selected.endpoint === 'cancel' ? styles.danger : styles.primary} disabled={busy || reminderBusy || locked || loading || !!retryUntil}
            onClick={() => { void change({ action: selected, payload: { version: data.version, requestKey: crypto.randomUUID() } }) }}>{actionDetails[selected.target].label}</button>
            <button type="button" disabled={busy} onClick={() => setSelected(null)}>Vazgeç</button></div></div>}
        {attempt && !busy && <div className={styles.confirmation}><p>İşlemin sonucu belirsiz. Durum değişikliği kaydedilmiş olabilir. “Aynı işlemi yeniden dene” düğmesi aynı işlemi ikinci kez uygulamaz. İsterseniz “Güncel randevuyu yükle” ile sonucu kontrol edin.</p>
          <button type="button" disabled={!!retryUntil || busy || reminderBusy} onClick={() => { void change(attempt) }}>Aynı işlemi yeniden dene</button></div>}
        <AppointmentReminder key={`${data.version}:${revision}`} id={id} version={data.version} disabled={busy || loading || locked || !!selected || !!attempt}
          post={post} onBusyChange={setReminderBusy} onUnauthorized={onUnauthorized} onReload={reload} />
      </section>
    </div>}
    {!data && !loading && <button type="button" disabled={busy || !!retryUntil} onClick={reload}>Yeniden dene</button>}
  </>
}
