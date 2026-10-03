import { useEffect, useState } from 'react'
import { logoEndpoint, readLogo, type Logo } from './businessLogoApi'
import styles from './BusinessLogo.module.css'

export default function BusinessMark({ revision = 0 }: { revision?: number }) {
  const [logo, setLogo] = useState<Logo | null>(null), [failed, setFailed] = useState(false)
  useEffect(() => {
    const controller = new AbortController()
    void fetch(logoEndpoint, { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(readLogo).then(value => { if (!controller.signal.aborted) { setLogo(value); setFailed(false) } })
      .catch(() => { if (!controller.signal.aborted) setFailed(true) })
    return () => controller.abort()
  }, [revision])
  return logo?.imageUrl && !failed ? <img className={styles.mark} src={logo.imageUrl} alt="İşletme logosu" onError={() => setFailed(true)} /> : null
}
