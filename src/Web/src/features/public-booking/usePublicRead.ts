import { useEffect, useState } from 'react'
import { get } from './publicBookingApi'

export default function usePublicRead<T>(path: string | null, valid: (v: unknown) => v is T, revision: number) {
  const [state, setState] = useState<{ key: string; data?: T; error?: unknown } | null>(null)
  const key = `${revision}:${path}`
  useEffect(() => {
    if (!path) return
    const controller = new AbortController()
    void get(path, valid, AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]))
      .then(data => { if (!controller.signal.aborted) setState({ key, data }) })
      .catch((error: unknown) => { if (!controller.signal.aborted) setState({ key, error }) })
    return () => controller.abort()
  }, [path, valid, key])
  return state?.key === key ? state : null
}
