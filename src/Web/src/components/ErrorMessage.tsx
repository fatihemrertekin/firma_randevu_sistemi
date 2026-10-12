import { useEffect, useRef } from 'react'

export default function ErrorMessage({ message, id }: { message: string; id?: string }) {
  const element = useRef<HTMLParagraphElement>(null)
  useEffect(() => { element.current?.focus() }, [message])
  return message ? <p id={id} ref={element} role="alert" tabIndex={-1}>{message}</p> : null
}
