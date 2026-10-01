import { useEffect, useRef } from 'react'

export default function ErrorMessage({ message }: { message: string }) {
  const element = useRef<HTMLParagraphElement>(null)
  useEffect(() => { element.current?.focus() }, [message])
  return message ? <p ref={element} role="alert" tabIndex={-1}>{message}</p> : null
}
