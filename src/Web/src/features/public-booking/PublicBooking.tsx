import { useCallback, useEffect, useState } from 'react'
import { useLocation, useNavigate } from 'react-router'
import BookingWizard from './BookingWizard'
import BookingResult from './BookingResult'
import { isCatalog, readAccess, resultId, type Catalog, type LinkAccess, type Receipt, type Result } from './publicBookingApi'
import usePublicRead from './usePublicRead'
import styles from './PublicBooking.module.css'

export default function PublicBooking({ initialAccess }: { initialAccess: () => LinkAccess }) {
  const location = useLocation(), navigate = useNavigate(), id = resultId(location.pathname)
  const [proof, setProof] = useState(initialAccess), [created, setCreated] = useState<{ result: Result; receipt: Receipt } | null>(null), [business, setBusiness] = useState<Catalog['business'] | null>(null)
  const onBusiness = useCallback((value: Catalog['business']) => setBusiness(value), [])
  const resultCatalog = usePublicRead(id ? '/catalog?page=1&pageSize=1' : null, isCatalog, 0)
  const visibleBusiness = business ?? resultCatalog?.data?.business
  useEffect(() => {
    document.title = 'Randevu · Çevrim içi rezervasyon'
    const capture = () => { const next = initialAccess() ?? readAccess(); if (next) { setCreated(null); setProof(next); void navigate(`/randevu/sonuc/${next.id}`, { replace: true }) } }
    window.addEventListener('hashchange', capture)
    return () => window.removeEventListener('hashchange', capture)
  }, [initialAccess, navigate])
  const validRoot = /^\/randevu\/?$/u.test(location.pathname)
  return <main className={`auth-theme ${styles.shell}`}>
    <header className={styles.header}><span className={styles.brand}>Randevu</span>{visibleBusiness && <span>{visibleBusiness.name}</span>}</header>
    <div className={styles.content}>{id ? <BookingResult key={`${id}:${proof?.token ?? ''}`} id={id} initialToken={proof?.id === id ? proof.token : undefined} initialResult={created?.result.id === id ? created.result : undefined} receipt={created?.result.id === id ? created.receipt : undefined}/>
      : validRoot ? <BookingWizard onBusiness={onBusiness} onCreated={(result, receipt) => { setCreated({ result, receipt }); void navigate(`/randevu/sonuc/${result.id}`, { replace: true }) }}/>
      : <><h1>Sayfa bulunamadı</h1><p>Bu randevu adresi geçerli değil. İşletmenin paylaştığı bağlantıyı kontrol edin.</p></>}
    </div>
    {visibleBusiness && <footer className={styles.footer}>{visibleBusiness.phone && <p>İşletme telefonu: {visibleBusiness.phone}</p>}{visibleBusiness.address && <p>{visibleBusiness.address}</p>}</footer>}
  </main>
}
