import { useEffect, useRef, useState, type FormEvent } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import { MemberRequestError, memberFailure, readMember, type StaffMember, type StaffPost } from './staffMembersApi'
import styles from './StaffMembers.module.css'

type Props = {
  member: StaffMember | null; post: StaffPost; onSaved: () => void; onCancel: () => void
  onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void
}
export default function StaffMemberEditor({ member, post, onSaved, onCancel, onDirtyChange, onBusyChange }: Props) {
  const [original, setOriginal] = useState(member)
  const [id] = useState(() => member?.id ?? crypto.randomUUID())
  const [name, setName] = useState(member?.name ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [fieldError, setFieldError] = useState('')
  const [reloadRequired, setReloadRequired] = useState(false)
  const input = useRef<HTMLInputElement>(null)
  const sending = useRef(false)
  const dirty = name !== (original?.name ?? '')
  useEffect(() => { input.current?.focus() }, [])
  useEffect(() => { onDirtyChange(dirty) }, [dirty, onDirtyChange])
  useEffect(() => {
    if (!dirty) return
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = '' }
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])
  function pending(value: boolean) { sending.current = value; setBusy(value); onBusyChange(value) }
  function cancel() {
    if (dirty && !window.confirm('Kaydedilmemiş personel değişiklikleri silinsin mi?')) return
    onDirtyChange(false); onCancel()
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (sending.current || reloadRequired) return
    const trimmed = name.trim()
    if (!trimmed || trimmed.length > 100 || /\p{Cc}/u.test(trimmed)) {
      setFieldError('Ad soyad 1–100 karakter olmalı ve kontrol karakteri içermemeli.'); input.current?.focus(); return
    }
    pending(true); setError(''); setFieldError('')
    try {
      const response = await post(original ? `/api/staff-members/${id}` : '/api/staff-members/',
        original ? { name: trimmed, version: original.version } : { id, name: trimmed }, AbortSignal.timeout(15000))
      if (response.status === 400) { setFieldError(memberFailure(400)); input.current?.focus(); return }
      await readMember(response)
      onDirtyChange(false); onSaved()
    } catch (problem: unknown) {
      const status = problem instanceof MemberRequestError ? problem.status : 500
      setError(problem instanceof MemberRequestError ? problem.message : memberFailure(500))
      // Yeni kaydın aynı istek kimliğiyle tekrarı çift kayıt yaratmaz. Düzenlemede önce güncel sürüm gerekir.
      setReloadRequired(status === 409 || (original !== null && status >= 500))
    } finally { pending(false) }
  }
  async function reload() {
    if (sending.current || (dirty && !window.confirm('Güncel kayıt yüklensin ve kaydedilmemiş değişiklikler silinsin mi?'))) return
    pending(true); setError(''); setFieldError('')
    try {
      const current = await readMember(await fetch(`/api/staff-members/${id}`, { cache: 'no-store', signal: AbortSignal.timeout(15000) }))
      setOriginal(current); setName(current.name); setReloadRequired(false); onDirtyChange(false)
      input.current?.focus()
    } catch (problem: unknown) { setError(problem instanceof MemberRequestError ? problem.message : memberFailure(500)) }
    finally { pending(false) }
  }
  return <form className={styles.editor} aria-label={original ? 'Personel adını düzenle' : 'Personel ekle'} aria-busy={busy} onSubmit={event => { void save(event) }} noValidate>
    <h3>{original ? 'Personel adını düzenle' : 'Yeni personel'}</h3>
    <label htmlFor="member-name">Ad soyad (zorunlu)</label>
    <input id="member-name" ref={input} value={name} required maxLength={100} autoComplete="off" disabled={busy || reloadRequired}
      aria-invalid={!!fieldError || undefined} aria-describedby={fieldError ? 'member-field-error' : undefined}
      onChange={event => { setName(event.target.value); setFieldError('') }} />
    {fieldError && <p id="member-field-error" className={styles.fieldError}>{fieldError}</p>}
    <ErrorMessage message={error} />
    <div className={styles.actions}>
      <button type="submit" className={styles.primary} disabled={busy || reloadRequired || (!dirty && original !== null)}>{busy ? 'İşlem sürüyor…' : 'Kaydet'}</button>
      <button type="button" disabled={busy} onClick={cancel}>Vazgeç</button>
      {(reloadRequired || error) && <button type="button" disabled={busy} onClick={() => { void reload() }}>Güncel kaydı yükle</button>}
    </div>
  </form>
}
