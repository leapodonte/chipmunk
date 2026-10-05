/** 只读取线上演示工作台，验证 HTTPS、角色导航和响应式布局；秘密不输出。 */
import { chromium, expect } from '@playwright/test';
import { readFileSync, mkdirSync, writeFileSync } from 'node:fs';
const file = process.env.SMILELAB_STAFF_CREDENTIALS;
if (!file) throw new Error('SMILELAB_STAFF_CREDENTIALS must point to a private staff delivery file');
const credentials = JSON.parse(readFileSync(file, 'utf8'));
const target = 'https://app.smilelab.ai';
const output = process.env.SMILELAB_LIVE_EVIDENCE ?? 'test-results/live';
mkdirSync(output, { recursive: true });
const roles = [{ account: 'doctor', label: '医生', panel: '预约与排班', nav: null, endpoint: 'appointments' }, { account: 'manager', label: '门诊经理', panel: '患者档案', nav: '患者档案', endpoint: 'patients' }, { account: 'consultant', label: '咨询顾问', panel: '咨询与跟进', nav: '咨询与跟进', endpoint: 'crm/leads' }, { account: 'operator', label: '平台运维', panel: '集成与审计', nav: '集成与审计', endpoint: 'operations/summary' }];
const browser = await chromium.launch({ channel: process.env.PLAYWRIGHT_CHROME_CHANNEL });
const checks = [];
try {
  for (const role of roles) {
    const context = await browser.newContext({ viewport: role.account === 'consultant' ? { width: 390, height: 844 } : { width: 1440, height: 1000 }, ignoreHTTPSErrors: false });
    try {
      const page = await context.newPage();
      const errors = []; page.on('pageerror', error => errors.push(error.message));
      const responses = [];
      page.on('response', response => { if (new URL(response.url()).pathname === '/api/dso/v1/' + role.endpoint) responses.push(response.status()); });
      await page.goto(target + '/workspace/');
      if (role.account !== 'doctor') { await page.locator('.login-form .ant-select').click(); await page.locator('.ant-select-item-option-content').getByText(role.label, { exact: true }).click(); }
      await page.getByLabel('员工开发密钥', { exact: true }).fill(credentials.accounts[role.account].key);
      const login = page.waitForResponse(response => response.url().endsWith('/api/v1/auth/staff-login'));
      await page.getByRole('button', { name: '登录', exact: true }).click();
      if ((await login).status() !== 200) throw new Error('Staff login failed for ' + role.account);
      await expect(page.getByRole('button', { name: '退出', exact: true })).toBeVisible();
      if (role.nav) await page.getByRole('button', { name: role.nav, exact: true }).click();
      await expect(page.getByRole('heading', { name: role.panel, exact: true })).toBeVisible();
      await expect.poll(() => responses.length).toBeGreaterThan(0);
      if (responses.some(status => status !== 200)) throw new Error('Workspace data request failed for ' + role.account);
      await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
      await expect(page.locator('.ant-alert-error')).toHaveCount(0);
      const dimensions = await page.evaluate(() => ({ page: document.documentElement.scrollWidth, viewport: innerWidth }));
      if (dimensions.page > dimensions.viewport + 2) throw new Error('Workspace overflows viewport for ' + role.account);
      if (errors.length) throw new Error('Workspace emitted browser errors for ' + role.account);
      await page.screenshot({ path: output + '/' + role.account + '.png', fullPage: true });
      await page.getByRole('button', { name: '退出', exact: true }).click();
      await expect(page.getByRole('heading', { name: '进入工作台', exact: true })).toBeVisible();
      checks.push(role.account);
      console.log('PASS public HTTPS workspace:', role.account);
    } finally { await context.close(); }
  }
  writeFileSync(output + '/summary.json', JSON.stringify({ base: target, accounts: checks, passed: checks.length, clinicalWrites: 0, crmWrites: 0, tlsVerified: true }, null, 2) + '\n');
} catch (error) {
  let message = String(error);
  for (const account of Object.values(credentials.accounts)) message = message.replaceAll(account.key, '[REDACTED]');
  console.error(message); process.exitCode = 1;
} finally { await browser.close(); }
