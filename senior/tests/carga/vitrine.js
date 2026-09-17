// Carga de leitura na vitrine: o cenário mais comum (muita gente olhando, pouca comprando).
// docker compose --profile carga run --rm k6 run /scripts/vitrine.js
import http from 'k6/http'
import { check, sleep } from 'k6'
import { BASE_URL } from './comum.js'

export const options = {
  scenarios: {
    navegacao: {
      executor: 'ramping-arrival-rate',
      startRate: 10,
      timeUnit: '1s',
      preAllocatedVUs: 50,
      maxVUs: 300,
      stages: [
        { target: 100, duration: '30s' },
        { target: 300, duration: '1m' },
        { target: 0, duration: '15s' },
      ],
    },
  },
  // Metas de serviço (SLOs) desta rota. O teste falha se não forem cumpridas.
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{rota:vitrine}': ['p(95)<300'],
    'http_req_duration{rota:detalhe}': ['p(95)<300'],
  },
}

const buscas = ['', 'rock', 'samba', 'workshop', 'noite']

export default function () {
  const busca = buscas[Math.floor(Math.random() * buscas.length)]
  const pagina = http.get(`${BASE_URL}/api/eventos?busca=${busca}&pagina=1&tamanhoPagina=9`, { tags: { rota: 'vitrine' } })
  check(pagina, { 'vitrine 200': (r) => r.status === 200 })

  const itens = pagina.json('itens') || []
  if (itens.length > 0) {
    const id = itens[Math.floor(Math.random() * itens.length)].id
    const detalhe = http.get(`${BASE_URL}/api/eventos/${id}`, { tags: { rota: 'detalhe' } })
    check(detalhe, { 'detalhe 200': (r) => r.status === 200 })
  }
  sleep(Math.random())
}
