-- Limite de requisições distribuído (janela fixa), compartilhado por todas as instâncias da API.
-- KEYS[1] = limite:{política}:{partição}:{número da janela}
-- ARGV[1] = limite de requisições na janela
-- ARGV[2] = duração da janela, em milissegundos
-- Retorno: { permitido (1/0), requisições na janela, milissegundos até a janela terminar }

local atual = redis.call('INCR', KEYS[1])
if atual == 1 then
  redis.call('PEXPIRE', KEYS[1], ARGV[2])
end

local ttl = redis.call('PTTL', KEYS[1])
if atual > tonumber(ARGV[1]) then
  return { 0, atual, ttl }
end

return { 1, atual, ttl }
