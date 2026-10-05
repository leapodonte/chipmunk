import { test, expect, type Page, type BrowserContext } from '@playwright/test';
import { readFileSync } from 'node:fs';
const fixture = JSON.parse(readFileSync(process.env.DSO_TEST_CREDENTIALS!, 'utf8')) as { devKey: string; staffKeys: Record<string, string> };
const run = Date.now().toString(36);
async function login(page: Page, role: string, account?: string) {
  await page.goto('/workspace/');
  if (role !== '医生') { await page.locator('.login-form .ant-select').click(); await page.locator('.ant-select-item-option-content').getByText(role, { exact: true }).click() }
  if (!account) { await page.getByLabel('患者测试标识', { exact: true }).fill('demo:browser-order-' + run); await page.getByLabel('患者开发密钥', { exact: true }).fill(fixture.devKey) }
  else await page.getByLabel('员工开发密钥', { exact: true }).fill(fixture.staffKeys[account]);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.getByRole('button', { name: '退出', exact: true })).toBeVisible();
  await page.getByRole('button', { name: '共享订单', exact: true }).click();
  await expect(page.getByRole('heading', { name: '共享订单', exact: true })).toBeVisible();
}
async function act(page: Page, action: string, fill?: (page: Page) => Promise<void>) {
  await page.locator('[data-action="' + action + '"]').click();
  if (fill) await fill(page);
  await page.getByRole('dialog').getByRole('button', { name: '确认操作', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.locator('.ant-alert-error')).toHaveCount(0);
}
test('患者、医生、销售、制造和独立质检共用一笔订单及自动更新的完整历程', async ({ browser }) => {
  test.setTimeout(100000);
  const contexts: BrowserContext[] = [];
  try {
    const pages: Record<string, Page> = {};
    for (const [name, role, account] of [['patient','患者（演示）',undefined],['doctor','医生','doctor'],['sales','订单销售','sales'],['manufacturer','制造人员','manufacturer'],['quality','独立质检','quality']] as const) {
      const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } }); contexts.push(context);
      pages[name] = await context.newPage(); await login(pages[name], role, account);
    }
    const patient = pages.patient!;
    await patient.getByRole('button', { name: '发起申请', exact: true }).click();
    const dialog = patient.getByRole('dialog');
    await dialog.locator('textarea').first().fill('Synthetic browser retainer request');
    await dialog.locator('.ant-form-item').filter({ hasText: '收货人' }).locator('input').fill('Synthetic recipient');
    await dialog.locator('.ant-form-item').filter({ hasText: '联系电话' }).locator('input').fill('000000');
    await dialog.locator('textarea').last().fill('Synthetic browser delivery address');
    await dialog.getByRole('button', { name: '发起申请', exact: true }).click();
    const detail = patient.locator('.order-detail'); await expect(detail).toBeVisible();
    const id = await detail.getAttribute('data-order-id'); expect(id).toMatch(/^order_/);
    for (const name of ['doctor','sales','manufacturer','quality']) {
      const page = pages[name]!; await page.getByRole('button', { name: '刷新', exact: true }).click();
      await page.getByRole('row').filter({ hasText: id! }).getByRole('button', { name: '查看进度' }).click();
      await expect(page.locator('.order-detail')).toHaveAttribute('data-order-id', id!);
    }
    await act(pages.doctor!, 'doctor_approve', async page => { await page.getByRole('dialog').locator('textarea').fill('Synthetic clinician-approved specification') });
    await expect(patient.getByTestId('order-status')).toHaveText('待患者付款', { timeout: 10000 });
    await act(patient, 'pay_demo');
    await expect(pages.sales!.getByTestId('order-status')).toHaveText('已付款，待销售验证', { timeout: 10000 });
    await act(pages.sales!, 'sales_validate');
    await expect(pages.manufacturer!.locator('[data-action="manufacturer_validate"]')).toBeVisible({ timeout: 10000 });
    await act(pages.manufacturer!, 'manufacturer_validate');
    await act(pages.manufacturer!, 'start_manufacturing', async page => { await page.getByRole('dialog').locator('input').fill('SYNTHETIC-UI-BATCH') });
    await act(pages.manufacturer!, 'finish_manufacturing');
    await expect(pages.quality!.locator('[data-action="qa_pass"]')).toBeVisible({ timeout: 10000 });
    await act(pages.quality!, 'qa_pass', async page => { for (const box of await page.getByRole('dialog').getByRole('checkbox').all()) await box.check() });
    await expect(pages.manufacturer!.locator('[data-action="ship"]')).toBeVisible({ timeout: 10000 });
    await act(pages.manufacturer!, 'ship', async page => { await page.getByRole('dialog').locator('input').first().fill('Demo carrier'); await page.getByRole('dialog').locator('input').last().fill('SYNTHETIC-UI-TRACK') });
    await expect(patient.locator('[data-action="confirm_delivery"]')).toBeVisible({ timeout: 10000 });
    await act(patient, 'confirm_delivery');
    for (const page of Object.values(pages)) {
      await expect(page.getByTestId('order-status')).toHaveText('已签收', { timeout: 10000 });
      await expect(page.locator('.order-timeline li')).toHaveCount(10);
    }
    await expect(pages.sales!.locator('.order-detail')).not.toContainText('Synthetic clinician-approved specification');
    await expect(pages.quality!.locator('.order-detail')).not.toContainText('Synthetic browser delivery address');
    await patient.screenshot({ path: 'test-results/shared-order-patient.png', fullPage: true });
    await pages.manufacturer!.screenshot({ path: 'test-results/shared-order-production.png', fullPage: true });
  } finally { for (const context of contexts) await context.close() }
});

test('制造订单工作台在手机视口和英文下可用', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 }); await login(page, '制造人员', 'manufacturer');
  await page.getByRole('button', { name: 'English', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Shared orders', exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 2)).toBe(true);
  await page.screenshot({ path: 'test-results/shared-order-mobile-en.png', fullPage: true });
});
