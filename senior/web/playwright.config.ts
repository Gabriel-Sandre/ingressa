import { defineConfig, devices } from '@playwright/test'

// Testes ponta a ponta contra o ambiente completo (docker compose up).
// BASE_URL permite apontar para outro ambiente (ex.: homologação).
export default defineConfig({
  testDir: './e2e',
  timeout: 90_000,
  // Uma repetição: o ambiente completo (banco, filas, duas APIs) roda na mesma máquina do
  // navegador, e uma lentidão pontual não deve reprovar o cenário.
  retries: 1,
  // 5 s (padrão) é pouco para uma reserva real: transação no banco, passe da fila e hash de senha.
  expect: { timeout: 15_000 },
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env.BASE_URL ?? 'http://localhost:8080',
    locale: 'pt-BR',
    timezoneId: 'America/Sao_Paulo',
    trace: 'retain-on-failure',
    launchOptions: process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {},
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'] } },
    { name: 'celular', use: { ...devices['Pixel 7'] } },
  ],
})
