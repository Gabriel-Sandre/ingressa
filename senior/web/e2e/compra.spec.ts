import { expect, test, type Page } from '@playwright/test'

const SENHA = 'Senha@123'

/**
 * Cria uma conta nova e já entra com ela. Os projetos "desktop" e "celular" rodam em
 * paralelo contra o mesmo ambiente: se os dois usassem a mesma conta, um roubaria o
 * passe da fila do outro (o passe é por usuário) e o teste ficaria instável.
 */
async function entrarComoNovoCliente(page: Page) {
  const email = `e2e-${Date.now()}-${Math.random().toString(36).slice(2, 7)}@teste.dev`
  await page.goto('/cadastro')
  await page.getByLabel('Nome').fill('Cliente de Teste E2E')
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Senha (mínimo 8 caracteres)').fill(SENHA)
  await page.getByLabel('Confirme a senha').fill(SENHA)
  await page.getByRole('button', { name: 'Criar conta' }).click()
  // Cadastro e login gastam PBKDF2 com 600 mil iterações (proposital); no primeiro acesso,
  // com a API ainda fria, isso passa dos 5 s padrão do Playwright.
  await expect(page.getByText(/^Olá,/)).toBeVisible({ timeout: 30_000 })
}

async function entrar(page: Page, email: string) {
  await page.goto('/entrar')
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Senha').fill(SENHA)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page.getByText(/^Olá,/)).toBeVisible({ timeout: 30_000 })
}

test('cliente reserva, paga e recebe os ingressos', async ({ page }) => {
  await entrarComoNovoCliente(page)

  await page.goto('/?busca=stand-up')
  await page.getByRole('link', { name: /Stand-up/ }).click()
  await page.getByLabel('Quantidade para Mezanino').fill('2')
  await page.getByRole('button', { name: 'Reservar ingressos' }).click()

  await expect(page.getByText(/Reserva garantida por/)).toBeVisible()
  await page.getByLabel(/recusado pelo banco/).check()
  await page.getByRole('button', { name: /^Pagar/ }).click()
  await expect(page.getByRole('alert')).toContainText('recusado')

  await page.getByLabel(/aprovado/).check()
  await page.getByRole('button', { name: /^Pagar/ }).click()
  await expect(page.getByText(/Pagamento confirmado/)).toBeVisible()

  // Os ingressos são emitidos pelo Worker em segundo plano.
  const pedido = page.locator('article').first()
  await expect(pedido.locator('td.codigo')).toHaveCount(2, { timeout: 30_000 })
})

test('evento de alta procura passa pela sala de espera', async ({ page }) => {
  await entrarComoNovoCliente(page)

  await page.goto('/?busca=festival')
  await page.getByRole('link', { name: /Festival de Rock/ }).click()
  await page.getByRole('link', { name: 'Entrar na fila' }).click()

  await expect(page.getByRole('heading', { name: 'Sala de espera' })).toBeVisible()

  // Quando liberado, volta para o evento com o passe e pode reservar.
  await expect(page.getByRole('button', { name: 'Reservar ingressos' })).toBeVisible({ timeout: 30_000 })
  await page.getByLabel('Quantidade para Pista', { exact: true }).fill('1')
  await page.getByRole('button', { name: 'Reservar ingressos' }).click()
  await expect(page.getByText(/Reserva garantida por/)).toBeVisible()
})

test('rotas protegidas e sessão persistente', async ({ page }) => {
  await page.goto('/pedidos')
  await expect(page).toHaveURL(/\/entrar$/)

  await entrar(page, 'cliente@ingressa.dev')
  await page.reload()
  await expect(page.getByText(/^Olá,/)).toBeVisible()
})
