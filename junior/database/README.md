# Banco de dados — versão Júnior

A versão Júnior usa **SQLite**. O arquivo `ingressa.db` é criado automaticamente na primeira execução
(`EnsureCreated`), com dados de exemplo em ambiente de desenvolvimento.

| Arquivo | Conteúdo |
|---|---|
| `schema.sql` | Script SQL gerado pelo EF Core a partir do modelo (`dotnet ef dbcontext script`) |

Para gerar o script novamente:

```bash
cd junior
dotnet tool restore
dotnet ef dbcontext script -p src/Ingressa.Api -o database/schema.sql
```

Para inspecionar o banco, abra `src/Ingressa.Api/ingressa.db` no [DB Browser for SQLite](https://sqlitebrowser.org/)
ou na extensão SQLite do VS Code. O arquivo `.db` não vai para o Git.
