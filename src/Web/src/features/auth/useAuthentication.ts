import { useEffect, useRef, useState, type FormEvent } from 'react'
import { getAccount, postWithCsrf, type Account } from '../../app/api'

type SetupInfo = { key: string; uri: string }

export default function useAuthentication() {
  const [account, setAccount] = useState<Account | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [mfaRequired, setMfaRequired] = useState(false)
  const [useRecoveryCode, setUseRecoveryCode] = useState(false)
  const [code, setCode] = useState('')
  const [setupInfo, setSetupInfo] = useState<SetupInfo | null>(null)
  const [setupPassword, setSetupPassword] = useState('')
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null)
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [notice, setNotice] = useState('')
  const passwordChangePending = useRef(false)
  const [resettingPassword, setResettingPassword] = useState(false)
  const [resettingStaffPassword, setResettingStaffPassword] = useState(false)
  const [acceptingInvitation, setAcceptingInvitation] = useState(false)

  function clearPasswordFields() {
    setCurrentPassword('')
    setNewPassword('')
    setConfirmPassword('')
  }

  async function handlePasswordChange(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy || passwordChangePending.current) return
    setError('')
    setNotice('')
    if (newPassword !== confirmPassword) {
      setError('Yeni parola ve tekrarı aynı olmalı.')
      return
    }
    passwordChangePending.current = true
    setBusy(true)
    try {
      const response = await postWithCsrf('/api/auth/change-password', {
        currentPassword, newPassword, confirmPassword,
      })
      clearPasswordFields()
      if (response.status === 401 || response.status === 403 || response.status === 409) {
        setAccount(null)
        setMfaRequired(false)
        setCode('')
        setError('Oturum geçersiz. Yeniden giriş yapın.')
        return
      }
      if (!response.ok) {
        if (response.status === 429) {
          setError('Çok fazla deneme. Daha sonra tekrar deneyin.')
        } else if (response.status === 400) {
          const problem = (await response.json()) as { title?: string }
          setError(problem.title ?? 'Parola alanlarını kontrol edin.')
        } else {
          setError('Sonuç doğrulanamadı. Yeniden giriş yapmayı deneyin.')
        }
        return
      }
      setAccount(null)
      setMfaRequired(false)
      setUseRecoveryCode(false)
      setSetupInfo(null)
      setRecoveryCodes(null)
      setSetupPassword('')
      setPassword('')
      setCode('')
      setNotice(account?.staffAccess && !account.ownerAccess
        ? 'Parolanız değişti ve bütün oturumlar kapatıldı. Yeni parolanızla yeniden giriş yapın.'
        : 'Parolanız değişti ve bütün oturumlar kapatıldı. Yeni parolanız ve ikinci adımla yeniden giriş yapın.')
    } catch {
      clearPasswordFields()
      setError('Sonuç doğrulanamadı. Yeniden giriş yapmayı deneyin.')
    } finally {
      passwordChangePending.current = false
      setBusy(false)
    }
  }

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
    setNotice('')
    try {
      const response = await postWithCsrf('/api/auth/login', { email, password })
      setPassword('')
      if (response.status === 202) {
        setMfaRequired(true)
        return
      }
      if (!response.ok) {
        setError(response.status === 401 ? 'E-posta veya parola hatalı.' : 'Giriş yapılamadı.')
        return
      }
      setAccount(await getAccount())
    } catch {
      setError('Giriş yapılamadı. Lütfen yeniden deneyin.')
    } finally {
      setBusy(false)
    }
  }

  async function handleMfaLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy) return
    setBusy(true)
    setError('')
    try {
      const path = useRecoveryCode ? '/api/auth/mfa/recovery-login' : '/api/auth/mfa/login'
      const response = await postWithCsrf(path, { code })
      if (!response.ok) {
        setError('Kod doğrulanamadı. Lütfen yeniden deneyin.')
        return
      }
      setCode('')
      setMfaRequired(false)
      setUseRecoveryCode(false)
      setAccount(await getAccount())
    } catch {
      setError('Kod doğrulanamadı. Lütfen yeniden deneyin.')
    } finally {
      setBusy(false)
    }
  }

  async function handleSetup(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy) return
    setBusy(true)
    setError('')
    try {
      const response = await postWithCsrf('/api/auth/mfa/setup', { password: setupPassword })
      if (!response.ok) {
        setError(response.status === 401 ? 'Parola doğrulanamadı.' : 'Kurulum başlatılamadı.')
        return
      }
      setSetupInfo((await response.json()) as SetupInfo)
      setSetupPassword('')
    } catch {
      setError('Kurulum başlatılamadı. Lütfen yeniden deneyin.')
    } finally {
      setBusy(false)
    }
  }

  async function handleEnable(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy) return
    setBusy(true)
    setError('')
    try {
      const response = await postWithCsrf('/api/auth/mfa/enable', {
        password: setupPassword,
        code,
      })
      if (!response.ok) {
        setError('Parola veya doğrulama kodu hatalı.')
        return
      }
      const body = (await response.json()) as { recoveryCodes: string[] }
      setRecoveryCodes(body.recoveryCodes)
      setSetupInfo(null)
      setSetupPassword('')
      setCode('')
      setAccount(null)
    } catch {
      setError('İki aşamalı giriş açılamadı. Lütfen yeniden deneyin.')
    } finally {
      setBusy(false)
    }
  }

  async function handleLogout() {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      const response = await postWithCsrf('/api/auth/logout', {})
      if (!response.ok) throw new Error('Çıkış başarısız.')
      setAccount(null)
      setSetupInfo(null)
      setSetupPassword('')
      clearPasswordFields()
    } catch {
      setError('Çıkış yapılamadı. Lütfen yeniden deneyin.')
    } finally {
      setBusy(false)
    }
  }

  return {
    account, setAccount, loading, busy, error, setError, notice, setNotice,
    email, setEmail, password, setPassword, mfaRequired, setMfaRequired,
    useRecoveryCode, setUseRecoveryCode, code, setCode, setupInfo,
    setupPassword, setSetupPassword, recoveryCodes, setRecoveryCodes,
    currentPassword, setCurrentPassword, newPassword, setNewPassword,
    confirmPassword, setConfirmPassword, resettingPassword, setResettingPassword,
    resettingStaffPassword, setResettingStaffPassword, acceptingInvitation, setAcceptingInvitation,
    clearPasswordFields, handlePasswordChange, handleLogin, handleMfaLogin,
    handleSetup, handleEnable, handleLogout,
  }
}
