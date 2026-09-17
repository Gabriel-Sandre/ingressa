import http from 'k6/http'
import { check, fail } from 'k6'

export const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080'
export const SENHA = 'Senha@123'

const json = { headers: { 'Content-Type': 'application/json' } }

export function autenticado(token, extras = {}) {
  return { headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}`, ...extras } }
}

export function criarCliente(sufixo) {
  const email = `carga-${sufixo}@teste.dev`
  http.post(`${BASE_URL}/api/auth/registrar`, JSON.stringify({ nome: `Cliente ${sufixo}`, email, senha: SENHA }), json)
  const login = http.post(`${BASE_URL}/api/auth/login`, JSON.stringify({ email, senha: SENHA }), json)
  if (!check(login, { 'login ok': (r) => r.status === 200 })) {
    fail(`login falhou (${login.status}). Aumente LIMITE_AUTENTICACAO no .env para testes de carga.`)
  }
  return login.json('accessToken')
}

export function buscarEvento(termo) {
  const r = http.get(`${BASE_URL}/api/eventos?busca=${encodeURIComponent(termo)}`)
  const evento = r.json('itens.0')
  if (!evento) fail(`evento "${termo}" não encontrado; suba o ambiente com dados de demonstração`)
  const detalhe = http.get(`${BASE_URL}/api/eventos/${evento.id}`).json()
  return detalhe
}
