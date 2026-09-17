# ADR 0012 — Observabilidade com OpenTelemetry

**Status:** aceita · **Versão:** Sênior · **Data:** 2026-09

## Contexto

Na Pleno havia logs JSON com um identificador de correlação. Com API e Worker em várias réplicas, Redis e RabbitMQ no caminho, um log isolado não responde "por que esta compra demorou 3 s?" nem "quantas pessoas estão na fila agora?".

## Alternativas

1. Continuar só com logs estruturados.
2. SDK de um fornecedor (Datadog, New Relic, Application Insights).
3. **OpenTelemetry com exportação OTLP**, deixando o destino para a configuração.

## Decisão

Opção 3.

## Justificativa

- Padrão aberto: localmente os dados vão para Grafana (Tempo, Prometheus, Loki) num container; na AWS, para X-Ray e CloudWatch pelo coletor ao lado de cada tarefa — sem mudar o código.
- O `traceparent` é gravado na outbox e enviado nos cabeçalhos do RabbitMQ: o trace de uma compra continua no Worker que emite o ingresso.
- Métricas de negócio (`ingressa.reservas`, `ingressa.pagamentos`, `ingressa.fila.tamanho`, `ingressa.fila.liberados`) ficam ao lado das técnicas.

## Consequências

- ✅ Um trace mostra a requisição, as consultas SQL, o Redis e o processamento assíncrono.
- ❌ Custo de ingestão na nuvem; a amostragem deve ser ajustada por ambiente (`OTEL_TRACES_SAMPLER`).
