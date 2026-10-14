import type { ReactNode } from 'react'
import salonPhoto from '../../../../../assets/plates/salon-background-neutral-1254.webp'
import salonPhotoSmall from '../../../../../assets/plates/salon-background-neutral-640.webp'
import styles from './AuthenticationScreens.module.css'

export default function AuthenticationLayout({ children }: { children: ReactNode }) {
  return (
    <main className={`auth-theme ${styles.page}`}>
      <section className={styles.card} aria-labelledby="page-title">
        <div className={styles.branding}>
          <div>
            <span className={styles.wordmark}>Randevu</span>
            <span className={styles.brandContext}>İşletme paneli</span>
          </div>
        </div>
        <div className={styles.formContent}>{children}</div>
      </section>
      <aside className={styles.hero} aria-label="İşletme paneli hakkında">
        <img className={styles.heroPhoto} src={salonPhoto} srcSet={`${salonPhotoSmall} 640w, ${salonPhoto} 1254w`}
          sizes="(max-width: 900px) 100vw, 64vw" width={1254} height={1254} alt="" decoding="async" fetchPriority="high" />
        <div className={styles.heroContent}>
          <p className={styles.audience}>Berber ve kuaför işletmeleri için</p>
          <h2>İşletmenize odaklanın.<span>Kontrol sizde olsun.</span></h2>
          <p className={styles.description}>İşletme bilgilerinizi ve ekibinizin erişimlerini tek yerden yönetin.</p>
          <div className={styles.highlights}>
            <div><strong>Güvenli erişim</strong><p>İşletme sahibi hesabında iki aşamalı giriş.</p></div>
            <div><strong>Size ait bilgiler</strong><p>İşletme profiliniz ve kontrollü çalışan davetleri.</p></div>
          </div>
        </div>
      </aside>
    </main>
  )
}
