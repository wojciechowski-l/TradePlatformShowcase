import { test, expect, type Page } from '@playwright/test';

async function login(page: Page, email: string, password: string) {
  await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible();
  await page.locator('#auth-email').fill(email);
  await page.locator('#auth-password').fill(password);
  await page.getByRole('button', { name: 'Sign In' }).click();
}

test.describe('Trade Platform UI - Auth Failures', () => {
  test('Shows error message when login fails', async ({ page }) => {
    const email = `bad_user_${Date.now()}@trade.com`;

    await page.goto('/');
    await page.getByRole('button', { name: 'Sign In' }).waitFor();

    await login(page, email, 'WrongPassword!');

    await expect(page.getByRole('alert')).toContainText('Invalid email or password.');
    await expect(page.getByText(`Welcome, ${email}`)).not.toBeVisible();
  });
});
