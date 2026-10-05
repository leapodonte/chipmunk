import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './tests', timeout: 45000, fullyParallel: false, workers: 1, retries: 0,
  reporter: [['list'], ['json', { outputFile: 'test-results/results.json' }]],
  use: { baseURL: process.env.SMILELAB_TEST_URL ?? 'http://127.0.0.1:18081', channel: process.env.PLAYWRIGHT_CHROME_CHANNEL, headless: true, viewport: { width: 1440, height: 1000 }, trace: 'off', screenshot: 'only-on-failure', ignoreHTTPSErrors: false },
});
