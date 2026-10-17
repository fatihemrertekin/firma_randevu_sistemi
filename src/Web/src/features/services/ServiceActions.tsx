import { RequestVerificationError, requestErrorMessage } from '../../app/api'
import { useEffect, useRef, useState } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import { readService, ServiceRequestError, serviceFailure, type Service, type ServicePost } from './servicesApi'
import styles from './Services.module.css'

type Props = { service: Service; post: ServicePost; blocked: boolean; dirty: boolean;
  onBusyChange: (value: boolean) => void; onConfirmChange: (value: boolean) => void; onRequiresReload: () => void;
  onChanged: (service: Service, message: string) => void; onDeleted: (message: string) => void }
export default function ServiceActions({ service, post, blocked, dirty, onBusyChange, onConfirmChange, onRequiresReload, onChanged, onDeleted }: Props) {
  const [target, setTarget] = useState<'status' | 'delete' | null>(null)
  const [busy, setBusy] = useState(false), [error, setError] = useState('')
  const sending = useRef(false), confirm = useRef<HTMLButtonElement>(null), opener = useRef<HTMLButtonElement | null>(null)
  const restoreFocus = useRef(false)
  useEffect(() => {
    if (target) confirm.current?.focus()
    else if (restoreFocus.current) { restoreFocus.current = false; opener.current?.focus() }
  }, [target])
  function open(value: 'status' | 'delete', button: HTMLButtonElement) { opener.current = button; setTarget(value); setError(''); onConfirmChange(true) }
  function cancel() { restoreFocus.current = true; setTarget(null); setError(''); onConfirmChange(false) }
  async function change() {
    if (!target || sending.current || blocked) return
    sending.current = true; setBusy(true); onBusyChange(true); setError('')
    try {
      if (target === 'delete') {
        const response = await post(`/api/services/${service.id}/delete`, { version: service.version }, AbortSignal.timeout(15000))
        if (response.status !== 204) throw new ServiceRequestError(response.ok ? 500 : response.status)
        onDeleted(`${service.name} hizmet listesinden silindi.`); return
      }
      const updated = await readService(await post(`/api/services/${service.id}/status`,
        { isActive: !service.isActive, version: service.version }, AbortSignal.timeout(15000)))
      if (updated.id !== service.id) throw new ServiceRequestError(500)
      const current = await readService(await fetch(`/api/services/${service.id}`, { cache: 'no-store', signal: AbortSignal.timeout(15000) }))
      if (current.id !== service.id) throw new ServiceRequestError(500)
      onChanged(current, `${updated.name} ${updated.isActive ? 'aktifleştirildi' : 'pasifleştirildi'}.`)
    } catch (problem: unknown) {
      setError(problem instanceof ServiceRequestError ? problem.message : requestErrorMessage(problem, serviceFailure(500)))
      setTarget(null); onConfirmChange(false); if (!(problem instanceof RequestVerificationError)) onRequiresReload()
    } finally { sending.current = false; setBusy(false); onBusyChange(false) }
  }
  return <section className={styles.definitionActions} aria-labelledby="service-actions-title">
    <h2 id="service-actions-title">Durum ve silme</h2>
    <p>Pasif hizmet için yeni randevu alınamaz ve mevcut randevu bu hizmetle başka zamana taşınamaz. Mevcut randevular iptal edilmez. Pasifleştirme kaydı korur; silme listeden kaldırır, değişiklik geçmişi korunur.</p>
    {dirty && <p>Durumu değiştirmek veya silmek için önce değişiklikleri kaydedin ya da vazgeçin.</p>}
    <ErrorMessage message={error} />
    <div className={styles.actions}>
      <button type="button" disabled={blocked || target !== null} onClick={event => open('status', event.currentTarget)}>{service.isActive ? 'Pasifleştir' : 'Aktifleştir'}</button>
      <button type="button" className={styles.danger} disabled={blocked || target !== null} onClick={event => open('delete', event.currentTarget)}>Sil</button>
    </div>
    {target && <fieldset className={styles.confirmation} disabled={busy} aria-label={target === 'delete' ? 'Hizmet silme onayı' : 'Hizmet durum değişikliği onayı'}>
      <legend>Hizmeti {target === 'delete' ? 'sil' : service.isActive ? 'pasifleştir' : 'aktifleştir'}</legend>
      {target === 'delete' ? <p><strong>{service.name}</strong> hizmet listesinden ve personelin hizmet seçimlerinden kaldırılacak; yeniden kullanılamayacak. Önceki bağlantılar ve değişiklik geçmişi korunur.</p>
        : <p><strong>{service.name}</strong> {service.isActive ? 'pasif' : 'aktif'} olarak işaretlenecek. {service.isActive ? 'Bu hizmet için yeni randevu alınamayacak ve mevcut randevu bu hizmetle başka zamana taşınamayacak. Mevcut randevular iptal edilmez.' : 'Diğer rezervasyon koşulları da uygunsa bu hizmet için yeniden randevu alınabilir.'} Kayıt silinmez; hizmetin adı, süresi ve fiyatı korunur.</p>}
      <div className={styles.actions}>
        <button type="button" ref={confirm} className={target === 'delete' ? styles.danger : styles.primary} disabled={blocked}
          onClick={() => { void change() }}>{busy ? 'İşlem sürüyor…' : target === 'delete' ? 'Hizmeti sil' : 'Durumu değiştir'}</button>
        <button type="button" onClick={cancel}>Vazgeç</button>
      </div>
    </fieldset>}
  </section>
}
