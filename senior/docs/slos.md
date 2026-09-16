# Objetivos de serviço (SLOs)

Metas que orientam alarmes, testes de carga e decisões de capacidade. Medidas no load balancer
(produção) e pelo k6 (antes de cada versão).

| Jornada | Indicador (SLI) | Meta (SLO) | Janela | Onde é verificado |
|---|---|---|---|---|
| Vitrine e detalhe do evento | requisições com resposta < 300 ms | 95% | 28 dias | k6 `vitrine.js`; alarme `latencia-api` (p95 < 500 ms, toda a API) |
| Vitrine | respostas sem erro 5xx | 99,9% | 28 dias | alarme `erros-5xx` |
| Entrar/consultar fila virtual | resposta < 200 ms | 95% | por abertura de vendas | k6 `pico-de-vendas.js` |
| Reservar | resposta < 500 ms | 95% | por abertura de vendas | k6 `pico-de-vendas.js` |
| Reservar | nunca vender além da capacidade | 100% | sempre | CHECK no banco + teste de integração + k6 |
| Emissão do ingresso após pagamento | concluída em < 1 min | 99% | 28 dias | métrica de idade da outbox; alarme `fila-de-falhas` |

**Orçamento de erro:** 99,9% em 28 dias ≈ 40 minutos de indisponibilidade. Se o orçamento
acabar, novas funcionalidades param até a causa ser corrigida.

Os números acima são metas de projeto. Os valores medidos em cada execução ficam em
[`desempenho.md`](desempenho.md).
