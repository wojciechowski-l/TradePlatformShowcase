import { test, expect } from '@playwright/test';

const apiUrl = process.env.API_URL || 'http://127.0.0.1:8081';

test.describe('Trade Platform E2E Flow', () => {
  test('Registers, Logs in, and Submits a Transaction', async ({ page, request }) => {
    const timestamp = new Date().getTime();
    const email = `playwright_user_${timestamp}@trade.com`;
    const recipientEmail = `playwright_recipient_${timestamp}@trade.com`;
    const password = 'Password123!';

    await request.post(`${apiUrl}/api/auth/register`, {
      data: {
        email: recipientEmail,
        password,
      },
    });

    const loginResponse = await request.post(`${apiUrl}/api/auth/login?useCookies=false`, {
      data: {
        email: recipientEmail,
        password,
      },
    });

    expect(loginResponse.ok()).toBeTruthy();

    const loginPayload = await loginResponse.json();
    const recipientToken = loginPayload.accessToken as string;

    const provisionResponse = await request.post(`${apiUrl}/api/accounts/provision`, {
      headers: {
        Authorization: `Bearer ${recipientToken}`,
      },
    });

    expect(provisionResponse.ok()).toBeTruthy();

    const recipientAccount = await provisionResponse.json();
    const recipientAccountId = recipientAccount.id as string;

    await page.goto('/');

    await page.getByRole('button', { name: /need an account\? register/i }).click();

    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password').fill(password);

    await page.getByRole('button', { name: 'Register' }).click();

    await expect(page.getByRole('alert')).toContainText('Registration Successful');
    await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible();

    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password').fill(password);
    
    await page.getByRole('button', { name: 'Sign In' }).click();

    await expect(page.getByText(`Welcome, ${email}`)).toBeVisible();

    const sourceInput = page.getByLabel('Source Account');
    await expect(sourceInput).toHaveValue(/ACC-\d+/);

    await page.getByLabel('Target Account').fill(recipientAccountId);
    await page.getByLabel('Amount').fill('500');

    await page.getByRole('button', { name: 'Submit Transaction' }).click();

    await expect(page.getByText('Success!')).toBeVisible();
    await expect(page.getByText(/Transaction ID:/i)).toBeVisible();
    await expect(page.getByText(/Outgoing 500 USD/)).toBeVisible({ timeout: 15000 });
    await expect(page.getByText(/^Processed$/)).toBeVisible({ timeout: 15000 });
  });
});
