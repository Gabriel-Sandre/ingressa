# Banco de dados — versão Pleno

A versão Pleno usa **PostgreSQL 17** com **migrations** do EF Core
(`src/Ingressa.Infrastructure/Persistencia/Migrations`). A API aplica as migrations pendentes ao iniciar.

| Arquivo | Conteúdo |
|---|---|
| `ingressa-pleno.sql` | Script **idempotente** com todas as migrations (`dotnet ef migrations script --idempotent`) — pode ser executado várias vezes; só aplica o que falta |

Uso típico em produção, quando a aplicação não deve alterar o esquema sozinha:

```bash
psql "host=... dbname=ingressa user=..." -f database/ingressa-pleno.sql
# e na aplicação: Banco__InicializarAoIniciar=false
```

Para gerar o script novamente:

```bash
cd pleno
dotnet tool restore
dotnet ef migrations script --idempotent -p src/Ingressa.Infrastructure -s src/Ingressa.Api -o database/ingressa-pleno.sql
```

Para acessar o banco do Docker Compose: `docker compose exec postgres psql -U ingressa -d ingressa`.
