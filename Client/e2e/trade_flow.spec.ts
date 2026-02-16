import { test, expect } from '@playwright/test';

test.describe('Trade Platform E2E Flow', () => {
  test('Registers, Logs in, and Submits a Transaction', async ({ page }) => {
    const timestamp = new Date().getTime();
    const email = `playwright_user_${timestamp}@trade.com`;
    const password = 'Password123!';

    await page.goto('/');

    await page.getByRole('button', { name: /need an account\? register/i }).click();

    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password').fill(password);

    page.once('dialog', async dialog => {
      expect(dialog.message()).toContain('Registration Successful');
      await dialog.accept();
    });

    await page.getByRole('button', { name: 'Register' }).click();

    await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible();

    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password').fill(password);
    
    await page.getByRole('button', { name: 'Sign In' }).click();

    await expect(page.getByText(`Welcome, ${email}`)).toBeVisible();

    const sourceInput = page.getByLabel('Source Account');
    await expect(sourceInput).toHaveValue(/ACC-\d+/);

    await page.getByLabel('Target Account').fill('PLAYWRIGHT-TGT');
    await page.getByLabel('Amount').fill('500');

    await page.getByRole('button', { name: 'Submit Transaction' }).click();

    await expect(page.getByText('Success!')).toBeVisible();
    await expect(page.getByText(/Transaction ID:/i)).toBeVisible();
    await expect(page.getByText('Processed')).toBeVisible({ timeout: 15000 });
  });
});