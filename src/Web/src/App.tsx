import { useEffect, useState, type FormEvent } from 'react'
import styles from './App.module.css'

type Account = { email: string }

async function getAccount(): Promise<Account | null> {
  const response = await fetch('/api/auth/me')
  if (response.status === 401) return null
  if (!response.ok) throw new Error('Hesap bilgisi alınamadı.')
  return (await response.json()) as Account
}

async function getCsrfToken(): Promise<string> {
  const response = await fetch('/api/auth/csrf', { cache: 'no-store' })
  if (!response.ok) throw new Error('İstek doğrulaması alınamadı.')
  const body = (await response.json()) as { token: string }
  return body.token
}

export default function App() {
  const [account, setAccount] = useState<Account | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  useEffect(() => {
    getAccount()
      .then(setAccount)
      .catch(() => setError('Oturum durumu alınamadı. Sayfayı yenileyin.'))
      .finally(() => setLoading(false))
  }, [])

  async function handleLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy) return
    setBusy(true)
    setError('')
    try {
      const token = await getCsrfToken()
      const response = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
        body: JSON.stringify({ email, password }),
      })
      if (!response.ok) {
        setError(response.status === 401 ? 'E-posta veya parola hatalı.' : 'Giriş yapılamadı.')
        return
      }
      setPassword('')
      setAccount(await getAccount())
    } catch {
      setError('Giriş yapılamadı. Lütfen yeniden deneyin.')
    } finally {
      setBusy(false)
    }
  }

  async function handleLogout() {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      const token = await getCsrfToken()
      const response = await fetch('/api/auth/logout', {
        method: 'POST',
        headers: { 'X-CSRF-TOKEN': token },
      })
      if (!response.ok) throw new Error('Çıkış başarısız.')
      setAccount(null)
    } catch {
      setError('Çıkış yapılamadı. Lütfen yeniden deneyin.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className={styles.page}>
      <section className={styles.card} aria-labelledby="page-title">
        <span className={styles.eyebrow}>Randevu</span>
        {loading ? (
          <p role="status">Oturum kontrol ediliyor…</p>
        ) : account ? (
          <>
            <h1 id="page-title">Hoş geldiniz</h1>
            <p>{account.email}</p>
            <p>Yönetim ekranları hazırlanıyor.</p>
            <button type="button" onClick={handleLogout} disabled={busy}>
              {busy ? 'Çıkış yapılıyor…' : 'Çıkış yap'}
            </button>
          </>
        ) : (
          <>
            <h1 id="page-title">İşletme girişi</h1>
            <form onSubmit={handleLogin}>
              <label htmlFor="email">E-posta</label>
              <input id="email" type="email" autoComplete="username" required
                value={email} onChange={event => setEmail(event.target.value)} />
              <label htmlFor="password">Parola</label>
              <input id="password" type="password" autoComplete="current-password" required
                value={password} onChange={event => setPassword(event.target.value)} />
              <button type="submit" disabled={busy}>
                {busy ? 'Giriş yapılıyor…' : 'Giriş yap'}
              </button>
            </form>
          </>
        )}
        {error && <p className={styles.error} role="alert">{error}</p>}
      </section>
    </main>
  )
}
