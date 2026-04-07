import { test, expect } from '@playwright/test';

test.describe('Trade Platform UI (Mocked Backend)', () => {
  const email = 'mock_user@trade.com';
  const password = 'Password123!';

  test.beforeEach(async ({ page }) => {
    await page.route('**/api/auth/register', async route => {
      await route.fulfill({ status: 200, body: JSON.stringify({}) });
    });

    await page.route('**/api/auth/login*', async route => {
      await route.fulfill({
        status: 200,
        body: JSON.stringify({
          accessToken: 'fake-jwt-token',
          tokenType: 'Bearer',
          expiresIn: 3600,
          refreshToken: 'fake-refresh'
        })
      });
    });

    await page.route('**/api/transactions', async route => {
      await route.fulfill({
        status: 202,
        body: JSON.stringify({
          id: '11111111-1111-1111-1111-111111111111',
          status: 'Pending'
        })
      });
    });

    await page.route('**/api/accounts/my-account', async route => {
      await route.fulfill({ status: 404, body: '' });
    });

    await page.route('**/api/accounts/provision', async route => {
      await route.fulfill({
        status: 200,
        body: JSON.stringify({
          id: 'MOCK-SRC',
          currency: 'USD',
          ownerId: 'mock-owner'
        })
      });
    });

    await page.route('**/api/accounts/my-account/activity', async route => {
      await route.fulfill({ status: 200, body: JSON.stringify([]) });
    });
  });

  test('Completes full flow with mocked backend', async ({ page }) => {
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

    const sourceAccount = page.getByLabel('Source Account');
    await expect(sourceAccount).toHaveValue('MOCK-SRC');
    await page.getByLabel('Target Account').fill('MOCK-TGT');
    await page.getByLabel('Amount').fill('500');

    await page.getByRole('button', { name: 'Submit Transaction' }).click();

    await expect(page.getByText('Success!')).toBeVisible();
    await expect(page.getByText('Transaction ID: 11111111-1111-1111-1111-111111111111')).toBeVisible();
  });
});
