# Experimento: por que a versão Júnior vende ingresso a mais

Este experimento isola, direto no PostgreSQL, a diferença entre o jeito da versão Júnior e o da versão Pleno de baixar o estoque. Ele não depende da aplicação.

## Montagem

Um setor com **10 lugares** e **200 tentativas de compra de 1 ingresso**, disparadas por **50 conexões ao mesmo tempo**.

```sql
CREATE TABLE "Setores" (
  "Id" serial PRIMARY KEY,
  "Capacidade" int NOT NULL,
  "Ocupados" int NOT NULL DEFAULT 0,
  CONSTRAINT "CK_Setores_Ocupados" CHECK ("Ocupados" >= 0 AND "Ocupados" <= "Capacidade")
);
INSERT INTO "Setores" ("Capacidade") VALUES (10);
```

## Jeito Júnior: ler → calcular → gravar

Cada compra lê `Ocupados`, confere se há lugar e grava `Ocupados + 1`:

```bash
seq 200 | xargs -P 50 -I{} bash -c '
  o=$(psql -tAc "SELECT \"Ocupados\" FROM \"Setores\" WHERE \"Id\"=1")
  if [ "$o" -lt 10 ]; then
    psql -qc "UPDATE \"Setores\" SET \"Ocupados\"=$((o+1)) WHERE \"Id\"=1"
    echo vendeu
  fi' | wc -l
```

| Resultado | Valor |
|---|---|
| Compras aceitas | **200** |
| Valor final de `Ocupados` | **9** |

Muitas conexões leram o mesmo valor antes de qualquer uma gravar. Todas acharam que havia lugar, e as gravações sobrescreveram umas às outras (*lost update*). O sistema confirmou 200 ingressos para 10 lugares e ainda registrou só 9 ocupados.

> Na versão Júnior o problema fica escondido porque o SQLite serializa as escritas. Em um banco de produção, ele aparece.

## Jeito Pleno: um único UPDATE condicional

```bash
seq 200 | xargs -P 50 -I{} psql -tAc '
  UPDATE "Setores" SET "Ocupados" = "Ocupados" + 1
  WHERE "Id" = 1 AND "Capacidade" - "Ocupados" >= 1
  RETURNING 1' | grep -c 1
```

| Resultado | Valor |
|---|---|
| Compras aceitas | **10** |
| Valor final de `Ocupados` | **10** |

O PostgreSQL trava a linha durante o `UPDATE`. Quem chega depois espera, e a condição `Capacidade - Ocupados >= 1` é reavaliada com o valor já atualizado. Não existe intervalo entre "conferir" e "gravar".

## Última linha de defesa

Mesmo que um bug tente ocupar lugares demais, a restrição do banco recusa:

```text
UPDATE "Setores" SET "Ocupados" = 11 WHERE "Id" = 1;
ERROR:  new row for relation "Setores" violates check constraint "CK_Setores_Ocupados"
```

## Onde isso está no código

- `src/Ingressa.Infrastructure/Persistencia/Estoque.cs` — o `ExecuteUpdateAsync` que gera o UPDATE acima.
- `src/Ingressa.Infrastructure/Persistencia/Configuracoes/EventoConfiguracao.cs` — a restrição `CK_Setores_Ocupados`.
- `tests/Ingressa.Api.IntegrationTests/PedidosTests.cs` — 40 clientes HTTP disputando 5 lugares, contra um PostgreSQL real.

*Executado em PostgreSQL 16, setembro de 2026.*
