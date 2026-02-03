describe('Trade Platform E2E Flow', () => {
  const timestamp = new Date().getTime();
  const email = `cypress_user_${timestamp}@trade.com`;
  const password = 'Password123!';

  it('Registers, Logs in, and Submits a Transaction', () => {
    cy.visit('/');

    cy.contains('button', /need an account\? register/i).click();

    cy.contains('label', 'Email').parent().find('input').type(email); // Note: "Email Address" might have become just "Email" in AuthScreen.tsx, using partial match 'Email' is safer
    cy.contains('label', 'Password').parent().find('input').type(password);

    cy.contains('button', 'Register').click();

    cy.on('window:alert', (text) => {
      expect(text).to.contain('Registration Successful');
    });

    cy.contains('h5', 'Sign In').should('be.visible');

    cy.contains('label', 'Email').parent().find('input').clear().type(email);
    cy.contains('label', 'Password').parent().find('input').clear().type(password);
    
    cy.contains('button', 'Sign In').click();

    cy.contains(`Welcome, ${email}`).should('be.visible');

    cy.contains('label', 'Source Account').parent().find('input').clear().type('CYPRESS-SRC');
    cy.contains('label', 'Target Account').parent().find('input').clear().type('CYPRESS-TGT');
    
    cy.contains('label', 'Amount').parent().find('input').clear().type('500');

    cy.contains('button', 'Submit Transaction').click();

    cy.contains('Success!').should('be.visible');

    cy.contains(/Transaction ID:/i).should('be.visible');
  });
});