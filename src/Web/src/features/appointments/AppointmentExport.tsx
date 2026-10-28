import { useEffect, useId, useRef, useState, type FormEvent } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import { appointmentEndpoint, AppointmentError, isDay, readAppointmentWorkbook, signal } from './appointmentsApi'
import styles from './AppointmentExport.module.css'

type Props = { day: string; staff: string; disabled: boolean; onUnauthorized: () => void;
  onRateLimit: (until: number) => void; onDownloaded: () => void }

export default function AppointmentExport({ day, staff, disabled, onUnauthorized, onRateLimit, onDownloaded }: Props) {
  const id = useId(), popup = useRef<HTMLDivElement>(null), trigger = useRef<HTMLButtonElement>(null), first = useRef<HTMLInputElement>(null)
  const request = useRef<AbortController | null>(null)
  const [open, setOpen] = useState(false), [from, setFrom] = useState(day), [to, setTo] = useState(day)
  const [busy, setBusy] = useState(false), [error, setError] = useState('')
  useEffect(() => () => { request.current?.abort() }, [])
  useEffect(() => { if (open) first.current?.focus() }, [open])

  function cancelRequest() { request.current?.abort(); request.current = null; setBusy(false) }
  function close() { popup.current?.hidePopover(); trigger.current?.focus() }
  async function download(event: FormEvent) {
    event.preventDefault()
    if (request.current || disabled) return
    if (!isDay(from) || !isDay(to) || to < from || (Date.parse(to + 'T12:00:00Z') - Date.parse(from + 'T12:00:00Z')) / 86400000 >= 31) {
      setError('Başlangıç ve bitiş tarihlerini kontrol edin. En fazla 31 günlük bir aralık seçin.'); first.current?.focus(); return
    }
    const controller = new AbortController(); request.current = controller; setBusy(true); setError('')
    const query = new URLSearchParams({ from, to }); if (staff) query.set('staffMemberId', staff)
    try {
      const file = await readAppointmentWorkbook(await fetch(appointmentEndpoint + 'export?' + query, { cache: 'no-store', signal: signal(controller) }))
      if (controller.signal.aborted) return
      const url = URL.createObjectURL(file), link = document.createElement('a')
      link.href = url; link.download = `randevular_${from}_${to}.xlsx`; document.body.append(link)
      try { link.click() } finally { link.remove(); window.setTimeout(() => URL.revokeObjectURL(url), 10000) }
      onDownloaded(); close()
    } catch (problem: unknown) {
      if (controller.signal.aborted) return
      const failure = problem instanceof AppointmentError ? problem : new AppointmentError(500)
      setError(failure.fields.date ?? failure.message)
      if (failure.status === 401) onUnauthorized()
      if (failure.status === 429) onRateLimit(failure.retryAfter)
    } finally {
      if (request.current === controller) { request.current = null; setBusy(false) }
    }
  }

  return <>
    <button ref={trigger} type="button" popoverTarget={id} aria-controls={id} aria-expanded={open} disabled={disabled || busy}>Excel'e aktar</button>
    <div id={id} ref={popup} className={styles.panel} popover="auto" role="dialog" aria-label="Excel dosyası indir"
      onToggle={event => {
        const showing = event.newState === 'open'; setOpen(showing)
        if (showing) setError(''); else cancelRequest()
      }}>
      {open && <form onSubmit={event => { void download(event) }}>
        <fieldset disabled={disabled || busy}>
          <div className={styles.fields}>
            <div><label htmlFor={id + '-from'}>Başlangıç tarihi</label><input id={id + '-from'} ref={first} type="date" required min="0001-01-02" max="9999-12-30" value={from} onChange={event => setFrom(event.target.value)} /></div>
            <div><label htmlFor={id + '-to'}>Bitiş tarihi</label><input id={id + '-to'} type="date" required min="0001-01-02" max="9999-12-30" value={to} onChange={event => setTo(event.target.value)} /></div>
          </div>
        </fieldset>
        <p>{staff ? 'Seçili çalışan' : 'Tüm çalışanlar'} · En fazla 31 gün / 5.000 kayıt. Dosya ad ve telefon içerir.</p>
        <ErrorMessage message={error} />
        <div className={styles.actions}><button type="submit" disabled={disabled || busy}>{busy ? 'Hazırlanıyor…' : "Excel'i indir"}</button><button type="button" onClick={close}>Vazgeç</button></div>
      </form>}
    </div>
  </>
}
