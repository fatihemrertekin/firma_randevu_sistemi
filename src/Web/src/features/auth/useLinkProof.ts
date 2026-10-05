import { useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { authPaths } from '../../app/routes'
import { useNavigationChange } from '../../app/NavigationEvents'

export type LinkProof = { kind: 'reset' | 'verify'; token: string } | null
export function readLinkProof(): LinkProof {
  if (typeof window === 'undefined') return null
  const hash = new URLSearchParams(window.location.hash.slice(1))
  const reset = hash.get('reset-owner-password'), verification = hash.get('verify-owner-email')
  const proof: LinkProof = reset !== null ? { kind: 'reset', token: reset }
    : verification !== null ? { kind: 'verify', token: verification } : null
  if (proof) window.history.replaceState(null, '', proof.kind === 'reset' ? authPaths.resetCode : authPaths.verify)
  return proof
}
export default function useLinkProof(initialProof: () => LinkProof) {
  const [proof, setProof] = useState(initialProof)
  const currentProof = useRef(proof)
  const navigate = useNavigate(), location = useLocation()
  useEffect(() => {
    const capture = () => {
      const next = readLinkProof()
      if (next) { currentProof.current = next; setProof(next); void navigate(next.kind === 'reset' ? authPaths.resetCode : authPaths.verify, { replace: true }) }
    }
    window.addEventListener('hashchange', capture)
    return () => window.removeEventListener('hashchange', capture)
  }, [navigate])
  const expected = proof?.kind === 'reset' ? authPaths.resetCode : authPaths.verify
  useNavigationChange(next => {
    const current = currentProof.current
    if (current && next.pathname !== (current.kind === 'reset' ? authPaths.resetCode : authPaths.verify)) { currentProof.current = null; setProof(null) }
  })
  return { proof: location.pathname === expected ? proof : null, clearProof: () => { currentProof.current = null; setProof(null) } }
}
