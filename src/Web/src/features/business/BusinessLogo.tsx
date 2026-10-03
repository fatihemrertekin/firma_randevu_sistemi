import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import useUnsavedChanges from '../../app/useUnsavedChanges'
import type { postWithCsrf } from '../../app/api'
import { logoEndpoint, LogoError, logoFailure, maxLogoBytes, readLogo, type Logo } from './businessLogoApi'
import styles from './BusinessLogo.module.css'

type Props = { post: typeof postWithCsrf; onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void; onSaved: () => void }
export default function BusinessLogo({ post, onDirtyChange, onBusyChange, onSaved }: Props) {
  const [logo, setLogo] = useState<Logo | null>(null), [preview, setPreview] = useState(''), [image, setImage] = useState('')
  const [loading, setLoading] = useState(true), [busy, setBusy] = useState(false), [locked, setLocked] = useState(false)
  const [revision, setRevision] = useState(0), [error, setError] = useState(''), [fieldError, setFieldError] = useState(''), [notice, setNotice] = useState('')
  const [retry, setRetry] = useState(0)
  const input = useRef<HTMLInputElement>(null), sending = useRef(false), reader = useRef<FileReader | null>(null), focusAfterLoad = useRef(false)
  useUnsavedChanges(!!image, onDirtyChange)
  useEffect(() => { onBusyChange(busy || loading); return () => onBusyChange(false) }, [busy, loading, onBusyChange])
  useEffect(() => () => reader.current?.abort(), [])
  useEffect(() => {
    if (!retry) return
    const timer = setTimeout(() => setRetry(value => Math.max(0, value - 1)), 1000)
    return () => clearTimeout(timer)
  }, [retry])
  useEffect(() => {
    const controller = new AbortController()
    void fetch(logoEndpoint, { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(readLogo).then(value => {
        if (controller.signal.aborted) return
        setLogo(value); setLocked(false); setImage(''); setPreview(''); setError(''); setFieldError('')
        if (input.current) input.current.value = ''
      }).catch((problem: unknown) => { if (!controller.signal.aborted) setError(logoFailure(problem instanceof LogoError ? problem.status : 500)) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [revision])
  useEffect(() => {
    if (busy || loading) return
    if (fieldError) input.current?.focus()
    else if (focusAfterLoad.current) { focusAfterLoad.current = false; input.current?.focus() }
  }, [fieldError, busy, loading])
  function choose(event: ChangeEvent<HTMLInputElement>) {
    reader.current?.abort(); setNotice(''); setFieldError('')
    const file = event.target.files?.[0]
    if (!file) return
    setImage(''); setPreview('')
    if (!['image/png', 'image/jpeg'].includes(file.type) || file.size === 0 || file.size > maxLogoBytes) {
      setFieldError('PNG veya JPEG seçin. Dosya en fazla 1 MB olmalı.'); event.target.value = ''; return
    }
    const next = new FileReader(); reader.current = next; setBusy(true)
    next.onload = () => {
      if (typeof next.result !== 'string' || !next.result.startsWith(`data:${file.type};base64,`)) {
        setFieldError('Dosya okunamadı. Yeniden seçin.'); return
      }
      setPreview(next.result); setImage(next.result.split(',')[1]); setError('')
    }
    next.onerror = () => setFieldError('Dosya okunamadı. Yeniden seçin.')
    next.onloadend = () => setBusy(false)
    next.readAsDataURL(file)
  }
  function reload() {
    if (busy || loading || retry || (image && !window.confirm('Güncel logo yüklensin ve kaydedilmemiş logo seçimi silinsin mi?'))) return
    focusAfterLoad.current = true; setLoading(true); setNotice(''); setRevision(value => value + 1)
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!logo || !image || busy || loading || locked || retry || sending.current) return
    sending.current = true; setBusy(true); setNotice(''); setError(''); setFieldError('')
    try {
      const response = await post(logoEndpoint, { version: logo.version, image }, AbortSignal.timeout(15000))
      if (response.status === 400 || response.status === 413) {
        let message = logoFailure(response.status)
        if (response.status === 400) {
          const problem: unknown = await response.json()
          if (typeof problem === 'object' && problem !== null && 'errors' in problem && typeof problem.errors === 'object' && problem.errors !== null &&
            'image' in problem.errors && Array.isArray(problem.errors.image) && typeof problem.errors.image[0] === 'string') message = problem.errors.image[0]
        }
        setFieldError(message); return
      }
      if (response.status === 429) {
        const wait = Number(response.headers.get('Retry-After'))
        setRetry(Number.isFinite(wait) && wait > 0 ? Math.min(120, Math.ceil(wait)) : 60); setError(logoFailure(429)); return
      }
      const saved = await readLogo(response)
      if (!saved.hasLogo) throw new LogoError(500)
      setLogo(saved); setImage(''); setPreview(''); if (input.current) input.current.value = ''
      onDirtyChange(false); setNotice('İşletme logosu kaydedildi.'); onSaved()
    } catch (problem: unknown) {
      setLocked(true); setError(logoFailure(problem instanceof LogoError ? problem.status : 500))
    } finally { sending.current = false; setBusy(false) }
  }
  return <section aria-labelledby="business-logo-title">
    <h2 id="business-logo-title">İşletme logosu</h2>
    <p>Logonuz giriş ve yönetim ekranında görünür. PNG veya JPEG; en fazla 1 MB ve 2048 × 2048 piksel. Büyük logolar oranı korunarak küçültülür.</p>
    {loading && <p role="status">Logo yükleniyor…</p>}
    {logo && <form className={styles.form} aria-label="İşletme logosunu düzenle" aria-busy={busy || loading} onSubmit={event => { void save(event) }}>
      {!logo.hasLogo && <p>Henüz işletme logosu yüklenmedi.</p>}
      {logo.imageUrl && <><p>Kaydedilmiş logo</p><img className={styles.preview} src={logo.imageUrl} alt="Kaydedilmiş işletme logosu" /></>}
      <label htmlFor="logo-file">{logo.hasLogo ? 'Yeni logo dosyası' : 'Logo dosyası'}</label>
      <input ref={input} className={styles.file} id="logo-file" type="file" accept="image/png,image/jpeg" onChange={choose}
        disabled={busy || loading || locked || retry > 0} aria-invalid={!!fieldError || undefined} aria-describedby={fieldError ? 'logo-file-help logo-file-error' : 'logo-file-help'} />
      <p id="logo-file-help">Dosya seçmek logoyu değiştirmez. Önizlemeyi kontrol edip kaydedin.</p>
      {preview && <><p>Yeni logo önizlemesi · henüz kaydedilmedi</p><img className={styles.preview} src={preview} alt="Yeni logo önizlemesi" /></>}
      {fieldError && <p id="logo-file-error" role="alert">{fieldError}</p>}
      <button type="submit" disabled={!image || busy || loading || locked || retry > 0}>{busy ? 'Logo hazırlanıyor / kaydediliyor…' : 'Logoyu kaydet'}</button>
    </form>}
    <ErrorMessage message={error} />
    {retry > 0 && <p role="status">{retry} saniye sonra tekrar deneyebilirsiniz.</p>}
    {notice && <p role="status">{notice}</p>}
    <button type="button" disabled={busy || loading || retry > 0} onClick={reload}>Güncel logoyu yükle</button>
  </section>
}
