import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App'
import { ProvedorDeSessao } from './sessao/Sessao'
import './index.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <ProvedorDeSessao>
        <App />
      </ProvedorDeSessao>
    </BrowserRouter>
  </StrictMode>,
)
