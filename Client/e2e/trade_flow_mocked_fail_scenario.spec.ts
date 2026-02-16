import { test, expect } from '@playwright/test';

test.describe("Trade Platform UI – Auth Failures (Mocked)", () => {
  const email = "bad_user@trade.com";
  const password = "WrongPassword!";

  test.beforeEach(async ({ page }) => {
    await page.route("**/api/auth/login*", async route => {
      await route.fulfill({
        status: 401,
        body: JSON.stringify({ message: "Login failed. Check credentials." }),
      });
    });
  });

  test("Shows error message when login fails", async ({ page }) => {
    await page.goto("/");

    await page.getByRole('button', { name: 'Sign In' }).waitFor();

    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password').fill(password);
    
    await page.getByRole('button', { name: 'Sign In' }).click();

    await expect(page.getByRole('alert')).toContainText("Login failed. Check credentials.");
    await expect(page.getByText(`Welcome, ${email}`)).not.toBeVisible();
  });
});