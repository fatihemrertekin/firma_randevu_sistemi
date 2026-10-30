import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { formatTryPrice } from '../../app/money'
import { formatBusinessDateTime } from '../../app/format'
import { readTargets, SessionExpired, type Target, type TargetPage } from './operatorApi'
import SubscriptionOperator from './SubscriptionOperator'
import styles from './SubscriptionOperator.module.css'
type Query = { page: number; search: string; access: string; refresh: number }

export default function OperatorFirms({ active = true, onExpired, onUnsaved, onBusy }: { active?: boolean; onExpired: () => void; onUnsaved: (value: boolean) => void; onBusy: (value: boolean) => void }) {
  const [data, setData] = useState<TargetPage | null>(null), [selected, setSelected] = useState<Target | null>(null)
  const [query, setQuery] = useState<Query>({ page: 1, search: '', access: 'all', refresh: 0 }), [search, setSearch] = useState('')
  const [loading, setLoading] = useState(true), [error, setError] = useState('')
  const opener = useRef<HTMLButtonElement | null>(null), restoreFocus = useRef(false)
  const changeQuery = useCallback((update: (q: Query) => Query) => { setLoading(true); setError(''); setQuery(update) }, [])
  useEffect(() => {
    const abort = new AbortController()
    void readTargets(query.page, query.search, query.access, AbortSignal.any([abort.signal, AbortSignal.timeout(20000)])).then(value => { if (!abort.signal.aborted) setData(value) }).catch(problem => {
      if (abort.signal.aborted) return
      if (problem instanceof SessionExpired) onExpired()
      setData(null); setError(problem instanceof Error && !(problem instanceof TypeError) ? problem.message : 'Firma listesi alınamadı. Bağlantıyı kontrol edip yeniden deneyin.')
    }).finally(() => { if (!abort.signal.aborted) setLoading(false) })
    return () => { abort.abort() }
  }, [query, onExpired])
  useEffect(() => { if (!selected && !loading && restoreFocus.current) { opener.current?.focus(); restoreFocus.current = false } }, [selected, loading])
  const back = useCallback(() => { restoreFocus.current = true; setSelected(null); changeQuery(q => ({ ...q, refresh: q.refresh + 1 })) }, [changeQuery])
  function find(event: FormEvent) { event.preventDefault(); changeQuery(q => ({ ...q, search: search.trim(), page: 1 })) }
  return <>
    <main className={styles.main} hidden={!!selected}>
      <div className={styles.heading}><h1>Firmalar</h1><button disabled={loading} onClick={() => changeQuery(q => ({ ...q, refresh: q.refresh + 1 }))}>Yenile</button></div>
      <p>Firmayı açarak dönem ve doğruladığınız ödeme kayıtlarını yönetin.</p>
      <form className={styles.filters} onSubmit={find}>
        <div><label htmlFor="firm-search">Firma ara</label><input id="firm-search" type="search" maxLength={160} value={search} onChange={e => setSearch(e.target.value)} /></div>
        <button disabled={loading} type="submit">Ara</button>
        <div><label htmlFor="firm-access">Erişim</label><select id="firm-access" value={query.access} onChange={e => changeQuery(q => ({ ...q, access: e.target.value }))}>
          <option value="all">Tümü</option><option value="restricted">Salt okunur</option><option value="open">Normal</option><option value="unavailable">Bağlantı yok</option>
        </select></div>
      </form>
      <p>Erişim filtresi bu sayfadaki en fazla 12 firmaya uygulanır.</p>
      {loading && <p role="status">Firma bilgileri yükleniyor…</p>}
      {error && <><p role="alert">{error}</p><button onClick={() => changeQuery(q => ({ ...q, refresh: q.refresh + 1 }))}>Yeniden dene</button></>}
      {data && <>
        <p className={styles.muted}>Kontrol zamanı: {formatBusinessDateTime(data.checkedAt)}</p>
        {!data.items.length ? <p>{!data.total && !query.search ? 'Henüz firma hedefi tanımlanmamış. Kurulum ayarlarını kontrol edin.' : 'Bu sayfada arama ve erişim filtresine uyan firma yok. Aramayı veya filtreyi değiştirin.'}</p> :
          <table className={styles.firms}><caption className={styles.srOnly}>Firmaların abonelik ve erişim özeti</caption><thead><tr><th>Firma</th><th>Son başlayan dönem</th><th>Gecikmiş borç</th><th>Erişim</th><th><span className={styles.srOnly}>İşlem</span></th></tr></thead><tbody>
            {data.items.map(target => <tr key={target.id}>
              <th scope="row"><span>{target.name}</span><small>{target.id}</small></th>
              <td data-label="Son başlayan dönem">{target.status === 'unavailable' ? 'Bilinmiyor' : target.summary?.latestStartedPeriod ? <>{new Intl.DateTimeFormat('tr-TR', { month: 'long', year: 'numeric', timeZone: 'Europe/Istanbul' }).format(new Date(target.summary.latestStartedPeriod.startsOn + 'T12:00:00Z'))}<small>{target.summary.latestStartedPeriod.paymentState === 'Paid' ? 'Ödendi' : target.summary.latestStartedPeriod.paymentState === 'Partial' ? 'Kısmi ödeme' : 'Ödeme bekleniyor'}</small></> : 'Henüz dönem yok'}</td>
              <td data-label="Gecikmiş borç">{target.summary ? formatTryPrice(target.summary.overdueOutstandingTry) : 'Bilinmiyor'}</td>
              <td data-label="Erişim">{target.status === 'unavailable' ? 'Bağlantı yok · Bilinmiyor' : target.summary?.restrictionsApplied ? 'Salt okunur' : 'Normal'}</td>
              <td><button disabled={loading} aria-label={`${target.name} firmasını aç`} onClick={e => { opener.current = e.currentTarget; setSelected(target) }}>Aç</button></td>
            </tr>)}
          </tbody></table>}
        {data.total > 12 && <div className={styles.actions}><button disabled={loading || data.page === 1} onClick={() => changeQuery(q => ({ ...q, page: q.page - 1 }))}>Önceki firmalar</button><span>{data.page} / {Math.ceil(data.total / 12)}</span><button disabled={loading || data.page * 12 >= data.total} onClick={() => changeQuery(q => ({ ...q, page: q.page + 1 }))}>Sonraki firmalar</button></div>}
      </>}
    </main>
    {selected && <SubscriptionOperator key={selected.id} active={active} targetId={selected.id} onBack={back} onExpired={onExpired} onUnsaved={onUnsaved} onBusy={onBusy} />}
  </>
}
