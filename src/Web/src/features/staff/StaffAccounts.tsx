import { useEffect, useRef, useState } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import styles from './StaffAccounts.module.css'
import { useLocation, useNavigate } from 'react-router'
import { pageSearch, readPage, readPageSize, sectionPaths } from '../../app/routes'
import Pagination from '../../components/Pagination'
import { isPageMetadata, type PageMetadata } from '../../app/pageMetadata'
import { useNavigationChange } from '../../app/NavigationEvents'
import { useCompletedNavigation } from '../../app/CompletedNavigation'

type StaffAccount = { id: string; email: string; isActive: boolean; version: string }
type StaffPage = PageMetadata & { items: StaffAccount[] }
type Props = { post: (path: string, body: object, signal?: AbortSignal) => Promise<Response> }

class StaffRequestError extends Error { }
function isAccount(value: unknown): value is StaffAccount {
  return typeof value === 'object' && value !== null &&
    'id' in value && typeof value.id === 'string' && 'email' in value && typeof value.email === 'string' &&
    'isActive' in value && typeof value.isActive === 'boolean' && 'version' in value && typeof value.version === 'string'
}
function parsePage(value: unknown): StaffPage {
  if (typeof value !== 'object' || value === null || !('items' in value) || !Array.isArray(value.items) ||
    !value.items.every(isAccount) ||
    !isPageMetadata(value) || value.items.length > value.pageSize) throw new StaffRequestError('Liste yanıtı doğrulanamadı. Listeyi yenile.')
  return { items: value.items, page: value.page, hasMore: value.hasMore, pageSize: value.pageSize, totalCount: value.totalCount }
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
  const location = useLocation(), navigate = useNavigate(), completed = useCompletedNavigation()
  const page = readPage(location.search), pageSize = readPageSize(location.search)
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
  useNavigationChange(next => { if (next.pathname === sectionPaths.access) { setLoading(true); setError(''); setData(null) } })
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  useEffect(() => {
    if (target) confirm.current?.focus()
    else if (restoreFocus.current) { restoreFocus.current = false; opener.current?.focus() }
  }, [target])
  useEffect(() => {
    const controller = new AbortController()
    const signal = AbortSignal.any([controller.signal, AbortSignal.timeout(15000)])
    void fetch(`/api/staff-accounts/?page=${page}${pageSize !== 20 ? `&pageSize=${pageSize}` : ''}`, { cache: 'no-store', signal })
      .then(async response => {
        if (!response.ok) throw new StaffRequestError(failure(response.status))
        const body = parsePage(await response.json())
        if (!controller.signal.aborted) {
          setData(body)
          if (body.page !== page) completed(sectionPaths.access + pageSearch(body.page, pageSize), { replace: true })
        }
      }).catch((problem: unknown) => {
        if (!controller.signal.aborted) setError(problem instanceof StaffRequestError ? problem.message : 'Liste alınamadı. Yeniden dene.')
      }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [page, pageSize, revision, completed, location.key])

  function load(nextPage = page, size = pageSize) {
    setLoading(true); setError(''); setData(null); setTarget(null)
    if (nextPage !== page || size !== pageSize) void navigate(sectionPaths.access + pageSearch(nextPage, size))
    else setRevision(value => value + 1)
  }
  function reload() { setNotice(''); load(); heading.current?.focus() }
  function cancel() { restoreFocus.current = true; setTarget(null) }
  async function changeActive() {
    if (!target || sending.current) return
    sending.current = true; setBusy(true); setError(''); setNotice('')
    try {
      const response = await post(`/api/staff-accounts/${target.id}/${target.isActive ? 'deactivate' : 'activate'}`, { version: target.version }, AbortSignal.timeout(15000))
      if (!response.ok) throw new StaffRequestError(failure(response.status))
      if (!mounted.current) return
      setTarget(null); setNotice(target.isActive
        ? 'Çalışan hesabı pasifleştirildi. Yeni giriş engellendi ve açık oturumları iptal edildi.'
        : 'Çalışan hesabı etkinleştirildi. Çalışan mevcut parolasıyla yeniden giriş yapabilir.')
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
    <p>Çalışanların panele giriş erişimini yönetin. Personel kayıtları, hizmetleri ve çalışma saatleri ayrı yönetilir.</p>
    <ErrorMessage message={error} />
    {notice && <p role="status">{notice}</p>}
    {loading && <p role="status">Çalışan hesapları yükleniyor…</p>}
    {!loading && data?.items.length === 0 && <p>Bu sayfada çalışan hesabı yok. Yeni çalışan için aşağıdaki davet bölümünü kullanın.</p>}
    {data && <ul className={styles.list}>
      {data.items.map(account => <li key={account.id} className={styles.row}>
        <div className={styles.account}><strong>{account.email}</strong><p>{account.isActive ? 'Aktif' : 'Pasif'}</p></div>
        <button type="button" className={account.isActive ? styles.danger : styles.primary} disabled={busy || loading || target !== null}
          aria-label={`${account.email} hesabını ${account.isActive ? 'pasifleştir' : 'etkinleştir'}`}
          onClick={event => { opener.current = event.currentTarget; setTarget(account); setError(''); setNotice('') }}>{account.isActive ? 'Pasifleştir' : 'Etkinleştir'}</button>
      </li>)}
    </ul>}
    {target && <fieldset className={styles.confirmation} aria-label={target.isActive ? 'Hesabı pasifleştirme onayı' : 'Hesabı etkinleştirme onayı'} disabled={busy}>
      <legend>Hesabı {target.isActive ? 'pasifleştir' : 'etkinleştir'}</legend>
      <p className={styles.account}><strong>{target.email}</strong> {target.isActive
        ? 'hesabı sisteme giriş yapamayacak. Açık oturumları ve mevcut parola sıfırlama kodları geçersiz olacak. Hesap silinmez.'
        : 'hesabı mevcut parolasıyla yeniden giriş yapabilecek. Çalışanın yeni oturum açması gerekir. Personel kaydı, hizmetleri ve çalışma saatleri değişmez.'}</p>
      <div className={styles.actions}>
        <button type="button" className={target.isActive ? styles.confirm : styles.primary} ref={confirm} onClick={() => { void changeActive() }}>{busy
          ? target.isActive ? 'Pasifleştiriliyor…' : 'Etkinleştiriliyor…'
          : target.isActive ? 'Hesabı pasifleştir' : 'Hesabı etkinleştir'}</button>
        <button type="button" onClick={cancel}>Vazgeç</button>
      </div>
    </fieldset>}
    <div className={styles.actions}>
      {(error || (data?.items.length ?? 0) > 0) && <button type="button" disabled={busy || loading} onClick={reload}>Listeyi yenile</button>}
      <Pagination label="Çalışan hesabı sayfaları" page={data?.page ?? page} pageSize={pageSize} itemCount={data?.items.length ?? 0} totalCount={data?.totalCount}
        hasNext={data?.hasMore ?? false} disabled={busy || loading || !!error || target !== null} onPageChange={next => { setNotice(''); load(next) }} onPageSizeChange={size => load(1, size)} />
    </div>
  </section>
}
