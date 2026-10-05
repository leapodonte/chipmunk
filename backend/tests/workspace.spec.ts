import { test, expect, type Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
const file = process.env.DSO_TEST_CREDENTIALS;
if (!file) throw new Error('DSO_TEST_CREDENTIALS must point to the private isolated test handoff file');
const fixture = JSON.parse(readFileSync(file, 'utf8')) as { staffKey?: string; staffKeys?: Record<string, string>; staffTokens: Record<string, string>; aliceToken: string; alicePatientId: string };
const runId = Date.now().toString(36);
async function login(page: Page, role = '医生') {
  await page.goto('/workspace/');
  if (role !== '医生') { await page.locator('.login-form .ant-select').click(); await page.locator('.ant-select-item-option-content').getByText(role, { exact: true }).click() }
  const identity = ({ '医生': 'doctor', '门诊经理': 'manager', '咨询顾问': 'consultant', '平台运维': 'operator' } as Record<string, string>)[role];
  await page.getByLabel('员工开发密钥', { exact: true }).fill(fixture.staffKeys?.[identity] ?? fixture.staffKey!);
  const loginResponse = page.waitForResponse(response => response.url().endsWith('/api/v1/auth/staff-login'));
  await page.getByRole('button', { name: '登录', exact: true }).click();
  expect((await loginResponse).status()).toBe(200);
  await expect(page.getByRole('button', { name: '退出', exact: true })).toBeVisible();
}

test('login, permissions, language and calendar render', async ({ page }) => {
  const errors: string[] = []; page.on('pageerror', e => errors.push(e.message));
  await login(page);
  await expect(page.getByRole('heading', { name: '预约与排班' })).toBeVisible();
  await expect(page.locator('.calendar')).toBeVisible();
  await expect(page.locator('.calendar').getByRole('button', { name: '今天', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: '集成与审计', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'English', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Appointments & availability' })).toBeVisible();
  await page.screenshot({ path: 'test-results/appointments-en.png', fullPage: true });
  expect(errors).toEqual([]);
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Open your workspace' })).toBeVisible();
});

test('doctor creates, signs and amends a real clinical note', async ({ page, request }) => {
  const complaint = 'Browser synthetic chief complaint ' + runId;
  const amendment = 'Browser synthetic amendment ' + runId;
  await request.post('/api/v1/patients/me/care-team/d_001', { headers: { Authorization: 'Bearer ' + fixture.aliceToken }, data: {} });
  await login(page);
  await page.getByRole('button', { name: '患者档案', exact: true }).click();
  const row = page.getByRole('row').filter({ hasText: 'Synthetic Alice' });
  await row.getByRole('button', { name: '查看档案' }).click();
  await page.getByRole('button', { name: '新建病历', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.locator('textarea').first().fill(complaint);
  await dialog.getByRole('button', { name: '牙位 11', exact: true }).click();
  await dialog.locator('.tooth-detail input').fill('Synthetic tooth finding');
  await dialog.getByRole('button', { name: '保存', exact: true }).click();
  const card = page.locator('.record-card').filter({ hasText: complaint }).first();
  await expect(card).toBeVisible();
  await card.getByRole('button', { name: '签署病历', exact: true }).click();
  await page.locator('.ant-popconfirm').getByRole('button', { name: '签署病历', exact: true }).click();
  await expect(card.getByText('已签署', { exact: true })).toBeVisible();
  await card.getByRole('button', { name: '创建修订', exact: true }).click();
  await dialog.locator('textarea').first().fill(amendment);
  await dialog.getByRole('button', { name: '保存', exact: true }).click();
  await expect(page.locator('.record-card').filter({ hasText: amendment })).toBeVisible();
  await page.screenshot({ path: 'test-results/clinical-zh.png', fullPage: true });
});

test('consultant creates an opportunity and completes a follow-up', async ({ page }) => {
  const title = 'Browser synthetic opportunity ' + runId;
  await login(page, '咨询顾问');
  await page.getByRole('button', { name: '咨询与跟进', exact: true }).click();
  await page.getByRole('button', { name: '新增线索', exact: true }).click();
  let dialog = page.getByRole('dialog');
  await dialog.locator('input').fill(title);
  await dialog.getByRole('button', { name: '创建', exact: true }).click();
  await expect(page.getByRole('heading', { name: title })).toBeVisible();
  await page.getByRole('button', { name: '新增跟进', exact: true }).click();
  dialog = page.getByRole('dialog');
  await dialog.locator('textarea').fill('Browser synthetic follow-up');
  const tomorrow = new Date(Date.now() + 86400000); tomorrow.setHours(10, 0, 0, 0);
  const inputTime = new Date(tomorrow.getTime() - tomorrow.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
  await dialog.locator('input[type="datetime-local"]').fill(inputTime);
  await dialog.getByRole('button', { name: '创建', exact: true }).click();
  const card = page.locator('.record-card').filter({ hasText: 'Browser synthetic follow-up' });
  await card.getByRole('button', { name: '完成跟进', exact: true }).click();
  await expect(card.getByText('已完成', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: '集成与审计', exact: true })).toHaveCount(0);
  await page.screenshot({ path: 'test-results/crm-zh.png', fullPage: true });
});

test('operator sees delivery states and audit with no clinical workspace', async ({ page }) => {
  await login(page, '平台运维');
  await page.getByRole('button', { name: '集成与审计', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Odoo 投递事件' })).toBeVisible();
  await expect(page.getByRole('heading', { name: '操作审计' })).toBeVisible();
  await expect(page.locator('.stat-card')).toHaveCount(3);
  await expect(page.locator('.ant-table-tbody').first().locator('tr[data-row-key]').first()).toBeVisible();
  await expect(page.locator('.ant-table-tbody').last().locator('tr[data-row-key]').first()).toBeVisible();
  await expect(page.getByRole('button', { name: '患者档案', exact: true })).toHaveCount(0);
  await page.screenshot({ path: 'test-results/operations-zh.png', fullPage: true });
});

test('mobile workspace remains usable', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(page, '咨询顾问');
  await page.getByRole('button', { name: '咨询与跟进', exact: true }).click();
  await expect(page.getByRole('button', { name: '新增线索', exact: true })).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
  expect(overflow).toBe(false);
  await page.screenshot({ path: 'test-results/mobile-crm-zh.png', fullPage: true });
});

test('CRM navigation fetches the next server page', async ({ page, request }) => {
  for (let index = 0; index < 25; index++) {
    const response = await request.post('/api/dso/v1/crm/leads', { headers: { Authorization: 'Bearer ' + fixture.staffTokens.consultant, 'Idempotency-Key': `ui-pagination-${runId}-${index}` }, data: { title: `Synthetic pagination ${runId} ${index}` } });
    expect(response.status()).toBe(200);
  }
  await login(page, '咨询顾问');
  await page.getByRole('button', { name: '咨询与跟进', exact: true }).click();
  const next = page.getByRole('button', { name: '下一页', exact: true });
  await expect(next).toBeEnabled();
  const response = page.waitForResponse(response => response.url().includes('/crm/leads?page=2&pageSize=20'));
  await next.click();
  expect((await response).status()).toBe(200);
  await expect(page.getByText('第 2 页', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: '上一页', exact: true })).toBeEnabled();
});
