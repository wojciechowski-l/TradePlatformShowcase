import { test, expect, type Page } from '@playwright/test';

async function register(page: Page, email: string, password: string) {
  await page.getByRole('button', { name: /need an account\? register/i }).click();
  await expect(page.getByRole('heading', { name: 'Create Account' })).toBeVisible();
  await page.locator('#register-email').fill(email);
  await page.locator('#register-password').fill(password);
  await Promise.all([
    page.waitForURL(/\/\?mode=login&authSuccess=/, { timeout: 15000 }),
    page.getByRole('button', { name: 'Register' }).click(),
  ]);

  await expect(page.getByRole('alert')).toContainText('Registration Successful');
  await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible();
}

async function login(page: Page, email: string, password: string) {
  await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible();
  await page.locator('#auth-email').fill(email);
  await page.locator('#auth-password').fill(password);
  await Promise.all([
    page.waitForURL((url) => url.pathname === '/' && url.search === '', { timeout: 15000 }),
    page.getByRole('button', { name: 'Sign In' }).click(),
  ]);

  const sourceAccountInput = page.getByLabel('Source Account');
  await expect(sourceAccountInput).toHaveValue(/ACC-\d+/, { timeout: 15000 });
}

async function logout(page: Page) {
  await Promise.all([
    page.waitForURL((url) => url.pathname === '/' && url.search === '', { timeout: 15000 }),
    page.getByRole('button', { name: 'Logout' }).click(),
  ]);
  await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible();
}

async function registerAndLogin(page: Page, email: string, password: string) {
  await page.goto('/');
  await register(page, email, password);
  await login(page, email, password);
}

test.describe('Trade Platform E2E Flow', () => {
  test('Registers, logs in, and submits a transaction', async ({ page }) => {
    test.setTimeout(120000);

    const timestamp = Date.now();
    const senderEmail = `playwright_user_${timestamp}@trade.com`;
    const recipientEmail = `playwright_recipient_${timestamp}@trade.com`;
    const password = 'Password123!';

    await registerAndLogin(page, recipientEmail, password);

    const recipientAccountInput = page.getByLabel('Source Account');
    await expect(recipientAccountInput).toHaveValue(/ACC-\d+/);
    const recipientAccountId = await recipientAccountInput.inputValue();

    await logout(page);

    await registerAndLogin(page, senderEmail, password);

    const sourceInput = page.getByLabel('Source Account');
    await expect(sourceInput).toHaveValue(/ACC-\d+/);

    await page.getByLabel('Target Account').fill(recipientAccountId);
    await page.getByLabel('Amount').fill('500');

    await page.getByRole('button', { name: 'Submit Transaction' }).click();

    const successAlert = page.getByRole('alert').filter({ hasText: 'Transaction ID:' });
    await expect(successAlert).toBeVisible();
    await expect(successAlert).toContainText('Success!');

    const transactionAlertText = await successAlert.textContent();
    const transactionIdMatch = transactionAlertText?.match(
      /Transaction ID:\s*([0-9a-fA-F-]{36})/
    );

    expect(transactionIdMatch).toBeTruthy();
    const transactionId = transactionIdMatch![1];

    await expect
      .poll(
        async () => {
          const statuses = await page.getByRole('status').allTextContents();
          const transactionStatus = statuses.find((status: string) =>
            status.includes(`Transaction ${transactionId} is now `)
          );

          return transactionStatus ?? '';
        },
        {
          timeout: 90000,
          intervals: [1000, 2000, 5000],
        }
      )
      .toContain(`Transaction ${transactionId} is now Processed!`);
  });
});
