import { useEffect, useRef, useState } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import styles from './StaffAccounts.module.css'

type StaffAccount = { id: string; email: string; isActive: boolean; version: string }
type StaffPage = { items: StaffAccount[]; page: number; hasMore: boolean }
type Props = { post: (path: string, body: object, signal?: AbortSignal) => Promise<Response> }

class StaffRequestError extends Error { }
function isAccount(value: unknown): value is StaffAccount {
  return typeof value === 'object' && value !== null &&
    'id' in value && typeof value.id === 'string' && 'email' in value && typeof value.email === 'string' &&
    'isActive' in value && typeof value.isActive === 'boolean' && 'version' in value && typeof value.version === 'string'
}
function parsePage(value: unknown): StaffPage {
  if (typeof value !== 'object' || value === null || !('items' in value) || !Array.isArray(value.items) ||
    !value.items.every(isAccount) || !('page' in value) || typeof value.page !== 'number' ||
    !('hasMore' in value) || typeof value.hasMore !== 'boolean') throw new StaffRequestError('Liste yanıtı doğrulanamadı. Listeyi yenile.')
  return { items: value.items, page: value.page, hasMore: value.hasMore }
}

function failure(status: number) {
  if (status === 401) return 'Oturumun sona erdi. Yeniden giriş yap.'
  if (status === 403) return 'Bu işlem için yetkin yok.'
  if (status === 409) return 'Hesap değişti. Listeyi yenileyip yeniden seç.'
  if (status === 404) return 'Hesap artık bu listede değil. Listeyi yenile.'
  if (status === 429) return 'Çok sık denendi. Bir süre bekleyip yeniden dene.'
  if (status === 400) return 'İşlem doğrulanamadı. Listeyi yenileyip yeniden dene.'
  return 'Sonuç doğrulanamadı. Listeyi yenileyerek hesabın durumunu kontrol et.'
}

export default function StaffAccounts({ post }: Props) {
  const [page, setPage] = useState(1)
  const [revision, setRevision] = useState(0)
  const [data, setData] = useState<StaffPage | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [target, setTarget] = useState<StaffAccount | null>(null)
  const [busy, setBusy] = useState(false)
  const sending = useRef(false)
  const mounted = useRef(true)
  const confirm = useRef<HTMLButtonElement>(null)
  const heading = useRef<HTMLHeadingElement>(null)
  const opener = useRef<HTMLButtonElement | null>(null)
  const restoreFocus = useRef(false)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  useEffect(() => {
    if (target) confirm.current?.focus()
    else if (restoreFocus.current) { restoreFocus.current = false; opener.current?.focus() }
  }, [target])
  useEffect(() => {
    const controller = new AbortController()
    const signal = AbortSignal.any([controller.signal, AbortSignal.timeout(15000)])
    void fetch(`/api/staff-accounts/?page=${page}`, { cache: 'no-store', signal })
      .then(async response => {
        if (!response.ok) throw new StaffRequestError(failure(response.status))
        const body = parsePage(await response.json())
        if (!controller.signal.aborted) setData(body)
      }).catch((problem: unknown) => {
        if (!controller.signal.aborted) setError(problem instanceof StaffRequestError ? problem.message : 'Liste alınamadı. Yeniden dene.')
      }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [page, revision])

  function load(nextPage = page) {
    setLoading(true); setError(''); setData(null); setTarget(null)
    setPage(nextPage); setRevision(value => value + 1)
  }
  function reload() { setNotice(''); load(); heading.current?.focus() }
  function cancel() { restoreFocus.current = true; setTarget(null) }
  async function deactivate() {
    if (!target || sending.current) return
    sending.current = true; setBusy(true); setError(''); setNotice('')
    try {
      const response = await post(`/api/staff-accounts/${target.id}/deactivate`, { version: target.version }, AbortSignal.timeout(15000))
      if (!response.ok) throw new StaffRequestError(failure(response.status))
      if (!mounted.current) return
      setTarget(null); setNotice('Çalışan hesabı pasifleştirildi. Yeni giriş engellendi ve açık oturumları iptal edildi.')
      load(); heading.current?.focus()
    } catch (problem: unknown) {
      if (mounted.current) setError(problem instanceof StaffRequestError ? problem.message : failure(500))
    } finally {
      sending.current = false
      if (mounted.current) setBusy(false)
    }
  }

  return <section aria-labelledby="staff-accounts-title">
    <h2 id="staff-accounts-title" ref={heading} className={styles.heading} tabIndex={-1}>Çalışan hesapları</h2>
    <p>Personelin sisteme giriş erişimini yönetin. Pasifleştirme hesabı silmez.</p>
    <ErrorMessage message={error} />
    {notice && <p role="status">{notice}</p>}
    {loading && <p role="status">Çalışan hesapları yükleniyor…</p>}
    {!loading && data?.items.length === 0 && <p>Bu sayfada çalışan hesabı yok. Yeni çalışan için aşağıdaki davet bölümünü kullanın.</p>}
    {data && <ul className={styles.list}>
      {data.items.map(account => <li key={account.id} className={styles.row}>
        <div className={styles.account}><strong>{account.email}</strong><p>{account.isActive ? 'Aktif' : 'Pasif'}</p></div>
        {account.isActive && <button type="button" className={styles.danger} disabled={busy || loading || target !== null}
          aria-label={`${account.email} hesabını pasifleştir`}
          onClick={event => { opener.current = event.currentTarget; setTarget(account); setError(''); setNotice('') }}>Pasifleştir</button>}
      </li>)}
    </ul>}
    {target && <fieldset className={styles.confirmation} aria-label="Hesabı pasifleştirme onayı" disabled={busy}>
      <legend>Hesabı pasifleştir</legend>
      <p className={styles.account}><strong>{target.email}</strong> hesabı sisteme giriş yapamayacak. Açık oturumları ve mevcut parola sıfırlama kodları geçersiz olacak. Hesap silinmez.</p>
      <button type="button" className={styles.confirm} ref={confirm} onClick={() => { void deactivate() }}>{busy ? 'Pasifleştiriliyor…' : 'Hesabı pasifleştir'}</button>
      <button type="button" onClick={cancel}>Vazgeç</button>
    </fieldset>}
    <div className={styles.actions}>
      <button type="button" disabled={busy || loading} onClick={reload}>Listeyi yenile</button>
      <button type="button" disabled={busy || loading || page === 1 || target !== null}
        onClick={() => { setNotice(''); load(page - 1) }}>Önceki sayfa</button>
      <span>Sayfa {page}</span>
      <button type="button" disabled={busy || loading || !data?.hasMore || target !== null}
        onClick={() => { setNotice(''); load(page + 1) }}>Sonraki sayfa</button>
    </div>
  </section>
}
