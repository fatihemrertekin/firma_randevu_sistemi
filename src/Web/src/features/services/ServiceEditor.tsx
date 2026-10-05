import { useEffect, useRef, useState, type FormEvent } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import useUnsavedChanges from '../../app/useUnsavedChanges'
import { parseTryPrice } from '../../app/money'
import { ServiceRequestError, readFieldErrors, readService, serviceFailure, type Service, type ServicePost } from './servicesApi'
import styles from './Services.module.css'

type Props = {
  service: Service | null; post: ServicePost; onSaved: () => void; onCancel: () => void
  onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void
  disabled?: boolean; requiresReload?: boolean; onReloaded: (service: Service) => void
}
export default function ServiceEditor({ service, post, onSaved, onCancel, onDirtyChange, onBusyChange, disabled = false, requiresReload = false, onReloaded }: Props) {
  const [original, setOriginal] = useState(service)
  const [id] = useState(() => service?.id ?? crypto.randomUUID())
  const [name, setName] = useState(service?.name ?? '')
  const [duration, setDuration] = useState(service ? String(service.durationMinutes) : '')
  const [price, setPrice] = useState(service?.price.replace('.', ',') ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [reloadRequired, setReloadRequired] = useState(false)
  const form = useRef<HTMLFormElement>(null)
  const sending = useRef(false)
  const dirty = name !== (original?.name ?? '') || duration !== (original ? String(original.durationMinutes) : '') || price !== (original?.price.replace('.', ',') ?? '')
  const mustReload = reloadRequired || requiresReload
  useUnsavedChanges(dirty, onDirtyChange)
  useEffect(() => { form.current?.querySelector<HTMLInputElement>('input')?.focus() }, [])
  useEffect(() => {
    if (busy) return
    const first = ['name', 'durationMinutes', 'price'].find(field => fieldErrors[field])
    if (first) form.current?.querySelector<HTMLInputElement>(`[name="${first}"]`)?.focus()
  }, [fieldErrors, busy])
  function pending(value: boolean) { sending.current = value; setBusy(value); onBusyChange(value) }
  function showFieldErrors(errors: Record<string, string>) {
    setFieldErrors(errors)
    const first = ['name', 'durationMinutes', 'price'].find(field => errors[field])
    if (first) form.current?.querySelector<HTMLInputElement>(`[name="${first}"]`)?.focus()
  }
  function cancel() {
    if (dirty && !window.confirm('Kaydedilmemiş hizmet değişiklikleri silinsin mi?')) return
    onDirtyChange(false); onCancel()
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (sending.current || mustReload || disabled) return
    const trimmed = name.trim(), amount = parseTryPrice(price), errors: Record<string, string> = {}
    if (!trimmed || trimmed.length > 100 || /\p{Cc}/u.test(trimmed)) errors.name = 'Hizmet adı 1–100 karakter olmalı ve kontrol karakteri içermemeli.'
    if (!/^[0-9]{1,4}$/u.test(duration) || Number(duration) < 1 || Number(duration) > 1440) errors.durationMinutes = 'Süre 1–1440 arasında tam dakika olmalı.'
    if (amount === null) errors.price = 'Fiyat 0–999999,99 TL arasında, en fazla iki ondalık basamaklı olmalı.'
    if (Object.keys(errors).length) { showFieldErrors(errors); return }
    pending(true); setError(''); setFieldErrors({})
    try {
      const definition = { name: trimmed, durationMinutes: Number(duration), price: amount }
      const response = await post(original ? `/api/services/${id}` : '/api/services/',
        original ? { ...definition, version: original.version } : { ...definition, id }, AbortSignal.timeout(15000))
      if (response.status === 400) {
        const fields = await readFieldErrors(response)
        if (Object.keys(fields).length) showFieldErrors(fields)
        else setError(serviceFailure(400))
        return
      }
      await readService(response); onDirtyChange(false); onSaved()
    } catch (problem: unknown) {
      const status = problem instanceof ServiceRequestError ? problem.status : 500
      setError(problem instanceof ServiceRequestError ? problem.message : serviceFailure(500))
      setReloadRequired(status === 409 || (original !== null && status >= 500))
    } finally { pending(false) }
  }
  async function reload() {
    if (sending.current || (dirty && !window.confirm('Güncel hizmet yüklensin ve kaydedilmemiş değişiklikler silinsin mi?'))) return
    pending(true); setError(''); setFieldErrors({})
    try {
      const current = await readService(await fetch(`/api/services/${id}`, { cache: 'no-store', signal: AbortSignal.timeout(15000) }))
      if (current.id !== id) throw new ServiceRequestError(500)
      setOriginal(current); setName(current.name); setDuration(String(current.durationMinutes)); setPrice(current.price.replace('.', ','))
      setReloadRequired(false); onDirtyChange(false)
      onReloaded(current)
    } catch (problem: unknown) { setError(problem instanceof ServiceRequestError ? problem.message : serviceFailure(500)) }
    finally { pending(false) }
  }
  function changed(field: string) { setFieldErrors(current => { const next = { ...current }; delete next[field]; return next }) }
  return <form ref={form} className={styles.editor} aria-label={original ? 'Hizmeti düzenle' : 'Hizmet ekle'} aria-busy={busy} onSubmit={event => { void save(event) }} noValidate>
    <fieldset className={styles.fields} disabled={busy || mustReload || disabled}>
      <div className={`${styles.field} ${styles.nameField}`}>
      <label htmlFor="service-name">Hizmet adı (zorunlu)</label>
      <input id="service-name" name="name" value={name} required maxLength={100} autoComplete="off"
        aria-invalid={!!fieldErrors.name || undefined} aria-describedby={fieldErrors.name ? 'service-name-error' : undefined}
        onChange={event => { setName(event.target.value); changed('name') }} />
      {fieldErrors.name && <p id="service-name-error" className={styles.fieldError}>{fieldErrors.name}</p>}
      </div>
      <div className={styles.field}>
      <label htmlFor="service-duration">Süre (dakika, zorunlu)</label>
      <input id="service-duration" name="durationMinutes" inputMode="numeric" value={duration} required maxLength={4}
        aria-invalid={!!fieldErrors.durationMinutes || undefined} aria-describedby={fieldErrors.durationMinutes ? 'service-duration-error' : undefined}
        onChange={event => { setDuration(event.target.value); changed('durationMinutes') }} />
      {fieldErrors.durationMinutes && <p id="service-duration-error" className={styles.fieldError}>{fieldErrors.durationMinutes}</p>}
      </div>
      <div className={styles.field}>
      <label htmlFor="service-price">Fiyat (TL, zorunlu)</label>
      <input id="service-price" name="price" inputMode="decimal" value={price} required maxLength={9} placeholder="Örn. 350,00"
        aria-invalid={!!fieldErrors.price || undefined} aria-describedby={fieldErrors.price ? 'service-price-error' : 'service-price-help'}
        onChange={event => { setPrice(event.target.value); changed('price') }} />
      <p id="service-price-help">Ücretsiz hizmet için 0 yazın. En fazla iki ondalık basamak kullanın.</p>
      {fieldErrors.price && <p id="service-price-error" className={styles.fieldError}>{fieldErrors.price}</p>}
      </div>
    </fieldset>
    <ErrorMessage message={error} />
    <div className={styles.actions}>
      <button type="submit" className={styles.primary} disabled={busy || mustReload || disabled || (!dirty && original !== null)}>{busy ? 'İşlem sürüyor…' : 'Kaydet'}</button>
      <button type="button" disabled={busy || disabled} onClick={cancel}>Vazgeç</button>
    </div>
    {(original || error) && <button type="button" className={styles.reload} disabled={busy || disabled} onClick={() => { void reload() }}>Güncel kaydı yükle</button>}
  </form>
}
