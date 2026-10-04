import { useEffect, useRef, useState, type FormEvent } from 'react'
import ErrorMessage from './ErrorMessage'
import useUnsavedChanges from '../app/useUnsavedChanges'
import { HoursError, hoursDraft, hoursFailure, hoursFieldErrors, readHours, retrySeconds, validHour, weekDays, type BusinessHours as Schedule, type HoursPost, type OpeningDay } from '../app/weeklyHoursApi'
import styles from './WeeklyHoursEditor.module.css'

export type HoursSnapshot = Schedule & { caption?: string; readOnly?: boolean }
type Props = { endpoint: string; heading: string; formLabel: string; subject: string; closedLabel: string; startLabel: string; endLabel: string;
  readSnapshot?: (response: Response) => Promise<HoursSnapshot>; post: HoursPost; onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void; embedded?: boolean;
  onSaved?: () => void; onCancel?: () => void }
export default function WeeklyHoursEditor({ endpoint, heading, formLabel, subject, closedLabel, startLabel, endLabel, readSnapshot = readHours, post, onDirtyChange, onBusyChange, onSaved, onCancel, embedded }: Props) {
  const [snapshot, setSnapshot] = useState<HoursSnapshot | null>(null), [days, setDays] = useState<OpeningDay[]>([])
  const [loading, setLoading] = useState(true), [busy, setBusy] = useState(false), [locked, setLocked] = useState(false)
  const [revision, setRevision] = useState(0), [error, setError] = useState(''), [notice, setNotice] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({}), [retry, setRetry] = useState(0)
  const form = useRef<HTMLFormElement>(null), sending = useRef(false), focusAfterLoad = useRef(!!onCancel)
  const dirty = snapshot !== null && JSON.stringify(days) !== JSON.stringify(hoursDraft(snapshot))
  useUnsavedChanges(dirty, onDirtyChange)
  useEffect(() => { onBusyChange(busy || loading); return () => onBusyChange(false) }, [busy, loading, onBusyChange])
  useEffect(() => {
    if (!retry) return
    const timer = setTimeout(() => setRetry(value => Math.max(0, value - 1)), 1000)
    return () => clearTimeout(timer)
  }, [retry])
  useEffect(() => {
    const controller = new AbortController()
    void fetch(endpoint, { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(readSnapshot).then(value => {
        if (controller.signal.aborted) return
        setSnapshot(value); setDays(hoursDraft(value)); setLocked(false); setErrors({}); setError('')
      }).catch((problem: unknown) => {
        if (!controller.signal.aborted) {
          setError(hoursFailure(problem instanceof HoursError ? problem.status : 500, subject))
          if (problem instanceof HoursError) setRetry(problem.retryAfter)
        }
      }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [endpoint, readSnapshot, revision, subject])
  useEffect(() => {
    if (loading || busy) return
    const key = ['days', ...weekDays.flatMap((_, day) => [`day${day}Closed`, `day${day}OpensAt`, `day${day}ClosesAt`])].find(item => errors[item])
    if (key) form.current?.querySelector<HTMLElement>(`[data-field="${key}"]`)?.focus()
    else if (focusAfterLoad.current) { focusAfterLoad.current = false; (form.current?.querySelector<HTMLInputElement>('input:not(:disabled)') ?? form.current?.querySelector<HTMLElement>('legend'))?.focus() }
  }, [errors, loading, busy])
  function change(index: number, patch: Partial<OpeningDay>) {
    setDays(current => current.map(item => item.day === index ? { ...item, ...patch } : item))
    setNotice(''); setErrors({})
  }
  function reload() {
    if (sending.current || loading || retry > 0 || (dirty && !window.confirm('Güncel saatler yüklensin ve kaydedilmemiş değişiklikler silinsin mi?'))) return
    focusAfterLoad.current = true; setLoading(true); setError(''); setNotice(''); setRevision(value => value + 1)
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!snapshot || snapshot.readOnly || sending.current || loading || locked || retry > 0) return
    const fields: Record<string, string> = {}
    for (const day of days) {
      if (day.isClosed) continue
      if (!validHour(day.opensAt)) fields[`day${day.day}OpensAt`] = `${startLabel} saatini girin.`
      if (!validHour(day.closesAt)) fields[`day${day.day}ClosesAt`] = `${endLabel} saatini girin.`
      else if (validHour(day.opensAt) && day.closesAt <= day.opensAt) fields[`day${day.day}ClosesAt`] = `${endLabel} aynı gün içinde ${startLabel.toLocaleLowerCase('tr-TR')}tan sonra olmalı.`
    }
    if (Object.keys(fields).length) { setErrors(fields); return }
    sending.current = true; setBusy(true); setError(''); setNotice(''); setErrors({})
    try {
      const response = await post(endpoint, { version: snapshot.version, days }, AbortSignal.timeout(15000))
      if (response.status === 400) {
        const fields = await hoursFieldErrors(response)
        if (Object.keys(fields).length) setErrors(fields)
        else setError(hoursFailure(400))
        return
      }
      if (response.status === 429) { setRetry(retrySeconds(response)); setError(hoursFailure(429)); return }
      const saved = await readSnapshot(response)
      setSnapshot(saved); setDays(hoursDraft(saved)); onDirtyChange(false); setNotice(`${subject} kaydedildi.`); onSaved?.()
    } catch (problem: unknown) {
      const status = problem instanceof HoursError ? problem.status : 500
      setError(hoursFailure(status, subject))
      setLocked(status === 401 || status === 403 || status === 404 || status === 409 || status >= 500)
    } finally { sending.current = false; setBusy(false) }
  }
  function cancel() {
    if (sending.current || loading || (dirty && !window.confirm('Kaydedilmemiş çalışma saatleri silinsin mi?'))) return
    onDirtyChange(false); onCancel?.()
  }
  const Heading = onCancel ? 'h3' : 'h2'
  return <section className={styles.editor} aria-labelledby="hours-title">
    <Heading id="hours-title" className={onCancel && !embedded ? styles.heading : styles.screenReaderOnly}>{heading}</Heading>
    {snapshot?.caption && !embedded && <p className={styles.identity}><strong>{snapshot.caption}</strong></p>}
    <p className={styles.description}>Türkiye saati (Europe/Istanbul) · Her gün tek aralık.</p>
    {loading && <p role="status">{subject} yükleniyor…</p>}
    {!loading && !snapshot && <p>Saatler yüklenemedi. Yeniden deneyebilirsiniz.</p>}
    {snapshot && <form id="weekly-hours-form" ref={form} className={styles.form} aria-label={formLabel} aria-busy={busy || loading} noValidate onSubmit={event => { void save(event) }}>
      {!snapshot.isConfigured && <p>Saatler henüz belirlenmedi. Aşağıdaki seçimler kaydedilmiş saatler değildir; günleri ve saatlerini belirleyip haftayı kaydedin.</p>}
      {snapshot.readOnly && <p>Personel pasif. Kaydedilmiş saatler korunur; düzenlemek için personeli aktifleştirin.</p>}
      <fieldset className={styles.fields} disabled={snapshot.readOnly || busy || loading || locked || retry > 0}>
        <legend className={errors.days ? styles.legend : `${styles.legend} ${styles.screenReaderOnly}`} tabIndex={-1} data-field="days">Pazartesi–Pazar</legend>
        {errors.days && <p className={styles.fieldError} role="alert">{errors.days}</p>}
        <div className={styles.columns} aria-hidden="true"><span>Gün</span><span>Durum</span><span>{startLabel}</span><span>{endLabel}</span></div>
        {days.map(day => <div key={day.day} className={styles.day}>
          <div className={styles.dayHeading}><strong>{weekDays[day.day]}</strong>
            <label className={styles.closed}><input type="checkbox" data-field={`day${day.day}Closed`} checked={day.isClosed}
              aria-label={`${weekDays[day.day]} ${closedLabel.toLocaleLowerCase('tr-TR')}`} aria-invalid={!!errors[`day${day.day}Closed`] || undefined}
              aria-describedby={errors[`day${day.day}Closed`] ? `hours-${day.day}-Closed-error` : undefined}
              onChange={event => change(day.day, { isClosed: event.target.checked, opensAt: null, closesAt: null })} />{closedLabel}</label>
          </div>
          {errors[`day${day.day}Closed`] && <p id={`hours-${day.day}-Closed-error`} className={styles.dayError}>{errors[`day${day.day}Closed`]}</p>}
          {day.isClosed && <p className={styles.closedDay}>Saat girişi gerekmiyor</p>}
          {!day.isClosed && <div className={styles.times}>
            {(['OpensAt', 'ClosesAt'] as const).map((field, i) => {
              const key = `day${day.day}${field}`, id = `hours-${day.day}-${field}`, value = i === 0 ? day.opensAt : day.closesAt
              return <div key={field}><label htmlFor={id}>{i === 0 ? startLabel : endLabel} (zorunlu)</label>
                <input id={id} type="time" required step={60} data-field={key} aria-label={`${weekDays[day.day]} ${(i === 0 ? startLabel : endLabel).toLocaleLowerCase('tr-TR')}`}
                  value={value ?? ''} aria-invalid={!!errors[key] || undefined} aria-describedby={errors[key] ? `${id}-error` : undefined}
                  onChange={event => change(day.day, i === 0 ? { opensAt: event.target.value || null } : { closesAt: event.target.value || null })} />
                {errors[key] && <p id={`${id}-error`} className={styles.fieldError}>{errors[key]}</p>}
              </div>
            })}
          </div>}
        </div>)}
      </fieldset>
    </form>}
    {dirty && !error && <p className={styles.draft} role="status">Değişiklikler henüz kaydedilmedi.</p>}
    <ErrorMessage message={error} />
    {retry > 0 && <p role="status">{retry} saniye sonra tekrar deneyebilirsiniz.</p>}
    {notice && <p className={styles.notice} role="status">{notice}</p>}
    <div className={styles.actions}>
      {snapshot && !snapshot.readOnly && <button type="submit" form="weekly-hours-form" disabled={busy || loading || locked || retry > 0 || (snapshot.isConfigured && !dirty)}>{busy ? 'Saatler kaydediliyor…' : 'Haftayı kaydet'}</button>}
      <button type="button" disabled={busy || loading || retry > 0} onClick={reload}>Güncel saatleri yükle</button>
      {onCancel && <button type="button" disabled={busy || loading} onClick={cancel}>Listeye dön</button>}
    </div>
  </section>
}
