import { expect, test, type Page } from '@playwright/test'

const SENHA = 'Senha@123'

async function entrar(page: Page, email: string) {
  await page.goto('/entrar')
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Senha').fill(SENHA)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page.getByText(/^Olá,/)).toBeVisible()
}

test('cliente reserva, paga e recebe os ingressos', async ({ page }) => {
  await entrar(page, 'cliente@ingressa.dev')

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
  await entrar(page, 'cliente@ingressa.dev')

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
