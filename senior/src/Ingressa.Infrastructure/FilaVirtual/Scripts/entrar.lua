-- Entra na fila virtual de um evento (ou devolve o estado atual, se já estiver nela).
-- KEYS[1] = fila:{evento}:espera   (ZSET  membro=usuário, score=ordem de chegada)
-- KEYS[2] = fila:{evento}:seq      (contador da ordem de chegada)
-- KEYS[3] = fila:{evento}:passe:{usuario}  (passe de compra, com TTL)
-- KEYS[4] = filas:ativas           (SET com os eventos que têm fila)
-- KEYS[5] = fila:{evento}:passe:{usuario}:usado
-- ARGV[1] = id do usuário
-- ARGV[2] = id do evento
-- Retorno: { estado, valor }
--   { "liberado", passe }     já pode comprar
--   { "comprando", "0" }      já usou o passe e está finalizando a compra
--   { "aguardando", posição } posição começa em 1

local passe = redis.call('GET', KEYS[3])
if passe then
  return { 'liberado', passe }
end

if redis.call('GET', KEYS[5]) then
  return { 'comprando', '0' }
end

if redis.call('ZSCORE', KEYS[1], ARGV[1]) == false then
  local ordem = redis.call('INCR', KEYS[2])
  -- NX: quem já está na fila nunca perde o lugar por entrar de novo.
  redis.call('ZADD', KEYS[1], 'NX', ordem, ARGV[1])
end

redis.call('SADD', KEYS[4], ARGV[2])

local rank = redis.call('ZRANK', KEYS[1], ARGV[1])
return { 'aguardando', tostring(rank + 1) }
