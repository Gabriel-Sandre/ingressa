-- Consulta a situação de um usuário na fila, sem alterar nada.
-- KEYS[1] = fila:{evento}:espera
-- KEYS[2] = fila:{evento}:passe:{usuario}
-- KEYS[3] = fila:{evento}:passe:{usuario}:usado
-- ARGV[1] = id do usuário
-- Retorno: { "liberado", passe } | { "comprando", "0" } | { "aguardando", posição } | { "fora", "0" }

local passe = redis.call('GET', KEYS[2])
if passe then
  return { 'liberado', passe }
end

-- Passe já consumido: a compra está em andamento. Sem isto, recarregar a página
-- devolveria "fora" e mandaria o comprador para o fim da fila enquanto ele ainda ocupa vaga.
if redis.call('GET', KEYS[3]) then
  return { 'comprando', '0' }
end

local rank = redis.call('ZRANK', KEYS[1], ARGV[1])
if rank then
  return { 'aguardando', tostring(rank + 1) }
end

return { 'fora', '0' }
