import styles from './App.module.css'

export default function App() {
  return (
    <main className={styles.page}>
      <section className={styles.card} aria-labelledby="page-title">
        <span className={styles.eyebrow}>Randevu</span>
        <h1 id="page-title">İşletmenizin günü, tek bakışta.</h1>
        <p>
          Randevu yönetimi ve çevrim içi rezervasyon ekranları burada yer alacak.
        </p>
      </section>
    </main>
  )
}
