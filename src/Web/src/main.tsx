import { createRoot } from 'react-dom/client'
import App from './App'
import './global.css'

const root = document.getElementById('root')

if (!root) {
  throw new Error('Uygulama kök öğesi bulunamadı.')
}

createRoot(root).render(<App />)
