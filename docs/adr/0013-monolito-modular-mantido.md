# ADR 0013 — Continuar sem microsserviços na Sênior

**Status:** aceita · **Versão:** Sênior · **Data:** 2026-09 · **Complementa:** [0004](0004-camadas-sem-microsservicos.md)

## Contexto

O roadmap previa "extração de serviço apenas onde a medição justificar". A versão Sênior trouxe fila virtual, cache e testes de carga — era o momento de decidir se algum módulo deveria virar um serviço separado (candidatos: fila virtual e emissão de ingressos).

## Alternativas

1. Extrair a fila virtual para um serviço próprio.
2. Extrair a emissão de ingressos / notificações.
3. **Manter API + Worker, escalando cada um separadamente.**

## Decisão

Opção 3.

## Justificativa

- A fila virtual já não pesa na API: o estado fica no Redis e a admissão roda no Worker. Um serviço separado acrescentaria uma chamada de rede ao caminho da compra sem ganho de escala.
- A emissão já é assíncrona e isolada no Worker, que escala independente da API.
- Um único time mantém o sistema: microsserviços trariam contratos versionados, deploys coordenados e transações distribuídas sem problema real que os justifique.
- API e Worker já são dois processos com escala própria — o benefício principal que se buscaria numa extração.

## Consequências

- ✅ Um repositório, um modelo de dados, deploy simples.
- ❌ Um bug grave na API pode afetar todas as funções dela. Mitigado com rollback automático no ECS.
- 🔁 Reavaliar se surgir um time separado para uma área ou uma necessidade de escala muito diferente (ex.: check-in no dia do evento).
