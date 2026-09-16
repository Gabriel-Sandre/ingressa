-- Consulta a situação de um usuário na fila, sem alterar nada.
-- KEYS[1] = fila:{evento}:espera
-- KEYS[2] = fila:{evento}:passe:{usuario}
-- ARGV[1] = id do usuário
-- Retorno: { "liberado", passe } | { "aguardando", posição } | { "fora", "0" }

local passe = redis.call('GET', KEYS[2])
if passe then
  return { 'liberado', passe }
end

local rank = redis.call('ZRANK', KEYS[1], ARGV[1])
if rank then
  return { 'aguardando', tostring(rank + 1) }
end

return { 'fora', '0' }
