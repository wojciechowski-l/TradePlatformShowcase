describe("Trade Platform UI – Auth Failures (Mocked)", () => {
  const email = "bad_user@trade.com";
  const password = "WrongPassword!";

  beforeEach(() => {
    cy.intercept("POST", "/api/auth/login*", {
      statusCode: 401,
      body: { message: "Login failed. Check credentials." },
    }).as("loginFailed");
  });

  it("Shows error message when login fails", () => {
    cy.visit("/");

    cy.login(email, password);
    cy.wait("@loginFailed");

    cy.get('div[role="alert"]', { timeout: 10000 })
      .should("contain.text", "Login failed. Check credentials.")
      .and("be.visible");

    cy.contains(`Welcome, ${email}`).should("not.exist");
  });
});
