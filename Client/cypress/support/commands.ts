Cypress.Commands.add('register', (email: string, password: string) => {
  cy.contains('button', /need an account\? register/i).click();

  cy.contains('label', 'Email')
    .parent()
    .find('input')
    .clear()
    .type(email);

  cy.contains('label', 'Password')
    .parent()
    .find('input')
    .clear()
    .type(password);

  cy.contains('button', 'Register').click();
});

Cypress.Commands.add('login', (email: string, password: string) => {
  cy.contains('h5', 'Sign In').should('be.visible');

  cy.contains('label', 'Email')
    .parent()
    .find('input')
    .clear()
    .type(email);

  cy.contains('label', 'Password')
    .parent()
    .find('input')
    .clear()
    .type(password);

  cy.contains('button', 'Sign In').click();
});
