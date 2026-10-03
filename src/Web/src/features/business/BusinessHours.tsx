import { useEffect, useRef, useState, type FormEvent } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import useUnsavedChanges from '../../app/useUnsavedChanges'
import { HoursError, hoursDraft, hoursFailure, hoursFieldErrors, readHours, retrySeconds, validHour, weekDays, type BusinessHours as Schedule, type HoursPost, type OpeningDay } from './businessHoursApi'
import common from '../../components/DefinitionManagement.module.css'
import styles from './BusinessHours.module.css'

type Props = { post: HoursPost; onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void }
export default function BusinessHours({ post, onDirtyChange, onBusyChange }: Props) {
  const [snapshot, setSnapshot] = useState<Schedule | null>(null), [days, setDays] = useState<OpeningDay[]>([])
  const [loading, setLoading] = useState(true), [busy, setBusy] = useState(false), [locked, setLocked] = useState(false)
  const [revision, setRevision] = useState(0), [error, setError] = useState(''), [notice, setNotice] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({}), [retry, setRetry] = useState(0)
  const form = useRef<HTMLFormElement>(null), sending = useRef(false), focusAfterLoad = useRef(false)
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
    void fetch('/api/business-hours/', { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(readHours).then(value => {
        if (controller.signal.aborted) return
        setSnapshot(value); setDays(hoursDraft(value)); setLocked(false); setErrors({}); setError('')
      }).catch((problem: unknown) => {
        if (!controller.signal.aborted) {
          setError(problem instanceof HoursError ? problem.message : hoursFailure(500))
          if (problem instanceof HoursError) setRetry(problem.retryAfter)
        }
      }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [revision])
  useEffect(() => {
    if (loading || busy) return
    const key = ['days', ...weekDays.flatMap((_, day) => [`day${day}Closed`, `day${day}OpensAt`, `day${day}ClosesAt`])].find(item => errors[item])
    if (key) form.current?.querySelector<HTMLElement>(`[data-field="${key}"]`)?.focus()
    else if (focusAfterLoad.current) { focusAfterLoad.current = false; form.current?.querySelector<HTMLInputElement>('input')?.focus() }
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
    if (!snapshot || sending.current || loading || locked || retry > 0) return
    const fields: Record<string, string> = {}
    for (const day of days) {
      if (day.isClosed) continue
      if (!validHour(day.opensAt)) fields[`day${day.day}OpensAt`] = 'Açılış saatini girin.'
      if (!validHour(day.closesAt)) fields[`day${day.day}ClosesAt`] = 'Kapanış saatini girin.'
      else if (validHour(day.opensAt) && day.closesAt <= day.opensAt) fields[`day${day.day}ClosesAt`] = 'Kapanış aynı gün içinde açılıştan sonra olmalı.'
    }
    if (Object.keys(fields).length) { setErrors(fields); return }
    sending.current = true; setBusy(true); setError(''); setNotice(''); setErrors({})
    try {
      const response = await post('/api/business-hours/', { version: snapshot.version, days }, AbortSignal.timeout(15000))
      if (response.status === 400) {
        const fields = await hoursFieldErrors(response)
        if (Object.keys(fields).length) setErrors(fields)
        else setError(hoursFailure(400))
        return
      }
      if (response.status === 429) { setRetry(retrySeconds(response)); setError(hoursFailure(429)); return }
      const saved = await readHours(response)
      setSnapshot(saved); setDays(hoursDraft(saved)); onDirtyChange(false); setNotice('İşletme saatleri kaydedildi.')
    } catch (problem: unknown) {
      const status = problem instanceof HoursError ? problem.status : 500
      setError(problem instanceof HoursError ? problem.message : hoursFailure(500))
      setLocked(status === 401 || status === 403 || status === 409 || status >= 500)
    } finally { sending.current = false; setBusy(false) }
  }
  return <section aria-labelledby="hours-title">
    <h2 id="hours-title">Haftalık açılış ve kapanış</h2>
    <p>Saatler Türkiye saatine göre kaydedilir (Europe/Istanbul). Açık günlerde tek saat aralığı kullanılır.</p>
    {loading && <p role="status">İşletme saatleri yükleniyor…</p>}
    {!loading && !snapshot && <p>Saatler yüklenemedi. Yeniden deneyebilirsiniz.</p>}
    {snapshot && <form ref={form} className={styles.form} aria-label="İşletme saatlerini düzenle" aria-busy={busy || loading} noValidate onSubmit={event => { void save(event) }}>
      {!snapshot.isConfigured && <p>Saatler henüz belirlenmedi. Aşağıdaki kapalı seçimleri kaydedilmiş saatler değildir; açık günleri ve saatlerini belirleyip haftayı kaydedin.</p>}
      <fieldset className={common.fields} disabled={busy || loading || locked || retry > 0}>
        <legend className={styles.legend} tabIndex={-1} data-field="days">Pazartesi–Pazar</legend>
        {errors.days && <p className={common.fieldError} role="alert">{errors.days}</p>}
        {days.map(day => <div key={day.day} className={styles.day}>
          <div className={styles.dayHeading}><strong>{weekDays[day.day]}</strong>
            <label className={styles.closed}><input type="checkbox" data-field={`day${day.day}Closed`} checked={day.isClosed}
              aria-label={`${weekDays[day.day]} kapalı`} aria-invalid={!!errors[`day${day.day}Closed`] || undefined}
              aria-describedby={errors[`day${day.day}Closed`] ? `hours-${day.day}-Closed-error` : undefined}
              onChange={event => change(day.day, { isClosed: event.target.checked, opensAt: null, closesAt: null })} />Kapalı</label>
          </div>
          {errors[`day${day.day}Closed`] && <p id={`hours-${day.day}-Closed-error`} className={common.fieldError}>{errors[`day${day.day}Closed`]}</p>}
          {!day.isClosed && <div className={styles.times}>
            {(['OpensAt', 'ClosesAt'] as const).map((field, i) => {
              const key = `day${day.day}${field}`, id = `hours-${day.day}-${field}`, value = i === 0 ? day.opensAt : day.closesAt
              return <div key={field}><label htmlFor={id}>{i === 0 ? 'Açılış' : 'Kapanış'} (zorunlu)</label>
                <input id={id} type="time" required step={60} data-field={key} aria-label={`${weekDays[day.day]} ${i === 0 ? 'açılış' : 'kapanış'}`}
                  value={value ?? ''} aria-invalid={!!errors[key] || undefined} aria-describedby={errors[key] ? `${id}-error` : undefined}
                  onChange={event => change(day.day, i === 0 ? { opensAt: event.target.value || null } : { closesAt: event.target.value || null })} />
                {errors[key] && <p id={`${id}-error`} className={common.fieldError}>{errors[key]}</p>}
              </div>
            })}
          </div>}
        </div>)}
      </fieldset>
      <div className={common.actions}><button type="submit" className={common.primary} disabled={busy || loading || locked || retry > 0 || (snapshot.isConfigured && !dirty)}>{busy ? 'Saatler kaydediliyor…' : 'Haftayı kaydet'}</button></div>
    </form>}
    <ErrorMessage message={error} />
    {retry > 0 && <p role="status">{retry} saniye sonra tekrar deneyebilirsiniz.</p>}
    {notice && <p role="status">{notice}</p>}
    <button type="button" disabled={busy || loading || retry > 0} onClick={reload}>Güncel saatleri yükle</button>
  </section>
}
