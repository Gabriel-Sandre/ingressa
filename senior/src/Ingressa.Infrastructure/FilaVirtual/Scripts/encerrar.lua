-- Tira o evento da lista de filas ativas, mas só se ninguém estiver esperando nem comprando.
-- Feito em um único script para que ninguém entre na fila entre a verificação e a remoção.
-- KEYS[1] = fila:{evento}:espera
-- KEYS[2] = fila:{evento}:ativos
-- KEYS[3] = filas:ativas
-- ARGV[1] = agora, em milissegundos
-- ARGV[2] = id do evento
-- Retorno: 1 se o evento saiu da lista; 0 se continua ativo.

redis.call('ZREMRANGEBYSCORE', KEYS[2], '-inf', ARGV[1])

if redis.call('ZCARD', KEYS[1]) == 0 and redis.call('ZCARD', KEYS[2]) == 0 then
  redis.call('SREM', KEYS[3], ARGV[2])
  return 1
end

return 0
