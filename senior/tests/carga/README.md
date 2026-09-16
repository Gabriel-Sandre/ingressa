# Testes de carga (k6)

| Script | Cenário | Metas (o teste falha se não cumprir) |
|---|---|---|
| `vitrine.js` | Até 300 requisições/s na vitrine e no detalhe do evento | p95 < 300 ms, erros < 1% |
| `pico-de-vendas.js` | 300 compradores abrindo a venda juntos, todos pela fila virtual | nenhum 5xx, p95 da fila < 200 ms, p95 da reserva < 500 ms, ninguém reserva sem passe, estoque nunca negativo |

## Como rodar

```bash
# no .env: LIMITE_AUTENTICACAO=100000 e LIMITE_RESERVAS=100000
# (todos os usuários virtuais saem do mesmo IP)
docker compose up -d --build
docker compose --profile carga run --rm --service-ports k6 run /scripts/vitrine.js
docker compose --profile carga run --rm --service-ports k6 run -e COMPRADORES=300 /scripts/pico-de-vendas.js
```

- Painel ao vivo do k6: http://localhost:5665
- Relatório HTML ao final: `tests/carga/resultados/relatorio.html` (não vai para o Git)
- Durante o teste, acompanhe no Grafana (http://localhost:3000) as métricas `ingressa_*` e os traces.

Os números dependem da máquina onde o teste roda; compare sempre execuções feitas no mesmo ambiente.
