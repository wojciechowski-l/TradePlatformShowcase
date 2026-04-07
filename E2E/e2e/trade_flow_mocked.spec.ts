import { test, expect, type Page } from '@playwright/test';

async function register(page: Page, email: string, password: string) {
  await page.getByRole('button', { name: /need an account\? register/i }).click();
  await expect(page.getByRole('heading', { name: 'Create Account' })).toBeVisible();
  await page.locator('#register-email').fill(email);
  await page.locator('#register-password').fill(password);
  await page.getByRole('button', { name: 'Register' }).click();

  await expect(page.getByRole('alert')).toContainText('Registration Successful');
  await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible();
}

async function login(page: Page, email: string, password: string) {
  await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible();
  await page.locator('#auth-email').fill(email);
  await page.locator('#auth-password').fill(password);
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.getByRole('heading', { name: 'New Transaction' })).toBeVisible();
}

test.describe('Trade Platform UI', () => {
  test('Registers, logs in, and provisions an account', async ({ page }) => {
    const email = `playwright_smoke_${Date.now()}@trade.com`;
    const password = 'Password123!';

    await page.goto('/');

    await register(page, email, password);
    await login(page, email, password);

    await expect(page.getByLabel('Source Account')).toHaveValue(/ACC-\d+/);
    await expect(page.getByRole('button', { name: 'Logout' })).toBeVisible();
  });
});
