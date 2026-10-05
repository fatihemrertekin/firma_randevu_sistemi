import { useEffect, useRef, useState } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import { formatTryPrice } from '../../app/money'
import ServiceEditor from './ServiceEditor'
import { ServiceRequestError, readService, readServicePage, serviceFailure, type Service, type ServicePage, type ServicePost } from './servicesApi'
import styles from '../../components/DefinitionManagement.module.css'

type Props = { post: ServicePost; onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void }
export default function Services({ post, onDirtyChange, onBusyChange }: Props) {
  const [page, setPage] = useState(1)
  const [revision, setRevision] = useState(0)
  const [data, setData] = useState<ServicePage | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [editing, setEditing] = useState<{ service: Service | null } | null>(null)
  const [target, setTarget] = useState<Service | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [busy, setBusy] = useState(false)
  const [stale, setStale] = useState(false)
  const sending = useRef(false)
  const heading = useRef<HTMLHeadingElement>(null)
  const confirm = useRef<HTMLButtonElement>(null)
  const addButton = useRef<HTMLButtonElement>(null)
  const opener = useRef<HTMLButtonElement | null>(null)
  const restoreFocus = useRef(false)
  useEffect(() => {
    if (target) confirm.current?.focus()
    else if (!editing && restoreFocus.current) {
      restoreFocus.current = false
      if (opener.current?.isConnected) opener.current.focus()
      else addButton.current?.focus()
    }
  }, [target, editing])
  useEffect(() => {
    const controller = new AbortController()
    void fetch(`/api/services/?page=${page}`, { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(readServicePage).then(value => { if (!controller.signal.aborted) setData(value) })
      .catch((problem: unknown) => {
        if (!controller.signal.aborted) { setData(null); setError(problem instanceof ServiceRequestError ? problem.message : 'Hizmet listesi alınamadı. Yeniden dene.') }
      }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [page, revision])
  function load(next = page) {
    setLoading(true); setError(''); setTarget(null); setStale(false)
    setPage(next); setRevision(value => value + 1)
  }
  function closeEditor() { restoreFocus.current = true; setEditing(null) }
  async function changeStatus() {
    if (!target || sending.current || stale) return
    sending.current = true; setBusy(true); onBusyChange(true); setError(''); setNotice('')
    try {
      if (deleting) {
        const response = await post(`/api/services/${target.id}/delete`, { version: target.version }, AbortSignal.timeout(15000))
        if (response.status !== 204) throw new ServiceRequestError(response.ok ? 500 : response.status)
        setNotice(`${target.name} hizmet listesinden silindi.`); load(1); heading.current?.focus(); return
      }
      const updated = await readService(await post(`/api/services/${target.id}/status`,
        { isActive: !target.isActive, version: target.version }, AbortSignal.timeout(15000)))
      setNotice(`${updated.name} ${updated.isActive ? 'aktifleştirildi' : 'pasifleştirildi'}.`)
      load(); heading.current?.focus()
    } catch (problem: unknown) { setError(problem instanceof ServiceRequestError ? problem.message : serviceFailure(500)); setStale(true) }
    finally { sending.current = false; setBusy(false); onBusyChange(false) }
  }
  const blocked = busy || loading || editing !== null || target !== null
  return <section aria-labelledby="services-title" aria-busy={loading || busy}>
    <h2 id="services-title" ref={heading} className={styles.heading} tabIndex={-1}>Hizmet listesi</h2>
    <p>İşletmenin sunduğu hizmetlerin adını, süresini ve fiyatını düzenleyin. Pasifleştirme kaydı korur; silme listeden kaldırır ve değişiklik geçmişini korur.</p>
    <ErrorMessage message={error} />
    {notice && <p role="status">{notice}</p>}
    {loading && <><p role="status">Hizmetler yükleniyor…</p>{!data && <div aria-hidden="true" className={styles.skeleton} />}</>}
    {!loading && data?.items.length === 0 && <p>Bu sayfada hizmet yok. Yeni hizmet ekleyerek başlayabilirsin.</p>}
    {!editing && !target && <button type="button" ref={addButton} className={styles.primary} disabled={busy || loading} onClick={event => {
      opener.current = event.currentTarget; setEditing({ service: null }); setError(''); setNotice('')
    }}>Yeni hizmet</button>}
    {editing && <ServiceEditor service={editing.service} post={post} onDirtyChange={onDirtyChange} onBusyChange={onBusyChange}
      onCancel={closeEditor} onSaved={() => { setEditing(null); setNotice('Hizmet kaydedildi.'); load(1); heading.current?.focus() }} />}
    {data && <ul className={styles.list}>{data.items.map(service => <li key={service.id} className={styles.row}>
      <div className={styles.identity}><strong>{service.name}</strong><p>{service.durationMinutes} dk · {formatTryPrice(service.price)}</p>
        <p><span aria-hidden="true">{service.isActive ? '● ' : '○ '}</span>{service.isActive ? 'Aktif' : 'Pasif'}</p></div>
      <div className={styles.actions}>
        <button type="button" disabled={blocked} aria-label={`${service.name} hizmetini düzenle`} onClick={event => {
          opener.current = event.currentTarget; setEditing({ service }); setError(''); setNotice('')
        }}>Düzenle</button>
        <button type="button" className={service.isActive ? styles.danger : undefined} disabled={blocked}
          aria-label={`${service.name} hizmetini ${service.isActive ? 'pasifleştir' : 'aktifleştir'}`} onClick={event => {
            opener.current = event.currentTarget; setDeleting(false); setTarget(service); setStale(false); setError(''); setNotice('')
          }}>{service.isActive ? 'Pasifleştir' : 'Aktifleştir'}</button>
        <button type="button" className={styles.danger} disabled={blocked} aria-label={`${service.name} hizmetini sil`} onClick={event => {
          opener.current = event.currentTarget; setDeleting(true); setTarget(service); setStale(false); setError(''); setNotice('')
        }}>Sil</button>
      </div>
    </li>)}</ul>}
    {target && <fieldset className={styles.confirmation} disabled={busy} aria-label={deleting ? 'Hizmet silme onayı' : 'Hizmet durum değişikliği onayı'}>
      <legend>Hizmeti {deleting ? 'sil' : target.isActive ? 'pasifleştir' : 'aktifleştir'}</legend>
      {deleting ? <p className={styles.identity}><strong>{target.name}</strong> hizmet listesinden ve personelin hizmet seçimlerinden kaldırılacak; yeniden kullanılamayacak. Önceki bağlantılar ve değişiklik geçmişi korunur.</p>
        : <p className={styles.identity}><strong>{target.name}</strong> {target.isActive ? 'pasif' : 'aktif'} olarak işaretlenecek. Kayıt silinmez; hizmetin adı, süresi ve fiyatı korunur.</p>}
      <div className={styles.actions}><button type="button" className={deleting || target.isActive ? styles.confirm : styles.primary} ref={confirm} disabled={stale}
        onClick={() => { void changeStatus() }}>{busy ? 'İşlem sürüyor…' : deleting ? 'Hizmeti sil' : 'Durumu değiştir'}</button>
      <button type="button" onClick={() => { restoreFocus.current = true; setTarget(null); setError('') }}>Vazgeç</button></div>
    </fieldset>}
    <div className={styles.actions}>
      <button type="button" disabled={busy || loading || editing !== null} onClick={() => { setNotice(''); load(); heading.current?.focus() }}>Listeyi yenile</button>
      <button type="button" disabled={blocked || page === 1} onClick={() => { setNotice(''); load(page - 1) }}>Önceki sayfa</button>
      <span>Sayfa {data?.page ?? page}</span>
      <button type="button" disabled={blocked || !data?.hasMore} onClick={() => { setNotice(''); load(page + 1) }}>Sonraki sayfa</button>
    </div>
  </section>
}
