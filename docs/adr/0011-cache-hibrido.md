# ADR 0011 — HybridCache (memória + Redis) para a vitrine

**Status:** aceita · **Versão:** Sênior · **Data:** 2026-09

## Contexto

A vitrine e a página do evento concentram a maior parte das requisições, e todas iam ao banco. Com várias réplicas, um cache só em memória fica inconsistente entre elas.

## Alternativas

1. Sem cache, só índices — já feito na Pleno; o limite passa a ser o número de conexões.
2. `IMemoryCache` — rápido, mas cada réplica invalida só a sua cópia.
3. Só `IDistributedCache` (Redis) — consistente, mas toda leitura faz uma ida à rede.
4. **`HybridCache`: memória local (5 s) + Redis (10 s), com invalidação por *tag*.**
5. CDN na frente da API.

## Decisão

Opção 4.

## Justificativa

- O `HybridCache` evita *cache stampede* (uma só consulta ao banco por chave, mesmo com muitas requisições simultâneas).
- Validade curta: disponibilidade de ingressos pode ficar até 10 s desatualizada na vitrine, mas **a compra sempre consulta o banco** — o cache nunca decide se há estoque.
- Alterações do organizador invalidam as *tags* `eventos` e `evento:{id}` logo após o `SaveChanges`.

## Consequências

- ✅ Leituras repetidas não chegam ao banco.
- ❌ A camada de memória de outras réplicas não recebe a invalidação: por até 5 s uma réplica pode mostrar dado antigo. Aceito para a vitrine.
- ❌ CDN (opção 5) fica como próximo passo se o volume de leitura crescer muito.
