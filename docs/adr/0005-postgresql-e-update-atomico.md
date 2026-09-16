# ADR 0005 — PostgreSQL e UPDATE condicional para o estoque

**Status:** aceita (substitui a ADR 0002 a partir da versão Pleno) · **Data:** 2026-09

## Contexto

A versão Júnior baixa o estoque com "ler → calcular → gravar". Sob concorrência, isso vende mais lugares do que existem ([experimento](../../pleno/docs/experimentos/concorrencia-no-estoque.md)).

## Alternativas

| Alternativa | Como funciona | Problema |
|---|---|---|
| Lock pessimista (`SELECT ... FOR UPDATE`) | Trava a linha do setor durante a compra | Segura a trava durante toda a lógica; vira fila sob pico |
| Concorrência otimista no setor (versão da linha) | Falha se o setor mudou; o cliente tenta de novo | Em eventos concorridos, a maioria das tentativas falha e precisa repetir |
| Lock distribuído (Redis) | Trava fora do banco | Mais uma peça que pode falhar; o banco continua sendo a verdade |
| **UPDATE condicional atômico** | `SET Ocupados = Ocupados + n WHERE Capacidade - Ocupados >= n` | Não guarda histórico por lugar (aceitável: setores sem assento numerado) |

## Decisão

UPDATE condicional atômico, mais uma restrição `CHECK` como última defesa, em PostgreSQL.
Para o **pedido** (pagar × expirar), concorrência otimista com a coluna `xmin`.

## Consequências

- ✅ Correto sob concorrência sem travas longas; o banco resolve em uma instrução.
- ✅ Nenhuma tentativa válida precisa ser repetida.
- ❌ O `DbContext` não enxerga a mudança feita pelo `ExecuteUpdate` nas entidades já carregadas — por isso a ocupação nunca é alterada pelas entidades, só pelo `IEstoque`.
- ❌ Assentos numerados exigirão outro modelo (uma linha por assento).
- SQLite foi abandonado: ele serializa escritas e esconde esse tipo de problema.
