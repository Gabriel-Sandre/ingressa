# Testes dos scripts Lua

Os scripts da fila virtual e do limitador rodam **dentro do Redis** — é isso que os torna
atômicos. Testá-los com dublês não provaria nada, então este teste os executa contra um
Redis de verdade, com as mesmas chaves e argumentos usados por `FilaVirtualRedis.cs` e
`LimitadorRedis.cs`, incluindo os cenários de concorrência que motivaram a escolha por Lua.

```bash
docker run -d --rm -p 6390:6379 --name redis-testes redis:7.4-alpine
pip install redis
python tests/lua/teste_scripts_lua.py --porta 6390
docker stop redis-testes
```

O que é verificado (o processo termina com código 1 se algo falhar):

| Cenário | O que prova |
|---|---|
| Entrar de novo mantém a posição | Reentrar na fila não pune quem já estava nela |
| Admissão até o limite | Nunca há mais compradores simultâneos que o configurado |
| Passe errado, certo e repetido | O passe é de uso único e ligado ao usuário |
| Situação "comprando" | Recarregar a página durante a compra não manda o comprador para o fim da fila |
| Devolução do passe | Uma reserva que falha não custa o lugar na fila |
| Conclusão e passes vencidos | A vaga volta para o próximo da fila |
| Encerramento da fila | O evento só sai da lista de filas ativas quando ninguém espera nem compra |
| 50 entradas simultâneas | Posições 1..50 sem repetição nem buraco |
| 10 Workers admitindo juntos | Exatamente 20 liberados para um limite de 20 |
| Limitador | 3 permitidas, as seguintes recusadas com o tempo restante correto |

No CI (`.github/workflows/ci.yml`, job **Sênior · scripts Lua**) o Redis sobe como serviço
e este arquivo é executado a cada push.
