-- Encerra a participação do comprador depois de uma reserva bem-sucedida:
-- libera a vaga de "comprador simultâneo" para o próximo da fila.
-- KEYS[1] = fila:{evento}:ativos
-- KEYS[2] = fila:{evento}:passe:{usuario}:usado
-- ARGV[1] = id do usuário

redis.call('ZREM', KEYS[1], ARGV[1])
redis.call('DEL', KEYS[2])
return 1
