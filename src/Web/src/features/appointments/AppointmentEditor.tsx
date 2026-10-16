import { useEffect, useRef, useState, type FormEvent } from 'react'
import type { postWithCsrf } from '../../app/api'
import useUnsavedChanges from '../../app/useUnsavedChanges'
import ErrorMessage from '../../components/ErrorMessage'
import { formatTryPrice } from '../../app/money'
import { readService, type Service } from '../services/servicesApi'
import AppointmentChoices from './AppointmentChoices'
import { appointmentEndpoint, AppointmentError, businessDay, formatDay, formatTime, isDay, readAppointment, readAvailability, signal, type Appointment, type Availability } from './appointmentsApi'
import styles from './Appointments.module.css'

type Attempt = { path: string; payload: Record<string, string> }
type Props = { appointment?: Appointment; initialDay: string; post: typeof postWithCsrf; onSaved: (value: Appointment) => void;
  onDirtyChange: (value: boolean) => void; onBusyChange: (value: boolean) => void; onUnauthorized: () => void;
  onReload: () => void; onCancel: () => void }
export default function AppointmentEditor({ appointment, initialDay, post, onSaved, onDirtyChange, onBusyChange, onUnauthorized, onReload, onCancel }: Props) {
  const [service, setService] = useState<Service | null>(null), [staff, setStaff] = useState(appointment?.staffMemberId ?? '')
  const [staffName, setStaffName] = useState(appointment?.staffName ?? ''), [day, setDay] = useState(appointment ? businessDay(new Date(appointment.startsAt)) : initialDay)
  const [startsAt, setStartsAt] = useState(''), [name, setName] = useState(''), [phone, setPhone] = useState('')
  const [slots, setSlots] = useState<Availability | null>(null), [slotsLoading, setSlotsLoading] = useState(!!appointment), [revision, setRevision] = useState(0)
  const [busy, setBusy] = useState(false), [locked, setLocked] = useState(false), [attempt, setAttempt] = useState<Attempt | null>(null)
  const [error, setError] = useState(''), [availabilityError, setAvailabilityError] = useState(''), [fields, setFields] = useState<Record<string, string>>({}), [retryUntil, setRetryUntil] = useState(0)
  const sending = useRef(false), form = useRef<HTMLFormElement>(null)
  const moving = !!appointment, serviceId = appointment?.serviceId ?? service?.id ?? ''
  const dirty = moving ? staff !== appointment.staffMemberId || day !== businessDay(new Date(appointment.startsAt)) || !!startsAt
    : !!service || !!staff || day !== initialDay || !!startsAt || !!name || !!phone
  const disabled = busy || !!attempt || !!retryUntil
  useUnsavedChanges(dirty || !!attempt, onDirtyChange)
  useEffect(() => { onBusyChange(busy); return () => onBusyChange(false) }, [busy, onBusyChange])
  useEffect(() => { if (!retryUntil) return; const timer = setTimeout(() => setRetryUntil(0), Math.max(0, retryUntil - Date.now())); return () => clearTimeout(timer) }, [retryUntil])
  useEffect(() => {
    if (!serviceId || !staff || !isDay(day)) return
    const controller = new AbortController(), query = new URLSearchParams({ staffMemberId: staff, date: day })
    if (!moving) query.set('serviceId', serviceId)
    const path = moving ? `${appointmentEndpoint}${appointment.id}/move-availability` : appointmentEndpoint + 'availability'
    void fetch(path + '?' + query, { cache: 'no-store', signal: signal(controller) })
      .then(response => readAvailability(response, moving)).then(value => {
        if (controller.signal.aborted) return
        if (moving ? value.version !== appointment.version : value.serviceVersion !== service?.version) throw new AppointmentError(409)
        setSlots(value); setLocked(false); setAvailabilityError('')
      }).catch((problem: unknown) => {
        if (controller.signal.aborted) return
        const failure = problem instanceof AppointmentError ? problem : new AppointmentError(500)
        setAvailabilityError(failure.fields.date ?? failure.message); setSlots(null); setLocked(true); setRetryUntil(failure.retryAfter)
        if (failure.status === 401) onUnauthorized()
      }).finally(() => { if (!controller.signal.aborted) setSlotsLoading(false) })
    return () => controller.abort()
  }, [serviceId, service?.version, staff, day, moving, appointment?.id, appointment?.version, revision, onUnauthorized])
  useEffect(() => {
    const key = Object.keys(fields)[0]
    if (key) form.current?.querySelector<HTMLInputElement>(`[name="${key}"]`)?.focus()
  }, [fields])
  function changed() { setStartsAt(''); setSlots(null); setSlotsLoading(true); setError(''); setAvailabilityError(''); setFields({}); setLocked(false) }
  async function reload() {
    if (sending.current || busy || attempt || retryUntil) return
    sending.current = true; setBusy(true); setError(''); setAvailabilityError(''); setStartsAt(''); setSlots(null)
    try {
      if (moving) onReload()
      else if (serviceId) {
        const current = await readService(await fetch(`/api/services/${serviceId}`, { cache: 'no-store', signal: AbortSignal.timeout(15000) }))
        setService(current)
      }
      setSlotsLoading(true); setRevision(value => value + 1)
    } catch (problem: unknown) { setError(problem instanceof Error ? problem.message : 'Güncel bilgiler alınamadı.') }
    finally { sending.current = false; setBusy(false) }
  }
  async function send(pending: Attempt) {
    if (sending.current || retryUntil) return
    sending.current = true; setBusy(true); setError(''); setFields({}); setAttempt(pending)
    try {
      const saved = await readAppointment(await post(pending.path, pending.payload, AbortSignal.timeout(15000)))
      if (saved.serviceId !== serviceId || (appointment && saved.id !== appointment.id)) throw new AppointmentError(500)
      setAttempt(null); onDirtyChange(false); onSaved(saved)
    } catch (problem: unknown) {
      const failure = problem instanceof AppointmentError ? problem : new AppointmentError(500)
      setError(failure.message); setFields(failure.fields); setRetryUntil(failure.retryAfter)
      if (failure.status < 500) setAttempt(null)
      if (failure.status === 409 || failure.status === 404 || failure.status === 403) { setLocked(true); setSlots(null); setStartsAt('') }
      if (failure.status === 401) onUnauthorized()
    } finally { sending.current = false; setBusy(false) }
  }
  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (disabled || locked || slotsLoading || sending.current) return
    const errors: Record<string, string> = {}
    if (!serviceId) errors.serviceId = 'Hizmet seçin.'
    if (!staff) errors.staffMemberId = 'Çalışan seçin.'
    if (!slots?.startsAt.includes(startsAt)) errors.startsAt = 'Güncel uygun saatlerden birini seçin.'
    if (!moving && (name.trim().length < 2 || name.trim().length > 100 || /\p{Cc}/u.test(name))) errors.customerName = 'Ad 2–100 karakter olmalı.'
    if (!moving && !phone.trim()) errors.customerPhone = 'Telefon girin.'
    if (Object.keys(errors).length) { setFields(errors); return }
    const payload: Record<string, string> = moving ? { version: appointment.version, requestKey: crypto.randomUUID(), staffMemberId: staff, startsAt }
      : { requestKey: crypto.randomUUID(), serviceId, serviceVersion: service?.version ?? '', staffMemberId: staff, startsAt, customerName: name.trim(), customerPhone: phone }
    void send({ path: moving ? `${appointmentEndpoint}${appointment.id}/move` : appointmentEndpoint, payload })
  }
  const price = appointment ? appointment.price.toFixed(2) : service?.price
  const duration = appointment?.durationMinutes ?? service?.durationMinutes
  const fieldError = (key: string) => fields[key] && <p className={styles.fieldError} id={`appointment-${key}-error`}>{fields[key]}</p>
  return <>
    <ErrorMessage message={error} />
    {attempt && !busy && <div className={styles.confirmation}><p>İşlemin sonucu belirsiz. {moving ? 'Taşıma tamamlanmış olabilir. “Aynı işlemi yeniden dene” düğmesini kullanın; aynı işlem ikinci bir taşıma oluşturmaz. Sayfayı kapatırsanız tekrar taşımadan önce günlük listeden randevunun güncel saatini kontrol edin.' : 'Randevu kaydedilmiş olabilir. Bilgileri değiştirmeden “Aynı işlemi yeniden dene” düğmesini kullanın; aynı işlem ikinci bir randevu oluşturmaz. Sayfayı kapatırsanız yeniden kayıt oluşturmadan önce günlük listeyi kontrol edin.'}</p>
      <button type="button" disabled={busy || !!retryUntil} onClick={() => { void send(attempt) }}>Aynı işlemi yeniden dene</button></div>}
    {(error || availabilityError) && !attempt && <button type="button" disabled={busy || !!retryUntil} onClick={() => { void reload() }}>Güncel bilgileri ve saatleri yükle</button>}
    <div className={styles.columns}>
      <form ref={form} className={styles.paper} noValidate aria-label={moving ? 'Randevuyu taşı' : 'Yeni randevu'} aria-busy={busy} onSubmit={save}>
        <h2>{moving ? 'Yeni zaman' : 'Hizmet ve zaman'}</h2>
        <fieldset className={styles.fields} disabled={disabled}>
          {!moving && <div className={styles.wide}><AppointmentChoices kind="services" id="appointment-serviceId" fieldName="serviceId" invalid={!!fields.serviceId} label="Hizmet" value={serviceId} disabled={disabled}
            onUnauthorized={onUnauthorized} onChange={item => { changed(); setService(item && 'durationMinutes' in item ? item : null); setStaff(''); setStaffName('') }} />{fieldError('serviceId')}</div>}
          <div><AppointmentChoices kind="staff" id="appointment-staffMemberId" fieldName="staffMemberId" invalid={!!fields.staffMemberId} label="Çalışan" value={staff} disabled={disabled || !serviceId}
            onUnauthorized={onUnauthorized} onChange={item => { changed(); setStaff(item?.id ?? ''); setStaffName(item?.name ?? '') }} />{fieldError('staffMemberId')}</div>
          <div className={styles.field}><label htmlFor="appointment-date">Tarih</label><input id="appointment-date" type="date" min="0001-01-02" max="9999-12-30" value={day}
            onChange={event => { changed(); setDay(event.target.value) }} /></div>
          <div className={styles.wide}><fieldset className={styles.slots} disabled={disabled || slotsLoading || locked}><legend>Uygun başlangıç saati</legend>
            {!serviceId || !staff ? <p>Önce hizmet ve çalışan seçin.</p> : !isDay(day) ? <p>Geçerli tarih seçin.</p> : slotsLoading ? <p role="status">Uygun saatler yükleniyor…</p>
              : availabilityError ? <div className={styles.availabilityNotice} role="status"><strong>Uygun saatler alınamadı</strong><p>{availabilityError}</p></div>
                : slots?.startsAt.length === 0 ? <div className={styles.availabilityNotice} role="status"><strong>Bu seçimle uygun saat bulunamıyor</strong>
                  <ul>{slots.unavailableReasons.map(reason => <li key={reason.code}>{reason.message}</li>)}</ul></div> : null}
            <div className={styles.slotOptions}>{slots?.startsAt.map(start => <label className={styles.slot} key={start}><input type="radio" name="startsAt" value={start} checked={startsAt === start}
              aria-invalid={!!fields.startsAt || undefined} aria-describedby={fields.startsAt ? 'appointment-startsAt-error' : undefined} onChange={() => { setStartsAt(start); setFields({}) }} />{formatTime(start)}</label>)}</div>
            {fieldError('startsAt')}
          </fieldset></div>
        </fieldset>
        {!moving && <><h2 className={styles.sectionTitle}>Müşteri bilgileri</h2><fieldset className={styles.fields} disabled={disabled}>
          <div className={styles.field}><label htmlFor="appointment-customerName">Ad soyad</label><input id="appointment-customerName" name="customerName" autoComplete="off" maxLength={100} value={name}
            aria-invalid={!!fields.customerName || undefined} aria-describedby={fields.customerName ? 'appointment-customerName-error' : undefined} onChange={event => { setName(event.target.value); setFields({}) }} />{fieldError('customerName')}</div>
          <div className={styles.field}><label htmlFor="appointment-customerPhone">Telefon</label><input id="appointment-customerPhone" name="customerPhone" type="tel" autoComplete="off" maxLength={32} value={phone}
            aria-invalid={!!fields.customerPhone || undefined} aria-describedby={fields.customerPhone ? 'appointment-customerPhone-error' : undefined} onChange={event => { setPhone(event.target.value); setFields({}) }} />{fieldError('customerPhone')}</div>
        </fieldset></>}
        <div className={styles.actions}><button type="submit" disabled={disabled || locked || slotsLoading || !slots || !startsAt}>{busy ? 'Kaydediliyor…' : moving ? 'Yeni zamanı kaydet' : 'Randevuyu kaydet'}</button>
          <button type="button" disabled={busy} onClick={onCancel}>Vazgeç</button></div>
      </form>
      <aside className={styles.paper}><h2>{moving ? 'Mevcut randevu' : 'Randevu özeti'}</h2><dl className={styles.summary}>
        {appointment && <div><dt>Müşteri</dt><dd>{appointment.customerName}</dd></div>}
        <div><dt>Hizmet</dt><dd>{appointment?.serviceName ?? service?.name ?? 'Seçilmedi'}{duration ? ` · ${duration} dk` : ''}</dd></div>
        <div><dt>Çalışan</dt><dd>{moving ? appointment.staffName : staffName || 'Seçilmedi'}</dd></div>
        <div><dt>Tarih ve saat</dt><dd>{moving ? `${formatDay(businessDay(new Date(appointment.startsAt)))} · ${formatTime(appointment.startsAt)}–${formatTime(appointment.endsAt)}`
          : startsAt && duration ? `${formatDay(day)} · ${formatTime(startsAt)}–${formatTime(new Date(Date.parse(startsAt) + duration * 60000).toISOString())}` : 'Saat seçilmedi'}</dd></div>
        {price && <div><dt>Hizmet bedeli</dt><dd>{formatTryPrice(price)}</dd></div>}
      </dl>{moving && <p className={styles.sectionTitle}>Taşıma onaylanana kadar mevcut randevu korunur.</p>}</aside>
    </div>
  </>
}
