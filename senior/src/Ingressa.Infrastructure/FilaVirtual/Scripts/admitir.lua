-- Libera os próximos da fila, respeitando o limite de compradores simultâneos.
-- Tudo roda de forma atômica no Redis: duas instâncias do Worker nunca liberam a mesma pessoa.
-- KEYS[1] = fila:{evento}:espera   (ZSET ordem de chegada)
-- KEYS[2] = fila:{evento}:ativos   (ZSET membro=usuário, score=instante em que o passe expira, em ms)
-- ARGV[1] = limite de compradores simultâneos
-- ARGV[2] = validade do passe, em milissegundos
-- ARGV[3] = agora, em milissegundos
-- ARGV[4] = prefixo da chave do passe: "fila:{evento}:passe:"
-- ARGV[5..] = passes aleatórios gerados pela aplicação (um por vaga possível)
-- Retorno: { quantidade liberada, tamanho da fila restante }

local limite = tonumber(ARGV[1])
local validade = tonumber(ARGV[2])
local agora = tonumber(ARGV[3])

-- Passes vencidos deixam de ocupar vaga.
redis.call('ZREMRANGEBYSCORE', KEYS[2], '-inf', agora)

local vagas = limite - redis.call('ZCARD', KEYS[2])
local disponiveis = #ARGV - 4
if vagas > disponiveis then
  vagas = disponiveis
end

local liberados = 0
if vagas > 0 then
  local proximos = redis.call('ZPOPMIN', KEYS[1], vagas)
  -- ZPOPMIN devolve { membro1, score1, membro2, score2, ... }
  for i = 1, #proximos, 2 do
    local usuario = proximos[i]
    liberados = liberados + 1
    redis.call('SET', ARGV[4] .. usuario, ARGV[4 + liberados], 'PX', validade)
    redis.call('ZADD', KEYS[2], agora + validade, usuario)
  end
end

return { liberados, redis.call('ZCARD', KEYS[1]) }
